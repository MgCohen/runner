using VSA.Contracts.Requests;

namespace Listener.Slices.GetListenerState;

public sealed record GetListenerStateRequest : IRequest<GetListenerStateResponse> {
    public static HttpMethod Method => HttpMethod.Get;
    public static string Route => "/api/listener";
}

// State: Paused, Connecting, Connected or Error. Mock: the app runs in mock mode.
public sealed record GetListenerStateResponse(bool On, string State, string? Reason, string Consumer, bool Mock) : IResponse;
