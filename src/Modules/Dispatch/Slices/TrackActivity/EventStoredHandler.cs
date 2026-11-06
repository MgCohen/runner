using Dispatch.Slices.Common;
using Events.Slices.StoreEvent;
using VSA.Contracts.Events;
using VSA.Handlers.Events;

namespace Dispatch.Slices.TrackActivity;

// A new event changes the event count of its source on Home.
sealed class EventStoredHandler(IIntegrationEventPublisher events) : IntegrationEventHandler<EventStored> {
    public override Task HandleAsync(EventStored integrationEvent, CancellationToken ct) => events.PublishAsync(new SourcesChanged(), ct);
}
