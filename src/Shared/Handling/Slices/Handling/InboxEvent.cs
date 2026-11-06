using System.Text.Json;
using System.Text.Json.Serialization;

namespace Handling.Slices.Handling;

// One event of the inbox Worker, in the shape of contract A (docs/CONTRACTS.md). Id increases: it is the dedup key and the order key.
public sealed record InboxEvent {
    [JsonPropertyName("id")] public required long Id { get; init; }
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("delivery_id")] public required string DeliveryId { get; init; }
    [JsonPropertyName("event_type")] public string? EventType { get; init; }
    [JsonPropertyName("action")] public string? Action { get; init; }
    [JsonPropertyName("received_at")] public required DateTimeOffset ReceivedAt { get; init; }

    // The raw JSON body that the source sent.
    [JsonPropertyName("payload")] public required JsonElement Payload { get; init; }
}
