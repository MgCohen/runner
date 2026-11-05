using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Network.Infrastructure;

namespace Traffic.Infrastructure;

// One entry of the log with its stable sequence ID.
public sealed record LoggedEntry(long Seq, TrafficEntry Entry);

// A ring buffer with the capacity of trafficKeep: the oldest entry goes first. Bodies stay only in memory, with the
// value of each JSON key that holds token, secret, password, authorization or key replaced by "•••".
public sealed class TrafficLog(IOptionsMonitor<TrafficOptions> options) : ITrafficSink {
    public const string Hidden = "•••";

    static readonly string[] SecretKeys = ["token", "secret", "password", "authorization", "key"];

    // The body is for a person to read: no escapes for non-ASCII text.
    static readonly JsonSerializerOptions Readable = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    readonly Lock gate = new();
    readonly LinkedList<LoggedEntry> entries = [];
    long seq;

    // Occurs after an entry is added. It can occur on any thread.
    public event Action<LoggedEntry>? Recorded;

    public bool CaptureBodies => options.CurrentValue.CaptureBodies;

    public int Capacity => options.CurrentValue.Capacity;

    public void Record(TrafficEntry entry) {
        ArgumentNullException.ThrowIfNull(entry);
        var kept = entry with { Body = entry.Body is { } body && CaptureBodies ? Redact(body) : null };
        LoggedEntry logged;
        lock (gate) {
            logged = new LoggedEntry(++seq, kept);
            entries.AddFirst(logged);
            TrimLocked();
        }
        Recorded?.Invoke(logged);
    }

    // Applies a smaller capacity at once.
    public void Trim() {
        lock (gate) {
            TrimLocked();
        }
    }

    // The entries, newest first.
    public IReadOnlyList<LoggedEntry> Snapshot() {
        lock (gate) {
            return [.. entries];
        }
    }

    public LoggedEntry? Find(long seq) {
        lock (gate) {
            return entries.FirstOrDefault(e => e.Seq == seq);
        }
    }

    void TrimLocked() {
        while (entries.Count > Capacity) {
            entries.RemoveLast();
        }
    }

    public static string Redact(string body) {
        try {
            if (JsonNode.Parse(body) is not { } node) {
                return body;
            }
            Hide(node);
            return node.ToJsonString(Readable);
        } catch (JsonException) {
            return body;
        }
    }

    static void Hide(JsonNode node) {
        switch (node) {
            case JsonObject obj:
                foreach (var name in obj.Select(p => p.Key).ToList()) {
                    if (SecretKeys.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase))) {
                        obj[name] = Hidden;
                    } else if (obj[name] is { } child) {
                        Hide(child);
                    }
                }
                break;
            case JsonArray array:
                foreach (var child in array.OfType<JsonNode>()) {
                    Hide(child);
                }
                break;
        }
    }
}
