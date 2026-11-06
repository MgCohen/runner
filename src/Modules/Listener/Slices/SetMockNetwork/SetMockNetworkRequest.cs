using VSA.Contracts.Requests;

namespace Listener.Slices.SetMockNetwork;

// Mock mode only: cuts or restores the mock network (A23).
public sealed record SetMockNetworkRequest(bool Up) : IRequest<SetMockNetworkResponse> {
    public static HttpMethod Method => HttpMethod.Post;
    public static string Route => "/api/listener/mock/network";
}

public sealed record SetMockNetworkResponse(bool Up) : IResponse;
