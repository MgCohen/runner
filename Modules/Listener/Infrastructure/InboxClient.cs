using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Listener.Infrastructure;

// The HTTP calls to the inbox API (contract A): catch-up, ack and the source list.
public sealed class InboxClient {
    public const int PageSize = 200;

    readonly HttpClient http;
    readonly string consumer;

    public InboxClient(HttpClient http, IOptions<ListenerOptions> options) {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        var o = options.Value;
        this.http = http;
        http.BaseAddress = o.BaseUrl;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", o.Token);
        consumer = ConsumerName.Resolve(o.Consumer);
    }

    // One page of pending events with payload, oldest first. Each event is the JSON of contract A.
    public async Task<IReadOnlyList<JsonElement>> GetPendingAsync(long after, CancellationToken ct) {
        var url = new Uri($"/api/events?consumer={Uri.EscapeDataString(consumer)}&pending=1&payload=1&after={after}&limit={PageSize}", UriKind.Relative);
        var page = await http.GetFromJsonAsync<EventPage>(url, ct);
        return page?.Events ?? [];
    }

    // Marks the events as consumed. Throws if the inbox refuses the call.
    public async Task ConsumeAsync(IReadOnlyCollection<long> ids, CancellationToken ct) {
        using var response = await http.PostAsJsonAsync(new Uri("/api/consume", UriKind.Relative), new ConsumeRequest(consumer, ids), ct);
        response.EnsureSuccessStatusCode();
    }

    // The source list of the inbox (GET /).
    public async Task<IReadOnlyList<string>> GetSourcesAsync(CancellationToken ct) =>
        (await http.GetFromJsonAsync<SourceList>(new Uri("/", UriKind.Relative), ct))?.Sources ?? [];

    sealed record EventPage([property: JsonPropertyName("events")] List<JsonElement> Events);

    sealed record ConsumeRequest([property: JsonPropertyName("consumer")] string Consumer, [property: JsonPropertyName("ids")] IReadOnlyCollection<long> Ids);

    sealed record SourceList([property: JsonPropertyName("sources")] List<string> Sources);
}
