using System.Diagnostics;

namespace Network.Infrastructure;

// Writes each HTTP call of the clients from IHttpClientFactory to the sink: time, method, host and path, status,
// size and duration. A call to a loopback host is the request bus of the views: it is not recorded.
public sealed class TrafficHttpHandler(ITrafficSink sink, TimeProvider time) : DelegatingHandler {
    // The largest body that the sink gets.
    public const int MaxBody = 16 * 1024;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);
        var uri = request.RequestUri;
        if (uri is null || uri.IsLoopback) {
            return await base.SendAsync(request, cancellationToken);
        }
        var at = time.GetUtcNow();
        var start = Stopwatch.GetTimestamp();
        try {
            var response = await base.SendAsync(request, cancellationToken);
            string? body = null;
            if (sink.CaptureBodies) {
                await response.Content.LoadIntoBufferAsync(cancellationToken);
                body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (body.Length > MaxBody) {
                    body = body[..MaxBody];
                }
            }
            sink.Record(new TrafficEntry(at, "out", request.Method.Method, uri.Host, uri.AbsolutePath, ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture),
                response.Content.Headers.ContentLength, Stopwatch.GetElapsedTime(start)) { Body = body });
            return response;
        } catch (Exception e) {
            sink.Record(new TrafficEntry(at, "out", request.Method.Method, uri.Host, uri.AbsolutePath, e is OperationCanceledException ? "canceled" : "error",
                null, Stopwatch.GetElapsedTime(start)));
            throw;
        }
    }
}
