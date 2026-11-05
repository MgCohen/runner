using Handling.Domain;
using Microsoft.Extensions.Logging;
using VSA.Handlers.Events;

namespace Handling.Slices.Handling;

// The base of each handler. A handler is a VSA event handler slice in a business module: it gives its config and Run.
// The base runs only for the handler key of the lane, checks the switches, runs Run with the timeout, and writes the
// result row: done, skipped, failed or cancelled. It catches each error, so one handler cannot stop the others.
public abstract partial class InboxHandler(HandlerContext context) : IntegrationEventHandler<InboxEventDispatched> {
    ILogger? log;

    public abstract HandlerConfig Config { get; }

    // The key of the handler in the store and the switches: the full type name.
    public string Key => GetType().FullName!;

    public HandlerInfo Info => new(Key, GetType().Name, Config.Sources, [.. Config.Matches.Select(m => new EventMatch(m.Type, m.Action))],
        Config.Description, Config.DefaultOn, Config.Timeout);

    protected ILogger Log => log ??= context.Loggers.CreateLogger(GetType());

    public sealed override async Task HandleAsync(InboxEventDispatched integrationEvent, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.HandlerKey != Key) {
            return;
        }
        var e = integrationEvent.Event;
        var start = context.Time.GetTimestamp();
        if (context.Switches.OffReason(Key, e.Source) is { } off) {
            await Record(integrationEvent, ResultKind.Skipped, off, start, final: true);
            return;
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Config.Timeout);
        try {
            await Run(e, timeout.Token);
            await Record(integrationEvent, ResultKind.Done, null, start, final: true);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            await Record(integrationEvent, ResultKind.Cancelled, null, start, final: false);
        } catch (OperationCanceledException) {
            await Record(integrationEvent, ResultKind.Failed, "timeout", start, final: false);
        } catch (PermanentFailureException failure) {
            PermanentFailure(Log, failure, e.Id);
            await Record(integrationEvent, ResultKind.Failed, failure.Message, start, final: true);
        } catch (Exception failure) {
            AttemptFailed(Log, failure, e.Id, integrationEvent.Attempt);
            await Record(integrationEvent, ResultKind.Failed, failure.Message, start, final: false);
        }
    }

    protected abstract Task Run(InboxEvent e, CancellationToken ct);

    // A failed result is final only for a permanent failure here. The Events module makes it final after the last attempt.
    async Task Record(InboxEventDispatched dispatched, ResultKind kind, string? reason, long start, bool final) {
        var id = dispatched.Event.Id;
        var permanent = kind == ResultKind.Failed && final;
        context.Store.Record(new HandlerResult(id, Key, kind, reason, context.Time.GetElapsedTime(start), dispatched.Attempt,
            kind is ResultKind.Done or ResultKind.Skipped || permanent, context.Time.GetUtcNow()));
        // The token of the attempt can be canceled (exit), but the result must reach the Events module.
        await context.Events.PublishAsync(new HandlerResultRecorded(id, Key, kind.ToString().ToLowerInvariant(), permanent), CancellationToken.None);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Event {Id}: permanent failure")]
    static partial void PermanentFailure(ILogger logger, Exception e, long id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Event {Id}: attempt {Attempt} failed")]
    static partial void AttemptFailed(ILogger logger, Exception e, long id, int attempt);
}
