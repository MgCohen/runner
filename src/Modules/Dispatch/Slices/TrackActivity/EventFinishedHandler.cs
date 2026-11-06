using Dispatch.Slices.Common;
using Events.Slices.Common;
using VSA.Contracts.Events;
using VSA.Handlers.Events;

namespace Dispatch.Slices.TrackActivity;

// A finished event changes the "Last" line of its handlers on Home.
sealed class EventFinishedHandler(IIntegrationEventPublisher events) : IntegrationEventHandler<EventFinished> {
    public override Task HandleAsync(EventFinished integrationEvent, CancellationToken ct) => events.PublishAsync(new SourcesChanged(), ct);
}
