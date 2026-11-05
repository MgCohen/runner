namespace Handling.Domain;

// Contract B: a handler runs 5 times maximum. The delay before attempt n + 1 is BaseDelay * 2^(n - 1), up to MaxDelay.
public sealed record RetryPolicy(int MaxAttempts, TimeSpan BaseDelay, TimeSpan MaxDelay) {
    public static readonly RetryPolicy Default = new(5, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(30));

    public bool IsLast(int attempts) => attempts >= MaxAttempts;

    public TimeSpan Delay(int attempts) {
        var exponent = Math.Clamp(attempts - 1, 0, 30);
        var ms = Math.Min(BaseDelay.TotalMilliseconds * Math.Pow(2, exponent), MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(ms);
    }
}
