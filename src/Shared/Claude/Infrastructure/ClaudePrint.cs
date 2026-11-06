using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Claude.Infrastructure;

public sealed record ClaudeOutput(int ExitCode, string Text);

// Runs claude -p and returns its answer.
public interface IClaudePrint {
    Task<ClaudeOutput> RunAsync(string prompt, string? workingDirectory, CancellationToken ct);
}

// Runs claude -p as a child of this process, with no CreateNoWindow (requirement T3): its output shows in a visible
// console window (VisibleConsole). The prompt goes to stdin. A canceled token stops the process tree.
public sealed partial class ClaudePrint(IOptions<ClaudeOptions> options, ILogger<ClaudePrint> log) : IClaudePrint {
    public async Task<ClaudeOutput> RunAsync(string prompt, string? workingDirectory, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(prompt);
        var o = options.Value;
        var start = new ProcessStartInfo(o.Path) {
            WorkingDirectory = workingDirectory ?? o.WorkingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "-p", "--permission-mode", o.PermissionMode }) {
            start.ArgumentList.Add(argument);
        }
        using var console = VisibleConsole.Open("Runner · claude -p");
        await Console.Out.WriteLineAsync($"> claude -p --permission-mode {o.PermissionMode}   (folder: {start.WorkingDirectory})");
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start '{o.Path}'.");
        Started(log, process.Id, start.WorkingDirectory);
        var watch = Stopwatch.StartNew();
        try {
            await process.StandardInput.WriteAsync(prompt.AsMemory(), ct);
            process.StandardInput.Close();
            var text = new StringBuilder();
            while (await process.StandardOutput.ReadLineAsync(ct) is { } line) {
                await Console.Out.WriteLineAsync(line);
                text.AppendLine(line);
            }
            await process.WaitForExitAsync(ct);
            Exited(log, process.Id, process.ExitCode, (long)watch.Elapsed.TotalSeconds);
            return new ClaudeOutput(process.ExitCode, text.ToString());
        } catch (OperationCanceledException) {
            Stopped(log, process.Id);
            process.Kill(entireProcessTree: true);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "claude -p started: pid {Pid}, folder {Folder}")]
    static partial void Started(ILogger logger, int pid, string folder);

    [LoggerMessage(Level = LogLevel.Information, Message = "claude -p ended: pid {Pid}, exit code {Code}, {Seconds} s")]
    static partial void Exited(ILogger logger, int pid, int code, long seconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "claude -p stopped: pid {Pid} (cancelled)")]
    static partial void Stopped(ILogger logger, int pid);
}
