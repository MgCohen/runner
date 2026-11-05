using VSA.Contracts.Requests;

namespace Dispatch.Slices.SetSource;

public sealed record SetSourceRequest(string Name, bool On) : IRequest<SetSourceResponse> {
    public static HttpMethod Method => HttpMethod.Post;
    public static string Route => "/api/sources/switch";
}

public sealed record SetSourceResponse(string Name, bool On) : IResponse;
