using VSA.Contracts.Requests;

namespace MockHandlers.Slices.SetMockDelay;

// Mock mode only: sets the delay of a mock handler, for example MockSlow (A28, A38).
public sealed record SetMockDelayRequest(string Handler, int Milliseconds) : IRequest<MockRunsResponse> {
    public static HttpMethod Method => HttpMethod.Post;
    public static string Route => "/api/mock/delay";
}

// Runs: the finished runs, in order, as "<handler>:<event id>".
public sealed record MockRunsResponse(IReadOnlyList<string> Runs) : IResponse;
