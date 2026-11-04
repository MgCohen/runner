using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Runner.Handlers.Claude;

/// <summary>Starts the <c>claude</c> CLI as a child process.</summary>
public sealed partial class ClaudeLauncher(IOptions<ClaudeOptions> options, ILogger<ClaudeLauncher> logger) : IClaudeLauncher
{
    private readonly ClaudeOptions _options = options.Value;

    public async Task<ClaudeRunResult> RunPrintAsync(ClaudeRequest request, CancellationToken cancellationToken)
    {
        // The prompt goes to stdin. A long page can be larger than the Windows command line limit.
        var result = await RunAsync(PrintArguments(_options), request, request.Prompt,
            _options.PrintTimeout, cancellationToken);
        logger.LogInformation("claude -p exited with code {ExitCode}", result.ExitCode);
        return result;
    }

    public async Task<ClaudeSession> StartRemoteSessionAsync(ClaudeRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name ?? $"{_options.SessionNamePrefix}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}";
        var sessionId = Guid.NewGuid().ToString();
        var result = await RunAsync(BackgroundArguments(_options, name, sessionId, request.Prompt), request,
            null, _options.BackgroundStartTimeout, cancellationToken);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"claude --bg exited with code {result.ExitCode}: {result.Error.Trim()} {result.Output.Trim()}");
        }

        // claude --bg prints the session ID. If the output has no UUID, use the ID from --session-id.
        var printed = UuidPattern().Match(result.Output);
        var id = printed.Success ? printed.Value : sessionId;
        logger.LogInformation("Started background Claude session {SessionId} ({Name})", id, name);
        return new ClaudeSession(id, name, result.Output);
    }

    /// <summary>Returns the arguments for <c>claude -p</c>. The prompt comes from stdin.</summary>
    internal static IReadOnlyList<string> PrintArguments(ClaudeOptions options) =>
        ["-p", "--permission-mode", CheckedMode(options)];

    /// <summary>Returns the arguments for a background Remote Control session.</summary>
    /// <remarks>
    /// The name follows <c>--remote-control</c>, because that option takes an optional value.
    /// <c>--</c> ends the options, so a prompt that starts with a dash stays a prompt.
    /// </remarks>
    internal static IReadOnlyList<string> BackgroundArguments(
        ClaudeOptions options, string name, string sessionId, string prompt) =>
        ["--bg", "--remote-control", name, "--permission-mode", CheckedMode(options),
         "--session-id", sessionId, "--", prompt];

    private static string CheckedMode(ClaudeOptions options) =>
        ClaudeOptions.AllowedPermissionModes.Contains(options.PermissionMode)
            ? options.PermissionMode
            : throw new InvalidOperationException(
                $"Claude:PermissionMode '{options.PermissionMode}' is not allowed. " +
                $"Use one of: {string.Join(", ", ClaudeOptions.AllowedPermissionModes)}.");

    private async Task<ClaudeRunResult> RunAsync(IReadOnlyList<string> arguments, ClaudeRequest request,
        string? stdin, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(_options.Path)
        {
            WorkingDirectory = request.WorkingDirectory ?? _options.WorkingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start '{_options.Path}'.");

        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin.AsMemory(), cancellationToken);
        }
        process.StandardInput.Close();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            throw new TimeoutException($"claude did not exit in {timeout}.");
        }

        return new ClaudeRunResult(process.ExitCode, await output, await error);
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex UuidPattern();
}
