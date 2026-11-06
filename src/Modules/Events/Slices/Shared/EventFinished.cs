using VSA.Contracts.Events;

namespace Events.Slices.Common;

// Each matching handler has a final result. Status: done or failed. The Listener acks the event.
// A duplicate of a finished event also gives this event, so the Listener acks it again.
public sealed record EventFinished(long InboxId, string Status) : IntegrationEvent;
