using System.Text.Json;
using Handling.Domain;

namespace Handling.Slices.Handling;

// Converts between the event of contract A and the store record.
public static class InboxEvents {
    public static EventRecord ToRecord(this InboxEvent e) {
        ArgumentNullException.ThrowIfNull(e);
        return new(e.Id, e.Source, e.DeliveryId, e.EventType, e.Action, e.ReceivedAt, e.Payload.GetRawText());
    }

    public static InboxEvent ToEvent(this EventRecord e) {
        ArgumentNullException.ThrowIfNull(e);
        using var payload = JsonDocument.Parse(e.Payload);
        return new InboxEvent {
            Id = e.Id,
            Source = e.Source,
            DeliveryId = e.DeliveryId,
            EventType = e.Type,
            Action = e.Action,
            ReceivedAt = e.ReceivedAt,
            Payload = payload.RootElement.Clone(),
        };
    }
}
