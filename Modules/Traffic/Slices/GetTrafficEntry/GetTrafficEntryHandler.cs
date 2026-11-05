using System.Globalization;
using Traffic.Infrastructure;
using VSA.Domain;
using VSA.Handlers.Requests;

namespace Traffic.Slices.GetTrafficEntry;

sealed class GetTrafficEntryHandler(TrafficLog log) : RoutedHandler<GetTrafficEntryRequest, GetTrafficEntryResponse> {
    protected override Task<Result<GetTrafficEntryResponse>> HandleRequestAsync(GetTrafficEntryRequest request, CancellationToken ct) {
        if (log.Find(request.Seq) is not { } logged) {
            return Task.FromResult<Result<GetTrafficEntryResponse>>(new Error("traffic.not_found", $"Entry {request.Seq} is not in the log."));
        }
        var x = logged.Entry;
        var size = x.Size is not { } bytes ? "—" : bytes < 1024 ? $"{bytes} B" : (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        var ms = x.Duration is { } d ? Math.Round(d.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) : "—";
        return Task.FromResult<Result<GetTrafficEntryResponse>>(new GetTrafficEntryResponse(logged.Seq, x.Direction, x.Kind, x.Host, x.Path, x.Status, size, ms,
            x.IsError, x.EventId, x.Body, log.CaptureBodies));
    }
}
