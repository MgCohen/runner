using Events.Infrastructure;
using Events.Slices.Common;
using Handling.Domain;
using Handling.Infrastructure;
using Handling.Slices.Handling;
using Microsoft.Extensions.Options;
using VSA.Contracts.Events;
using VSA.Handlers.Events;

namespace Events.Slices.RunHandlers;

// Steps 5 and 6 of the event pipeline. Done and skipped are final. A cancelled item stays pending and does not count
// as an attempt. A failed item with fewer than 5 attempts waits for its retry time (contract B); a permanent failure
// or the last attempt is final. When each matching handler has a final result, the event is Done or Failed.
sealed class RecordResultHandler(HandlingStore store, LaneSignal lanes, IOptionsMonitor<EventsOptions> options, TimeProvider time, IIntegrationEventPublisher events)
    : IntegrationEventHandler<HandlerResultRecorded> {
    public override async Task HandleAsync(HandlerResultRecorded integrationEvent, CancellationToken ct) {
        var (id, handler) = (integrationEvent.InboxId, integrationEvent.HandlerKey);
        if (store.PendingOf(id, handler) is not { } item) {
            return;
        }
        EventStatus? finished;
        switch (integrationEvent.Result) {
            case "done" or "skipped":
                finished = store.Complete(id, handler, final: false);
                break;
            case "failed":
                var attempts = item.Attempts + 1;
                var retry = options.CurrentValue.Retry;
                if (!integrationEvent.Permanent && !retry.IsLast(attempts)) {
                    store.Reschedule(id, handler, attempts, time.GetUtcNow() + retry.Delay(attempts));
                    lanes.Wake(handler);
                    return;
                }
                finished = store.Complete(id, handler, final: true);
                break;
            default:
                return;
        }
        if (finished is { } status) {
            await events.PublishAsync(new EventFinished(id, status.ToString().ToLowerInvariant()), ct);
        }
    }
}
