using VSA.Contracts.Requests;

namespace Dispatch.Slices.ListSources;

public sealed record ListSourcesRequest : IRequest<ListSourcesResponse> {
    public static HttpMethod Method => HttpMethod.Get;
    public static string Route => "/api/sources";
}

// The sources in the order of the inbox source list, then the other sources that a handler names.
public sealed record ListSourcesResponse(IReadOnlyList<SourceItem> Sources) : IResponse;

public sealed record SourceItem(string Name, bool On, int Events, IReadOnlyList<HandlerItem> Handlers);

// Last: "#id type · HH:mm" or "No events yet".
public sealed record HandlerItem(string Key, string Name, bool On, string Last);
