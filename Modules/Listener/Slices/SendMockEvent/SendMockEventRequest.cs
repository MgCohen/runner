using VSA.Contracts.Requests;

namespace Listener.Slices.SendMockEvent;

// Mock mode only: adds an event to the mock inbox. Kind "task" sends a Notion task edit and sets the mock task to Todo.
// Kind "github" sends a GitHub push. Kind "custom" sends Source, Type, Action and Payload, AgeDays ago. Again: an ID to send again.
public sealed record SendMockEventRequest(string Kind, string? Source = null, string? Type = null, string? Action = null, string? Payload = null, long? Again = null,
    double AgeDays = 0)
    : IRequest<SendMockEventResponse> {
    public static HttpMethod Method => HttpMethod.Post;
    public static string Route => "/api/listener/mock/events";
}

public sealed record SendMockEventResponse(long Id) : IResponse;
