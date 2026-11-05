using VSA.Contracts.Requests;

namespace Dispatch.Slices.SetHandler;

public sealed record SetHandlerRequest(string Key, bool On) : IRequest<SetHandlerResponse> {
    public static HttpMethod Method => HttpMethod.Post;
    public static string Route => "/api/handlers/switch";
}

public sealed record SetHandlerResponse(string Key, bool On) : IResponse;
