using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Claude.Infrastructure;

// A background Remote Control session. Open it on claude.ai, or with claude attach <id>.
public sealed record ClaudeSession(string SessionId, string Name);

// Starts background sessions with claude --bg. Only the Background modes use it; the default modes use claude -p.
public interface IClaudeLauncher {
    Task<ClaudeSession> StartRemoteSessionAsync(string prompt, string? workingDirectory, string name, CancellationToken ct);
}

public sealed partial class ClaudeLauncher(IOptions<ClaudeOptions> options) : IClaudeLauncher {
    public async Task<ClaudeSession> StartRemoteSessionAsync(string prompt, string? workingDirectory, string name, CancellationToken ct) {
        var o = options.Value;
        // The name follows --remote-control, because that option takes an optional value. "--" ends the options.
        var output = await ClaudeProcess.RunAsync(o.Path, ["--bg", "--remote-control", name, "-n", name, "--permission-mode", o.PermissionMode, "--", prompt],
            workingDirectory ?? o.WorkingDirectory, o.BackgroundStartTimeout, ct);
        var id = ParseBackgroundId(output) ?? throw new InvalidOperationException($"claude --bg printed no session ID: {output.Trim()}");
        return new ClaudeSession(id, name);
    }

    // claude --bg prints "backgrounded · <id>".
    public static string? ParseBackgroundId(string output) {
        var match = BackgroundId().Match(output);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"backgrounded\s*·\s*([0-9a-f]+)")]
    private static partial Regex BackgroundId();
}

static class ClaudeProcess {
    // Runs claude and returns its output. A non-zero exit code throws.
    public static async Task<string> RunAsync(string path, IReadOnlyList<string> arguments, string? workingDirectory, TimeSpan timeout, CancellationToken ct) {
        var start = new ProcessStartInfo(path) {
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start '{path}'.");
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        try {
            await process.WaitForExitAsync(limit.Token);
        } catch (OperationCanceledException) {
            process.Kill(entireProcessTree: true);
            throw;
        }
        if (process.ExitCode != 0) {
            throw new InvalidOperationException($"claude {arguments[0]} exited with code {process.ExitCode}: {(await error).Trim()}");
        }
        return await output;
    }
}
