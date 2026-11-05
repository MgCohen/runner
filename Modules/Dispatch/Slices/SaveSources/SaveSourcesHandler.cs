using Handling.Infrastructure;
using Listener.Slices.Listen;
using VSA.Handlers.Events;

namespace Dispatch.Slices.SaveSources;

// Saves the source list of the inbox, so Home shows it also with no network, also after a restart.
sealed class SaveSourcesHandler(HandlingStore store) : IntegrationEventHandler<SourcesListed> {
    public override Task HandleAsync(SourcesListed integrationEvent, CancellationToken ct) {
        if (integrationEvent.Sources.Count > 0) {
            store.SaveSources(integrationEvent.Sources);
        }
        return Task.CompletedTask;
    }
}
