using VSA.Contracts.Requests;

namespace Dispatch.Slices.GetHandler;

// Key: the full type name of the handler.
public sealed record GetHandlerRequest(string Key) : IRequest<GetHandlerResponse> {
    public static HttpMethod Method => HttpMethod.Get;
    public static string Route => "/api/handlers/{Key}";
}

public sealed record GetHandlerResponse(string Key, string Name, bool On, string Description, IReadOnlyList<string> Sources, string ListensTo, string Last,
    int Events, int Failed) : IResponse;
