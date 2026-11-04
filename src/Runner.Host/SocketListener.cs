using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Runner.Abstractions;

namespace Runner.Host;

/// <summary>
/// Keeps a WebSocket open to <c>/listen</c> and stores each pushed event (contract A, live push).
/// After each <c>hello</c>, it starts a catch-up, because a push is a signal and not a guarantee.
/// </summary>
public sealed class SocketListener(
    EventStore store,
    CatchUp catchUp,
    DispatchSignal dispatch,
    IOptions<InboxOptions> options,
    ILogger<SocketListener> logger) : BackgroundService
{
    /// <summary>The number of <c>hello</c> messages since start. Tests read it.</summary>
    public int Connections => _connections;

    private int _connections;

    public static Uri ListenUri(InboxOptions o)
    {
        var builder = new UriBuilder(o.BaseUrl)
        {
            Scheme = o.BaseUrl.Scheme == Uri.UriSchemeHttp ? "ws" : "wss",
            Path = o.BaseUrl.AbsolutePath.TrimEnd('/') + "/listen",
            Query = "consumer=" + Uri.EscapeDataString(o.ResolvedConsumer),
        };
        if (builder.Uri.IsDefaultPort) builder.Port = -1;
        return builder.Uri;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        var uri = ListenUri(o);
        var failures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                socket.Options.SetRequestHeader("Authorization", "Bearer " + o.Token);
                socket.Options.KeepAliveInterval = o.KeepAliveInterval;
                socket.Options.KeepAliveTimeout = o.KeepAliveTimeout;
                await socket.ConnectAsync(uri, stoppingToken);
                logger.LogInformation("Connected to {Uri}", uri);

                await ReceiveAsync(socket, () => failures = 0, stoppingToken);
                logger.LogWarning("The inbox closed the socket: {Status} {Description}",
                    socket.CloseStatus, socket.CloseStatusDescription);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Socket error: {Message}", ex.Message);
                logger.LogDebug(ex, "Socket error details");
            }

            var delay = Backoff.Delay(o.ReconnectBaseDelay, o.ReconnectMaxDelay, failures++);
            logger.LogInformation("Reconnect in {Delay}", delay);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ReceiveAsync(ClientWebSocket socket, Action onHello, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return;
            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;

            if (result.MessageType == WebSocketMessageType.Text) Handle(message.ToArray(), onHello, cancellationToken);
            message.SetLength(0);
        }
    }

    private void Handle(byte[] json, Action onHello, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var kind = doc.RootElement.TryGetProperty("kind", out var k) ? k.GetString() : null;
            switch (kind)
            {
                case "hello":
                    onHello();
                    var n = Interlocked.Increment(ref _connections);
                    logger.LogInformation("Hello from the inbox (connection {Count})", n);
                    _ = catchUp.TryRunAsync(n == 1 ? "connect" : "reconnect", cancellationToken);
                    break;
                case "event":
                    var e = doc.RootElement.GetProperty("event").Deserialize<InboxEvent>()!;
                    if (store.TryAdd(e))
                    {
                        logger.LogInformation("Pushed event {Id} {Source}/{Type}/{Action}", e.Id, e.Source, e.EventType, e.Action);
                        dispatch.Set();
                    }
                    break;
                default:
                    logger.LogDebug("Unknown message kind {Kind}", kind);
                    break;
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Bad message from the inbox");
        }
    }
}
