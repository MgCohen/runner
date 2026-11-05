using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Network.Infrastructure;

// Writes each HTTP call of the clients from IHttpClientFactory to the sink: time, method, host and path, status,
// size and duration. It also writes one log line for each call. A call to a loopback host is the request bus of the
// views: it is not recorded.
public sealed partial class TrafficHttpHandler(ITrafficSink sink, TimeProvider time, ILogger<TrafficHttpHandler> log) : DelegatingHandler {
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
            var duration = Stopwatch.GetElapsedTime(start);
            Call(log, request.Method.Method, uri.Host, uri.AbsolutePath, (int)response.StatusCode, (long)duration.TotalMilliseconds);
            sink.Record(new TrafficEntry(at, "out", request.Method.Method, uri.Host, uri.AbsolutePath, ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture),
                response.Content.Headers.ContentLength, duration) { Body = body });
            return response;
        } catch (Exception e) {
            CallFailed(log, request.Method.Method, uri.Host, uri.AbsolutePath, e.GetType().Name);
            sink.Record(new TrafficEntry(at, "out", request.Method.Method, uri.Host, uri.AbsolutePath, e is OperationCanceledException ? "canceled" : "error",
                null, Stopwatch.GetElapsedTime(start)));
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "HTTP {Method} {Host}{Path} → {Status} in {Ms} ms")]
    static partial void Call(ILogger logger, string method, string host, string path, int status, long ms);

    [LoggerMessage(Level = LogLevel.Warning, Message = "HTTP {Method} {Host}{Path} failed: {Error}")]
    static partial void CallFailed(ILogger logger, string method, string host, string path, string error);
}
