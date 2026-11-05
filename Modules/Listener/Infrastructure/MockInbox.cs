using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace Listener.Infrastructure;

// Mock mode: a fake inbox Worker in this process. It keeps the events that are not consumed, as the real inbox does,
// pushes each new event to the open socket, and answers catch-up, ack and the source list. The self-test can cut the network.
public sealed class MockInbox : IInboxSocket, INetworkState {
    public static readonly Uri BaseAddress = new("https://inbox.mock.invalid");

    public static readonly IReadOnlyList<string> SourceNames = ["notion", "github", "linear", "slack", "stripe", "calendar"];

    readonly Lock gate = new();
    readonly SortedDictionary<long, string> pending = [];
    Channel<string>? live;
    long next = 9_000_000 + (DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 100_000 * 10);

    public bool IsUp { get; private set; } = true;

    // The IDs that the runner acked.
    public IReadOnlyCollection<long> Consumed { get { lock (gate) { return [.. consumed]; } } }

    readonly HashSet<long> consumed = [];

    public void SetNetwork(bool up) {
        lock (gate) {
            IsUp = up;
            if (!up) {
                live?.Writer.TryComplete();
                live = null;
            }
        }
    }

    // Adds an event to the inbox and pushes it to the open socket. Returns its ID. Age: the event came that long ago.
    public long Send(string source, string? type, string? action, string payload, TimeSpan? age = null) {
        long id;
        string json;
        lock (gate) {
            id = ++next;
            json = new JsonObject {
                ["id"] = id,
                ["source"] = source,
                ["delivery_id"] = Guid.NewGuid().ToString(),
                ["event_type"] = type,
                ["action"] = action,
                ["received_at"] = (DateTimeOffset.UtcNow - (age ?? TimeSpan.Zero)).ToString("o", CultureInfo.InvariantCulture),
                ["payload"] = JsonNode.Parse(payload),
            }.ToJsonString();
            pending[id] = json;
            live?.Writer.TryWrite($$"""{"kind":"event","event":{{json}}}""");
        }
        return id;
    }

    // Pushes an event again with the same ID, as a duplicate delivery.
    public void SendAgain(long id) {
        lock (gate) {
            if (Stored(id) is { } json) {
                live?.Writer.TryWrite($$"""{"kind":"event","event":{{json}}}""");
            }
        }
    }

    readonly Dictionary<long, string> all = [];

    string? Stored(long id) => all.TryGetValue(id, out var json) ? json : pending.GetValueOrDefault(id);

    public Task<IInboxConnection> ConnectAsync(CancellationToken ct) {
        lock (gate) {
            if (!IsUp) {
                throw new HttpRequestException("The mock network is down.");
            }
            live?.Writer.TryComplete();
            live = Channel.CreateUnbounded<string>();
            live.Writer.TryWrite("""{"kind":"hello","consumer":"mock"}""");
            return Task.FromResult<IInboxConnection>(new Connection(live));
        }
    }

    public HttpMessageHandler Api() => new ApiHandler(this);

    sealed class Connection(Channel<string> channel) : IInboxConnection {
        public async Task<string?> ReceiveAsync(CancellationToken ct) {
            try {
                return await channel.Reader.ReadAsync(ct);
            } catch (ChannelClosedException) {
                throw new HttpRequestException("The mock socket closed.");
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    sealed class ApiHandler(MockInbox inbox) : HttpMessageHandler {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            if (!inbox.IsUp) {
                throw new HttpRequestException("The mock network is down.");
            }
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/api/events", StringComparison.Ordinal)) {
                var after = long.Parse(System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["after"] ?? "0", CultureInfo.InvariantCulture);
                string[] events;
                lock (inbox.gate) {
                    events = [.. inbox.pending.Where(p => p.Key > after).Select(p => p.Value)];
                }
                return Json($$"""{"events":[{{string.Join(",", events)}}]}""");
            }
            if (path == "/api/consume") {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                lock (inbox.gate) {
                    foreach (var id in body.RootElement.GetProperty("ids").EnumerateArray().Select(i => i.GetInt64())) {
                        if (inbox.pending.Remove(id, out var json)) {
                            inbox.all[id] = json;
                        }
                        inbox.consumed.Add(id);
                    }
                }
                return Json("""{"consumed":[],"already":[],"unknown":[]}""");
            }
            return Json(JsonSerializer.Serialize(new { sources = SourceNames }));
        }

        static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
