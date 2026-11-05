using System.Diagnostics;
using Claude.Infrastructure;

namespace App.Host;

// Real mode with no terminal: the app gets its own tokens. runner.secrets.env (next to App.Host.exe) holds
// KEY=op://... references only. For each KEY that the environment does not have (tray.ps1 sets them with op run),
// the app runs `op read` in the visible console (T3). OP_SERVICE_ACCOUNT_TOKEN and Claude__WorkingDirectory come
// from the user environment, as a process that the desktop shortcut starts has the variables of the log-on time.
public static class Secrets {
    public static IReadOnlyList<string> Load() {
        var problems = new List<string>();
        RefreshPath();
        CopyUserVariable("OP_SERVICE_ACCOUNT_TOKEN", "OP_SERVICE_ACCOUNT_TOKEN");
        CopyUserVariable("Claude__WorkingDirectory", "Modules__Claude__WorkingDirectory");
        var file = Path.Combine(AppContext.BaseDirectory, "runner.secrets.env");
        if (!File.Exists(file)) {
            problems.Add($"{file} is missing, so the app has no tokens.");
            return problems;
        }
        var missing = File.ReadAllLines(file)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#') && line.Contains('=', StringComparison.Ordinal))
            .Select(line => (Key: line[..line.IndexOf('=', StringComparison.Ordinal)], Ref: line[(line.IndexOf('=', StringComparison.Ordinal) + 1)..]))
            .Where(s => s.Ref.StartsWith("op://", StringComparison.Ordinal) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(s.Key)))
            .ToList();
        if (missing.Count == 0) {
            return problems;
        }
        using var console = VisibleConsole.Open("Runner · op read");
        Console.WriteLine("Runner: reading the tokens from 1Password.");
        foreach (var (key, reference) in missing) {
            var (code, value, error) = Read(reference);
            if (code == 0 && value.Length > 0) {
                Environment.SetEnvironmentVariable(key, value);
            } else {
                problems.Add($"op read for {key} failed ({code}): {error.Trim()}");
            }
        }
        return problems;
    }

    static (int Code, string Value, string Error) Read(string reference) {
        var start = new ProcessStartInfo("op") {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("read");
        start.ArgumentList.Add(reference);
        try {
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromSeconds(30))) {
                process.Kill(entireProcessTree: true);
                return (-1, "", "timeout after 30 s");
            }
            return (process.ExitCode, output.Result.Trim(), error.Result);
        } catch (System.ComponentModel.Win32Exception e) {
            return (-1, "", $"op is not on the PATH ({e.Message})");
        }
    }

    // Explorer keeps the PATH of its own start, so a tool that was installed later (op, claude) is not on the PATH
    // of a shortcut start. Add the saved machine and user PATH entries that the process does not have.
    public static void RefreshPath() {
        var current = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        var known = new HashSet<string>(current.Select(Normal), StringComparer.OrdinalIgnoreCase);
        foreach (var target in new[] { EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User }) {
            var saved = Environment.GetEnvironmentVariable("PATH", target) ?? "";
            foreach (var entry in Environment.ExpandEnvironmentVariables(saved).Split(';', StringSplitOptions.RemoveEmptyEntries)) {
                if (known.Add(Normal(entry))) {
                    current.Add(entry);
                }
            }
        }
        Environment.SetEnvironmentVariable("PATH", string.Join(';', current));

        static string Normal(string entry) => entry.Trim().TrimEnd('\\');
    }

    static void CopyUserVariable(string userName, string processName) {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(processName))
            && Environment.GetEnvironmentVariable(userName, EnvironmentVariableTarget.User) is { Length: > 0 } value) {
            Environment.SetEnvironmentVariable(processName, value);
        }
    }
}
