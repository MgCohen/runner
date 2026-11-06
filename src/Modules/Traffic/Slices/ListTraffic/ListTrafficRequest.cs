using VSA.Contracts.Requests;

namespace Traffic.Slices.ListTraffic;

// Directions: in, out. Kinds: event, hello, connect, GET, POST … Results: ok, error. Before: a Seq; the page starts after it.
public sealed record ListTrafficRequest(IReadOnlyList<string> Directions, IReadOnlyList<string> Kinds, IReadOnlyList<string> Results, string? Text, long? Before, int Limit = 50)
    : IRequest<ListTrafficResponse> {
    public static HttpMethod Method => HttpMethod.Post;
    public static string Route => "/api/traffic/list";
}

// Matching, Errors, KbIn and KbOut cover all matching entries. Kept: the capacity of the log. Kinds: the kinds that occur.
public sealed record ListTrafficResponse(IReadOnlyList<TrafficItem> Entries, int Matching, int Kept, int Errors, double KbIn, double KbOut, bool HasMore,
    IReadOnlyList<string> Kinds, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Facets) : IResponse;

// Size: "97 B" or "1.2 KB". Label: "event 12480", or host and path.
public sealed record TrafficItem(long Seq, DateTimeOffset Time, string Direction, string Kind, string Label, string Status, string Size, string Ms, bool IsError, long? EventId);
