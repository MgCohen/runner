using VSA.Contracts.Requests;

namespace Events.Slices.ListEvents;

// One page of the stored events, newest first. Before: an event ID; the page starts after it. Handlers: handler keys.
public sealed record ListEventsRequest(IReadOnlyList<string> States, IReadOnlyList<string> Sources, IReadOnlyList<string> Handlers, string? Text, long? Before, int Limit = 50)
    : IRequest<ListEventsResponse> {
    public static HttpMethod Method => HttpMethod.Post;
    public static string Route => "/api/events/list";
}

// Facets: for each group (state, source, handler), the count of each value with the other active filters.
// Sources and Handlers: the values of the filter panel.
public sealed record ListEventsResponse(IReadOnlyList<EventItem> Events, int Matching, int Total, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Facets,
    bool HasMore, IReadOnlyList<string> Sources, IReadOnlyList<HandlerOption> Handlers) : IResponse;

// Handlers: the matching handler names, comma-separated, or "no handler".
public sealed record EventItem(long Id, string Source, string Type, string State, DateTimeOffset ReceivedAt, string Handlers);

public sealed record HandlerOption(string Key, string Name, IReadOnlyList<string> Sources);
