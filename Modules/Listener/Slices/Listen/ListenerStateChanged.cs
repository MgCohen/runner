using VSA.Contracts.Events;

namespace Listener.Slices.Listen;

// The listener state changed. State: Paused, Connecting, Connected or Error. Reason: the short error reason.
public sealed record ListenerStateChanged(string State, string? Reason) : IntegrationEvent;
