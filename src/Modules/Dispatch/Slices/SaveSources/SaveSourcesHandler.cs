using Dispatch.Slices.Common;
using Handling.Infrastructure;
using Listener.Slices.Listen;
using VSA.Contracts.Events;
using VSA.Handlers.Events;

namespace Dispatch.Slices.SaveSources;

// Saves the source list of the inbox, so Home shows it also with no network, also after a restart.
sealed class SaveSourcesHandler(HandlingStore store, IIntegrationEventPublisher events) : IntegrationEventHandler<SourcesListed> {
    public override async Task HandleAsync(SourcesListed integrationEvent, CancellationToken ct) {
        if (integrationEvent.Sources.Count > 0) {
            store.SaveSources(integrationEvent.Sources);
        }
        await events.PublishAsync(new SourcesChanged(), ct);
    }
}
