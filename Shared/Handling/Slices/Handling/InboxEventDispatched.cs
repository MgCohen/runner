using VSA.Contracts.Events;

namespace Handling.Slices.Handling;

// A handler lane runs one attempt of one handler for one event. The Events module publishes it; it is here because
// each InboxHandler handles it (a documented exception to "an event is in the slice that publishes it").
public sealed record InboxEventDispatched(InboxEvent Event, string HandlerKey, int Attempt) : IntegrationEvent;
