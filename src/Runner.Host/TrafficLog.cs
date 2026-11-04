using System.Diagnostics;

namespace Runner.Host;

/// <summary>
/// One network item. It never contains headers, tokens, query strings or bodies.
/// </summary>
/// <param name="Direction"><c>out</c> for a call or a connect, <c>in</c> for a received message.</param>
/// <param name="Kind">The HTTP method, or the socket item: <c>connect</c>, <c>close</c>, <c>hello</c>, <c>event</c>, <c>message</c>.</param>
/// <param name="Target">Host and path, or <c>event &lt;id&gt;</c>.</param>
/// <param name="Size">Bytes of the response or the message. Null if not known.</param>
public sealed record TrafficEntry(
    DateTimeOffset Time,
    string Direction,
    string Kind,
    string Target,
    string Status,
    long? Size,
    TimeSpan? Duration);

/// <summary>An in-memory ring buffer of the last <see cref="Capacity"/> network items.</summary>
public sealed class TrafficLog
{
    public const int Capacity = 500;

    private readonly Lock _lock = new();
    private readonly Queue<TrafficEntry> _entries = new();

    /// <summary>Occurs after an entry is added. It can occur on any thread.</summary>
    public event Action<TrafficEntry>? EntryAdded;

    public void Add(TrafficEntry entry)
    {
        lock (_lock)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > Capacity) _entries.Dequeue();
        }
        EntryAdded?.Invoke(entry);
    }

    /// <summary>Returns the entries, newest first.</summary>
    public IReadOnlyList<TrafficEntry> Snapshot()
    {
        lock (_lock) return _entries.Reverse().ToList();
    }

    /// <summary>Returns host and path. The query string, the fragment and the user info are not in the result.</summary>
    public static string Target(Uri? uri) =>
        uri is null ? "" : uri.IsAbsoluteUri ? uri.Host + uri.AbsolutePath : uri.OriginalString.Split('?', '#')[0];
}

/// <summary>
/// Records each HTTP call of the clients from <c>IHttpClientFactory</c> in the <see cref="TrafficLog"/>:
/// time, method, host and path, status, response size and duration.
/// </summary>
public sealed class TrafficHandler(TrafficLog log, TimeProvider time) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var at = time.GetUtcNow();
        var start = Stopwatch.GetTimestamp();
        var method = request.Method.Method;
        var target = TrafficLog.Target(request.RequestUri);
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            log.Add(new TrafficEntry(at, "out", method, target, ((int)response.StatusCode).ToString(),
                response.Content.Headers.ContentLength, Stopwatch.GetElapsedTime(start)));
            return response;
        }
        catch (Exception ex)
        {
            var status = ex is OperationCanceledException ? "canceled" : "error";
            log.Add(new TrafficEntry(at, "out", method, target, status, null, Stopwatch.GetElapsedTime(start)));
            throw;
        }
    }
}
