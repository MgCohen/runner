namespace Listener.Domain;

// Paused: the switch is off. Connecting: the switch is on. Connected: the inbox Worker sent hello.
// Error: the socket is lost, there is no network, or the token is refused. From Error, the listener connects again after the backoff.
public enum ListenerState {
    Paused,
    Connecting,
    Connected,
    Error,
}

public static class ListenerErrors {
    public const string NoNetwork = "No network";
    public const string TokenRefused = "Token refused";
    public const string NotReachable = "Inbox not reachable";
}

// The reconnect delays: 1, 2, 5, 10, then 30 s.
public static class Reconnect {
    static readonly TimeSpan[] Delays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    public static TimeSpan Delay(int failures) => Delays[Math.Clamp(failures, 0, Delays.Length - 1)];
}

// The ack delays after a failed ack: 5 s, doubled each time, up to 5 min.
public static class AckRetry {
    public static TimeSpan Delay(int failures) => TimeSpan.FromSeconds(Math.Min(5 * Math.Pow(2, Math.Clamp(failures, 0, 10)), 300));
}
