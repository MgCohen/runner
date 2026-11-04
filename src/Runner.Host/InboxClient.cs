using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Runner.Abstractions;

namespace Runner.Host;

/// <summary>HTTP calls to the inbox API (contract A, catch-up and ack).</summary>
public sealed class InboxClient
{
    public const int PageSize = 200;

    private readonly HttpClient _http;
    private readonly string _consumer;

    public InboxClient(HttpClient http, IOptions<InboxOptions> options)
    {
        var o = options.Value;
        _http = http;
        _http.BaseAddress = o.BaseUrl;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", o.Token);
        _consumer = o.ResolvedConsumer;
    }

    /// <summary>Reads one page of pending events with payload, oldest first.</summary>
    public async Task<IReadOnlyList<InboxEvent>> GetPendingAsync(long after, CancellationToken cancellationToken)
    {
        var url = $"/api/events?consumer={Uri.EscapeDataString(_consumer)}&pending=1&payload=1&after={after}&limit={PageSize}";
        var page = await _http.GetFromJsonAsync<EventPage>(url, cancellationToken);
        return page?.Events ?? [];
    }

    /// <summary>Marks the events as consumed. Throws if the inbox does not accept the call.</summary>
    public async Task ConsumeAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(
            "/api/consume", new ConsumeRequest(_consumer, ids), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private sealed record EventPage([property: JsonPropertyName("events")] List<InboxEvent> Events);

    private sealed record ConsumeRequest(
        [property: JsonPropertyName("consumer")] string Consumer,
        [property: JsonPropertyName("ids")] IReadOnlyCollection<long> Ids);
}
