using Microsoft.Extensions.Logging;

namespace Runner.Abstractions;

/// <summary>
/// Business logic for one kind of event (CONTRACTS.md, contract B).
/// Return to mark the event as done. Throw to try again later.
/// Throw <see cref="PermanentFailureException"/> to stop without a retry.
/// </summary>
public interface IEventHandler
{
    Task HandleAsync(InboxEvent e, HandlerContext context);
}

/// <summary>
/// Selects the events for a handler. A null value matches any value.
/// A class can have more than one attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class OnAttribute(string source, string? eventType = null, string? action = null) : Attribute
{
    public string Source { get; } = source;
    public string? EventType { get; } = eventType;
    public string? Action { get; } = action;

    public bool Matches(InboxEvent e) =>
        string.Equals(Source, e.Source, StringComparison.OrdinalIgnoreCase)
        && (EventType is null || string.Equals(EventType, e.EventType, StringComparison.OrdinalIgnoreCase))
        && (Action is null || string.Equals(Action, e.Action, StringComparison.OrdinalIgnoreCase));
}

/// <summary>What the connector gives a handler for one attempt.</summary>
public sealed record HandlerContext(
    int Attempt,
    ILogger Logger,
    IServiceProvider Services,
    CancellationToken CancellationToken);

/// <summary>The event can never succeed. The connector records the failure and does not retry.</summary>
public sealed class PermanentFailureException(string message, Exception? inner = null) : Exception(message, inner);
