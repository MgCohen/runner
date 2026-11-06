using VSA.Contracts.Events;

namespace Events.Slices.StoreEvent;

// A new event is in the local store.
public sealed record EventStored(long InboxId, string Source) : IntegrationEvent;
