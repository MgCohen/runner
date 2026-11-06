namespace Handling.Domain;

// Pending: one or more matching handlers have no final result. Done: no final failure. Failed: one or more final failures.
public enum EventStatus {
    Pending,
    Done,
    Failed,
}

// The state that the views show: new = Pending with 0 attempts, pending = Pending with 1 or more attempts.
public static class EventState {
    public const string New = "new";
    public const string Pending = "pending";
    public const string Done = "done";
    public const string Failed = "failed";

    public static readonly IReadOnlyList<string> All = [New, Pending, Done, Failed];

    public static string Of(EventStatus status, int attempts) => status switch {
        EventStatus.Done => Done,
        EventStatus.Failed => Failed,
        _ => attempts == 0 ? New : Pending,
    };
}
