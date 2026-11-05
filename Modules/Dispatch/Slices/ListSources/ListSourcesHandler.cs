using Dispatch.Domain;
using Handling.Infrastructure;
using VSA.Domain;
using VSA.Handlers.Requests;

namespace Dispatch.Slices.ListSources;

// The saved inbox source list, plus each source that a HandlerConfig names. A source with no handlers shows too.
// A handler with two or more sources shows under each of them. Counts cover the events in the local store.
sealed class ListSourcesHandler(HandlingStore store, HandlerCatalog catalog, HandlerSwitches switches) : RoutedHandler<ListSourcesRequest, ListSourcesResponse> {
    protected override Task<Result<ListSourcesResponse>> HandleRequestAsync(ListSourcesRequest request, CancellationToken ct) {
        var counts = store.SourceCounts();
        var names = store.Sources().Concat(catalog.All.SelectMany(h => h.Sources)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var stats = catalog.All.ToDictionary(h => h.Key, h => store.StatsOf(h.Key));
        var sources = names.Select(name => new SourceItem(name, switches.IsSourceOn(name), counts.GetValueOrDefault(name),
            [.. catalog.All.Where(h => h.Listens(name)).Select(h => new HandlerItem(h.Key, h.Name, switches.IsHandlerOn(h.Key), LastText.Of(stats[h.Key].Last)))]))
            .ToList();
        return Task.FromResult<Result<ListSourcesResponse>>(new ListSourcesResponse(sources));
    }
}
