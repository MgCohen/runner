namespace Network.Infrastructure;

// One network item. It never holds headers, tokens or query strings. Body is set only when the sink asks for bodies.
// Direction: "out" for a call or a connect, "in" for a received message. Kind: the HTTP method, or connect, close,
// hello, event, message. EventId: the inbox event of a socket message.
public sealed record TrafficEntry(DateTimeOffset Time, string Direction, string Kind, string Host, string Path, string Status,
    long? Size, TimeSpan? Duration) {
    public long? EventId { get; init; }
    public string? Body { get; init; }

    public bool IsError => Status is "error" or "canceled" || (int.TryParse(Status, out var code) && code >= 400);
}

public interface ITrafficSink {
    // True if the sink keeps response bodies (the setting captureBodies).
    bool CaptureBodies { get; }

    void Record(TrafficEntry entry);
}

public sealed class NoTrafficSink : ITrafficSink {
    public bool CaptureBodies => false;

    public void Record(TrafficEntry entry) {
    }
}
