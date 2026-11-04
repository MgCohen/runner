using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using Runner.Handlers.Tasks;

namespace Runner.Handlers.Claude;

/// <summary>Runs <c>claude -p</c> in the console of the current process and returns its answer.</summary>
public interface IClaudePrint
{
    Task<ClaudeOutput> RunAsync(string prompt, string? workingDirectory, CancellationToken cancellationToken);
}

/// <summary>
/// Runs <c>claude -p</c> as a child of the current console. Each line of the answer shows in that window,
/// so the person who started the program sees the run. The prompt goes to stdin.
/// </summary>
public sealed class ClaudePrint(IOptions<ClaudeOptions> options) : IClaudePrint
{
    private readonly ClaudeOptions _options = options.Value;

    public async Task<ClaudeOutput> RunAsync(string prompt, string? workingDirectory, CancellationToken cancellationToken)
    {
        if (!ClaudeOptions.AllowedPermissionModes.Contains(_options.PermissionMode))
        {
            throw new InvalidOperationException($"Claude:PermissionMode '{_options.PermissionMode}' is not allowed.");
        }

        var start = new ProcessStartInfo(_options.Path)
        {
            WorkingDirectory = workingDirectory ?? _options.WorkingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "-p", "--permission-mode", _options.PermissionMode })
        {
            start.ArgumentList.Add(argument);
        }

        Console.WriteLine($"> claude -p --permission-mode {_options.PermissionMode}   (folder: {start.WorkingDirectory})");
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start '{_options.Path}'.");
        await process.StandardInput.WriteAsync(prompt.AsMemory(), cancellationToken);
        process.StandardInput.Close();

        var text = new StringBuilder();
        while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
        {
            Console.WriteLine(line);
            text.AppendLine(line);
        }
        await process.WaitForExitAsync(cancellationToken);
        return new ClaudeOutput(process.ExitCode, text.ToString());
    }
}
