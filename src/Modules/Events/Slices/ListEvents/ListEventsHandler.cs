using Handling.Domain;
using Handling.Infrastructure;
using VSA.Domain;
using VSA.Handlers.Requests;

namespace Events.Slices.ListEvents;

sealed class ListEventsHandler(HandlingStore store, HandlerCatalog catalog) : RoutedHandler<ListEventsRequest, ListEventsResponse> {
    protected override Task<Result<ListEventsResponse>> HandleRequestAsync(ListEventsRequest request, CancellationToken ct) {
        var limit = Math.Clamp(request.Limit, 1, 200);
        var page = store.List(new EventQuery(request.States ?? [], request.Sources ?? [], request.Handlers ?? [], request.Text, request.Before, limit));
        string Name(string key) => catalog.Find(key)?.Name ?? key.Split('.')[^1];
        var events = page.Events.Select(e => new EventItem(e.Id, e.Source, e.TypeText, e.State, e.ReceivedAt,
            e.Handlers.Count == 0 ? "no handler" : string.Join(", ", e.Handlers.Select(Name)))).ToList();
        var sources = store.Sources().Concat(catalog.All.SelectMany(h => h.Sources)).Concat(store.SourceCounts().Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var handlers = catalog.All.Select(h => new HandlerOption(h.Key, h.Name, h.Sources)).ToList();
        return Task.FromResult<Result<ListEventsResponse>>(new ListEventsResponse(events, page.Matching, page.Total, page.Facets, page.HasMore, sources, handlers));
    }
}
