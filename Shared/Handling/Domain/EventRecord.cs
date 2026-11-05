namespace Handling.Domain;

// One event of the inbox Worker in the local store. Payload is the raw JSON body of the source.
public sealed record EventRecord(long Id, string Source, string DeliveryId, string? Type, string? Action, DateTimeOffset ReceivedAt, string Payload) {
    // The type text that the views show, for example "page.properties_updated".
    public string TypeText => Action is null ? Type ?? "" : $"{Type}.{Action}";
}
