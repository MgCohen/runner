using System.Text.Json;
using Handling.Infrastructure;
using VSA.Domain;
using VSA.Handlers.Requests;

namespace Events.Slices.GetEvent;

sealed class GetEventHandler(HandlingStore store, HandlerCatalog catalog) : RoutedHandler<GetEventRequest, GetEventResponse> {
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    protected override Task<Result<GetEventResponse>> HandleRequestAsync(GetEventRequest request, CancellationToken ct) {
        if (store.Get(request.Id) is not { } stored) {
            return Task.FromResult<Result<GetEventResponse>>(new Error("events.not_found", $"Event {request.Id} is not in the store."));
        }
        var results = stored.Handlers.Select(key => new HandlerResultItem(catalog.Find(key)?.Name ?? key.Split('.')[^1],
            stored.Results.FirstOrDefault(r => r.HandlerKey == key)?.Text ?? "waiting")).ToList();
        var e = stored.Event;
        string payload;
        try {
            using var json = JsonDocument.Parse(e.Payload);
            payload = JsonSerializer.Serialize(json.RootElement, Indented);
        } catch (JsonException) {
            payload = e.Payload;
        }
        return Task.FromResult<Result<GetEventResponse>>(new GetEventResponse(e.Id, e.Source, e.TypeText, stored.State, e.ReceivedAt, stored.Attempts,
            stored.AckedAt, results, payload));
    }
}
