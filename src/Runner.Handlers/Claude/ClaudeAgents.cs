using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Runner.Handlers.Claude;

/// <summary>One entry of <c>claude agents --json</c>.</summary>
public sealed record ClaudeAgent(string Id, string? SessionId, string? State, string? Status, string? Cwd);

/// <summary>Reads the state of background sessions on this computer.</summary>
public interface IClaudeAgents
{
    /// <summary>Returns the sessions from <c>claude agents --json</c>.</summary>
    Task<IReadOnlyList<ClaudeAgent>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Returns the claude.ai link of a Remote Control session from <c>claude logs</c>, or null.</summary>
    Task<string?> GetRemoteLinkAsync(string id, CancellationToken cancellationToken);

    /// <summary>Returns the text of the last answer in the session transcript, or null.</summary>
    string? ReadLastAnswer(string sessionId);
}

public sealed partial class ClaudeAgents(IOptions<ClaudeOptions> options) : IClaudeAgents
{
    private readonly ClaudeOptions _options = options.Value;

    /// <summary>Background states that mean the session does no more work without a person.</summary>
    public static readonly IReadOnlySet<string> EndStates =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "done", "blocked", "failed", "error", "stopped", "exited" };

    public async Task<IReadOnlyList<ClaudeAgent>> ListAsync(CancellationToken cancellationToken)
    {
        var output = await RunAsync(["agents", "--json"], cancellationToken);
        return Parse(output);
    }

    public async Task<string?> GetRemoteLinkAsync(string id, CancellationToken cancellationToken)
    {
        var output = await RunAsync(["logs", id], cancellationToken);
        var match = RemoteLinkPattern().Match(output);
        return match.Success ? match.Value : null;
    }

    public string? ReadLastAnswer(string sessionId)
    {
        var projects = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
        if (!Directory.Exists(projects)) return null;

        var file = Directory.EnumerateDirectories(projects)
            .Select(d => Path.Combine(d, sessionId + ".jsonl"))
            .FirstOrDefault(File.Exists);
        return file is null ? null : LastAnswer(File.ReadLines(file));
    }

    /// <summary>Reads the list that <c>claude agents --json</c> prints.</summary>
    internal static IReadOnlyList<ClaudeAgent> Parse(string json)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return [];
        return document.RootElement.EnumerateArray()
            .Where(a => a.ValueKind == JsonValueKind.Object && a.TryGetProperty("id", out _))
            .Select(a => new ClaudeAgent(
                Text(a, "id") ?? "", Text(a, "sessionId"), Text(a, "state"), Text(a, "status"), Text(a, "cwd")))
            .ToList();
    }

    /// <summary>Returns the text of the last assistant message in transcript lines (JSONL), or null.</summary>
    internal static string? LastAnswer(IEnumerable<string> lines)
    {
        string? last = null;
        foreach (var line in lines)
        {
            if (!line.Contains("\"assistant\"", StringComparison.Ordinal)) continue;
            try
            {
                using var entry = JsonDocument.Parse(line);
                var root = entry.RootElement;
                if (Text(root, "type") != "assistant"
                    || !root.TryGetProperty("message", out var message)
                    || !message.TryGetProperty("content", out var content)
                    || content.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                var text = string.Join("\n", content.EnumerateArray()
                    .Where(c => Text(c, "type") == "text")
                    .Select(c => Text(c, "text"))
                    .Where(t => !string.IsNullOrWhiteSpace(t))).Trim();
                if (text.Length > 0) last = text;
            }
            catch (JsonException)
            {
                // A line that is not complete JSON. The session can still write to the file.
            }
        }
        return last;
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private async Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(_options.Path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start '{_options.Path}'.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        await error;
        return await output;
    }

    [GeneratedRegex(@"https://claude\.ai/code/session_[A-Za-z0-9]+")]
    private static partial Regex RemoteLinkPattern();
}
