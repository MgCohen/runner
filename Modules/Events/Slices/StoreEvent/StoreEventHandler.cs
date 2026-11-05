using Events.Infrastructure;
using Events.Slices.Common;
using Handling.Domain;
using Handling.Infrastructure;
using Handling.Slices.Handling;
using VSA.Contracts.Events;
using VSA.Handlers.Events;

namespace Events.Slices.StoreEvent;

// Step 2 of the event pipeline: saves the event and adds one pending item for each matching handler.
// A duplicate event ID is dropped; if that event is finished, it is acked again. An event with no handler is done at once.
sealed class StoreEventHandler(HandlingStore store, HandlerCatalog catalog, LaneSignal lanes, IIntegrationEventPublisher events)
    : IntegrationEventHandler<InboxEventReceived> {
    public override async Task HandleAsync(InboxEventReceived integrationEvent, CancellationToken ct) {
        var e = integrationEvent.Event.ToRecord();
        var handlers = catalog.Match(e).Select(h => h.Key).ToList();
        if (!store.TryAdd(e, handlers)) {
            if (store.StatusOf(e.Id) is { } status and not EventStatus.Pending) {
                store.AckAgain(e.Id);
                await events.PublishAsync(new EventFinished(e.Id, status.ToString().ToLowerInvariant()), ct);
            }
            return;
        }
        await events.PublishAsync(new EventStored(e.Id, e.Source), ct);
        if (handlers.Count == 0) {
            await events.PublishAsync(new EventFinished(e.Id, "done"), ct);
            return;
        }
        foreach (var handler in handlers) {
            lanes.Wake(handler);
        }
    }
}
