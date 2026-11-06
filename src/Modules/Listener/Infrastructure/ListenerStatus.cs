using Listener.Domain;

namespace Listener.Infrastructure;

// The current state of the listener, for the requests. The socket listener sets it.
public sealed class ListenerStatus {
    readonly Lock gate = new();

    public ListenerState State { get; private set; } = ListenerState.Connecting;

    public string? Reason { get; private set; }

    // True if the state changed.
    public bool Set(ListenerState state, string? reason = null) {
        lock (gate) {
            if (State == state && Reason == reason) {
                return false;
            }
            State = state;
            Reason = reason;
            return true;
        }
    }
}
