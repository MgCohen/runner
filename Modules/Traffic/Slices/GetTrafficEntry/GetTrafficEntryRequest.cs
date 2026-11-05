using VSA.Contracts.Requests;

namespace Traffic.Slices.GetTrafficEntry;

public sealed record GetTrafficEntryRequest(long Seq) : IRequest<GetTrafficEntryResponse> {
    public static HttpMethod Method => HttpMethod.Get;
    public static string Route => "/api/traffic/{Seq}";
}

// Body: the kept response body, or null. CaptureBodies: the current setting.
public sealed record GetTrafficEntryResponse(long Seq, string Direction, string Kind, string Host, string Path, string Status, string Size, string Ms, bool IsError,
    long? EventId, string? Body, bool CaptureBodies) : IResponse;
