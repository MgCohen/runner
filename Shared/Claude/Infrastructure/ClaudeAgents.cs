using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Claude.Infrastructure;

// One entry of claude agents --json.
public sealed record ClaudeAgent(string Id, string? SessionId, string? State);

// Reads the state of the background sessions on this PC.
public interface IClaudeAgents {
    Task<IReadOnlyList<ClaudeAgent>> ListAsync(CancellationToken ct);

    // The claude.ai link of a Remote Control session, or null.
    Task<string?> GetRemoteLinkAsync(string id, CancellationToken ct);

    // The text of the last answer in the session transcript, or null.
    string? ReadLastAnswer(string sessionId);
}

public sealed partial class ClaudeAgents(IOptions<ClaudeOptions> options) : IClaudeAgents {
    // States that mean the session does no more work without a person.
    public static readonly IReadOnlySet<string> EndStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "done", "blocked", "failed", "error", "stopped", "exited" };

    public async Task<IReadOnlyList<ClaudeAgent>> ListAsync(CancellationToken ct) =>
        Parse(await ClaudeProcess.RunAsync(options.Value.Path, ["agents", "--json"], null, TimeSpan.FromSeconds(30), ct));

    public async Task<string?> GetRemoteLinkAsync(string id, CancellationToken ct) {
        var match = RemoteLink().Match(await ClaudeProcess.RunAsync(options.Value.Path, ["logs", id], null, TimeSpan.FromSeconds(30), ct));
        return match.Success ? match.Value : null;
    }

    public string? ReadLastAnswer(string sessionId) {
        var projects = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
        if (!Directory.Exists(projects)) {
            return null;
        }
        var file = Directory.EnumerateDirectories(projects).Select(d => Path.Combine(d, sessionId + ".jsonl")).FirstOrDefault(File.Exists);
        return file is null ? null : LastAnswer(File.ReadLines(file));
    }

    public static IReadOnlyList<ClaudeAgent> Parse(string json) {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) {
            return [];
        }
        return [.. document.RootElement.EnumerateArray()
            .Where(a => a.ValueKind == JsonValueKind.Object && a.TryGetProperty("id", out _))
            .Select(a => new ClaudeAgent(Text(a, "id") ?? "", Text(a, "sessionId"), Text(a, "state")))];
    }

    // The text of the last assistant message in transcript lines (JSONL), or null.
    public static string? LastAnswer(IEnumerable<string> lines) {
        ArgumentNullException.ThrowIfNull(lines);
        string? last = null;
        foreach (var line in lines.Where(l => l.Contains("\"assistant\"", StringComparison.Ordinal))) {
            try {
                using var entry = JsonDocument.Parse(line);
                var root = entry.RootElement;
                if (Text(root, "type") != "assistant" || !root.TryGetProperty("message", out var message)
                    || !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) {
                    continue;
                }
                var text = string.Join("\n", content.EnumerateArray().Where(c => Text(c, "type") == "text").Select(c => Text(c, "text"))
                    .Where(t => !string.IsNullOrWhiteSpace(t))).Trim();
                if (text.Length > 0) {
                    last = text;
                }
            } catch (JsonException) {
                // A line that is not complete JSON: the session can still write to the file.
            }
        }
        return last;
    }

    static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    [GeneratedRegex(@"https://claude\.ai/code/session_[A-Za-z0-9]+")]
    private static partial Regex RemoteLink();
}
