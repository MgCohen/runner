using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Runner.Abstractions;

namespace Runner.Host;

/// <summary>The state of the <c>/listen</c> socket.</summary>
public enum ListenerState
{
    /// <summary>The listener switch is off. The socket is closed.</summary>
    Paused,

    Connecting,

    /// <summary>The inbox sent <c>hello</c>.</summary>
    Connected,

    /// <summary>The last connect or receive failed. The listener waits, then connects again.</summary>
    Error,
}

/// <summary>
/// Keeps a WebSocket open to <c>/listen</c> and stores each pushed event (contract A, live push).
/// After each <c>hello</c>, it starts a catch-up, because a push is a signal and not a guarantee.
/// If <see cref="RunnerSwitches.ListenerEnabled"/> is false, the socket closes and does not connect again
/// until the switch is on.
/// </summary>
public sealed class SocketListener(
    EventStore store,
    CatchUp catchUp,
    DispatchSignal dispatch,
    RunnerSwitches switches,
    TrafficLog traffic,
    TimeProvider time,
    IOptions<InboxOptions> options,
    ILogger<SocketListener> logger) : BackgroundService
{
    /// <summary>The number of <c>hello</c> messages since start. Tests read it.</summary>
    public int Connections => _connections;

    private int _connections;
    private volatile ListenerState _state = ListenerState.Connecting;

    public ListenerState State => _state;

    /// <summary>Occurs when <see cref="State"/> changes. It can occur on any thread.</summary>
    public event Action<ListenerState>? StateChanged;

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
        var target = TrafficLog.Target(uri);
        var failures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!switches.ListenerEnabled)
            {
                SetState(ListenerState.Paused);
                logger.LogInformation("The listener is off. The socket stays closed.");
                try
                {
                    await switches.WaitUntilListenerEnabledAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                logger.LogInformation("The listener is on.");
                failures = 0;
                continue;
            }

            if (!await ConnectAndReceiveAsync(uri, target, o, () => failures = 0, stoppingToken)) continue;
            if (stoppingToken.IsCancellationRequested) return;

            var delay = Backoff.Delay(o.ReconnectBaseDelay, o.ReconnectMaxDelay, failures++);
            logger.LogInformation("Reconnect in {Delay}", delay);
            using var wait = PauseToken(stoppingToken, out var unsubscribe);
            try
            {
                await Task.Delay(delay, wait.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                // The listener is off. The next pass waits for the switch.
            }
            finally
            {
                unsubscribe();
            }
        }
    }

    /// <summary>
    /// Connects and receives until the socket closes. Returns false if the listener switch closed the socket
    /// or the host stops: then no reconnect delay applies.
    /// </summary>
    private async Task<bool> ConnectAndReceiveAsync(
        Uri uri, string target, InboxOptions o, Action onHello, CancellationToken stoppingToken)
    {
        using var pause = PauseToken(stoppingToken, out var unsubscribe);
        var connected = false;
        try
        {
            SetState(ListenerState.Connecting);
            using var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", "Bearer " + o.Token);
            socket.Options.KeepAliveInterval = o.KeepAliveInterval;
            socket.Options.KeepAliveTimeout = o.KeepAliveTimeout;
            var start = time.GetUtcNow();
            await socket.ConnectAsync(uri, pause.Token);
            connected = true;
            traffic.Add(new TrafficEntry(start, "out", "connect", target, "open", null, time.GetUtcNow() - start));
            logger.LogInformation("Connected to {Uri}", uri);

            await ReceiveAsync(socket, () => { onHello(); SetState(ListenerState.Connected); }, pause.Token);
            traffic.Add(new TrafficEntry(time.GetUtcNow(), "in", "close", target,
                socket.CloseStatus?.ToString() ?? "closed", null, null));
            logger.LogWarning("The inbox closed the socket: {Status} {Description}",
                socket.CloseStatus, socket.CloseStatusDescription);
            SetState(ListenerState.Connecting);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
        catch (OperationCanceledException) when (!switches.ListenerEnabled)
        {
            if (connected) traffic.Add(new TrafficEntry(time.GetUtcNow(), "out", "close", target, "paused", null, null));
            logger.LogInformation("The listener is off. The socket is closed.");
            return false;
        }
        catch (Exception ex)
        {
            traffic.Add(new TrafficEntry(time.GetUtcNow(), connected ? "in" : "out", connected ? "close" : "connect",
                target, "error", null, null));
            logger.LogWarning("Socket error: {Message}", ex.Message);
            logger.LogDebug(ex, "Socket error details");
            SetState(ListenerState.Error);
            return true;
        }
        finally
        {
            unsubscribe();
        }
    }

    /// <summary>Returns a token that is canceled when the host stops or when the listener switch goes off.</summary>
    private CancellationTokenSource PauseToken(CancellationToken stoppingToken, out Action unsubscribe)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        void OnChanged()
        {
            if (switches.ListenerEnabled) return;
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
        switches.Changed += OnChanged;
        unsubscribe = () => switches.Changed -= OnChanged;
        if (!switches.ListenerEnabled) cts.Cancel();
        return cts;
    }

    private void SetState(ListenerState state)
    {
        if (_state == state) return;
        _state = state;
        StateChanged?.Invoke(state);
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
            else traffic.Add(new TrafficEntry(time.GetUtcNow(), "in", "message", "", "ignored", message.Length, null));
            message.SetLength(0);
        }
    }

    private void Handle(byte[] json, Action onHello, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var kind = doc.RootElement.TryGetProperty("kind", out var k) ? k.GetString() : null;
            long? eventId = kind == "event" && doc.RootElement.TryGetProperty("event", out var ev)
                && ev.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id) ? id : null;
            traffic.Add(new TrafficEntry(time.GetUtcNow(), "in", kind ?? "message",
                eventId is null ? "" : $"event {eventId}", "received", json.Length, null));
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
            traffic.Add(new TrafficEntry(time.GetUtcNow(), "in", "message", "", "bad JSON", json.Length, null));
            logger.LogWarning(ex, "Bad message from the inbox");
        }
    }
}
