using Handling.Domain;

namespace Events.Infrastructure;

// Settings of the Events module. Section: Modules:Events.
public sealed class EventsOptions {
    public const string StoreDaysKey = "storeDays";

    static readonly int[] Days = [1, 7, 30];

    // The local store deletes finished events older than this: 1, 7 or 30. Another value gives 7.
    public int StoreDays { get; set; } = 7;

    public int MaxAttempts { get; set; } = 5;

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromMinutes(30);

    public RetryPolicy Retry => new(MaxAttempts, RetryBaseDelay, RetryMaxDelay);

    public static int ValidDays(int days) => Days.Contains(days) ? days : 7;
}
