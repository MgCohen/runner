namespace Handling.Domain;

// The event can never succeed. The failure is final at once, with no retry (contract B).
public sealed class PermanentFailureException : Exception {
    public PermanentFailureException() { }

    public PermanentFailureException(string message) : base(message) { }

    public PermanentFailureException(string message, Exception? inner) : base(message, inner) { }
}
