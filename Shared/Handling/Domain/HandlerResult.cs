namespace Handling.Domain;

public enum ResultKind {
    Done,
    Skipped,
    Failed,
    Cancelled,
}

// The last result of one handler for one event. Final: the handler does not run again for the event.
public sealed record HandlerResult(long EventId, string HandlerKey, ResultKind Kind, string? Reason, TimeSpan Duration, int Attempt, bool Final, DateTimeOffset At) {
    // The text that the views show: "done (1.2 s)", "failed: <reason>", "skipped (off)", "cancelled".
    public string Text => Kind switch {
        ResultKind.Done => $"done ({Duration.TotalSeconds:0.0} s)",
        ResultKind.Skipped => $"skipped ({Reason})",
        ResultKind.Failed => $"failed: {Reason}",
        _ => "cancelled",
    };
}

// One event that waits for one handler. NextRetryAt is the earliest time of the next attempt.
public sealed record PendingItem(long EventId, string HandlerKey, int Attempts, DateTimeOffset NextRetryAt);

// One event with its pending items and the results of its handlers.
public sealed record StoredEvent(EventRecord Event, EventStatus Status, int Attempts, DateTimeOffset? AckedAt, IReadOnlyList<string> Handlers, IReadOnlyList<HandlerResult> Results) {
    public string State => EventState.Of(Status, Attempts);
}
