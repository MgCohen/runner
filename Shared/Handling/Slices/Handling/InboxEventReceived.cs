using VSA.Contracts.Events;

namespace Handling.Slices.Handling;

// The Listener got an event from the socket or from catch-up.
public sealed record InboxEventReceived(InboxEvent Event) : IntegrationEvent;
