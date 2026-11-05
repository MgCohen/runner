using VSA.Contracts.Requests;

namespace Listener.Slices.SetListener;

public sealed record SetListenerRequest(bool On) : IRequest<SetListenerResponse> {
    public static HttpMethod Method => HttpMethod.Post;
    public static string Route => "/api/listener";
}

public sealed record SetListenerResponse(bool On) : IResponse;
