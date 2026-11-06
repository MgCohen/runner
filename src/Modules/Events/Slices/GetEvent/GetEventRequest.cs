using VSA.Contracts.Requests;

namespace Events.Slices.GetEvent;

public sealed record GetEventRequest(long Id) : IRequest<GetEventResponse> {
    public static HttpMethod Method => HttpMethod.Get;
    public static string Route => "/api/events/{Id}";
}

// Results: one row for each matching handler: "done (1.2 s)", "failed: …", "skipped (off)", "cancelled" or "waiting".
public sealed record GetEventResponse(long Id, string Source, string Type, string State, DateTimeOffset ReceivedAt, int Attempts, DateTimeOffset? AckedAt,
    IReadOnlyList<HandlerResultItem> Results, string Payload) : IResponse;

public sealed record HandlerResultItem(string Handler, string Result);
