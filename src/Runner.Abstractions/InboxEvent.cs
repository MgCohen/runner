using System.Text.Json;
using System.Text.Json.Serialization;

namespace Runner.Abstractions;

/// <summary>
/// One event from the inbox. The live push and the catch-up API use the same shape
/// (CONTRACTS.md, contract A).
/// </summary>
public sealed record InboxEvent
{
    [JsonPropertyName("id")] public required long Id { get; init; }
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("delivery_id")] public required string DeliveryId { get; init; }
    [JsonPropertyName("event_type")] public string? EventType { get; init; }
    [JsonPropertyName("action")] public string? Action { get; init; }
    [JsonPropertyName("received_at")] public required DateTimeOffset ReceivedAt { get; init; }

    /// <summary>The raw JSON body that the source sent.</summary>
    [JsonPropertyName("payload")] public required JsonElement Payload { get; init; }
}
