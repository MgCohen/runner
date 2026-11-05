using System.Text.Json;
using Handling.Infrastructure;
using Handling.Slices.Handling;
using Listener.Domain;
using Listener.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Network.Infrastructure;
using VSA.Contracts.Events;

namespace Listener.Slices.Listen;

// Keeps the /listen socket open while the listener switch is on (C1). At each hello it runs catch-up first,
// then it takes the live events. It publishes InboxEventReceived for each event and ListenerStateChanged for each
// state. After a lost connection it connects again with the backoff 1, 2, 5, 10, then 30 s.
// At start: with --paused (Runner:Paused), off. Else with listenerOnStart = false, off. Else the saved switch.
// The start rule does not change the saved switch.
public sealed partial class SocketListener(
    IInboxSocket inbox,
    INetworkState network,
    InboxClient client,
    HandlerSwitches switches,
    ListenerStatus status,
    ITrafficSink traffic,
    IServiceScopeFactory scopes,
    IOptionsMonitor<ListenerOptions> options,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<SocketListener> log) : BackgroundService {
    public override Task StartAsync(CancellationToken cancellationToken) {
        if ((bool.TryParse(configuration["Runner:Paused"], out var paused) && paused) || !options.CurrentValue.ListenerOnStart) {
            switches.SetListener(false, save: false);
        }
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var host = options.CurrentValue.BaseUrl.Host;
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested) {
            if (!switches.Listener) {
                await SetState(ListenerState.Paused, null);
                await switches.WaitForListenerAsync(stoppingToken);
                failures = 0;
                continue;
            }
            await SetState(ListenerState.Connecting, null);
            var error = await ConnectAndReceiveAsync(host, () => failures = 0, stoppingToken);
            if (stoppingToken.IsCancellationRequested) {
                return;
            }
            if (error is null) {
                continue;
            }
            await SetState(ListenerState.Error, error);
            await WaitAsync(Reconnect.Delay(failures++), stoppingToken);
        }
    }

    // Connects and receives until the socket closes. Returns the error reason, or null if the switch closed the socket.
    async Task<string?> ConnectAndReceiveAsync(string host, Action onHello, CancellationToken stoppingToken) {
        if (!network.IsUp) {
            return ListenerErrors.NoNetwork;
        }
        using var pause = PauseToken(stoppingToken, out var unsubscribe);
        var start = time.GetUtcNow();
        var connected = false;
        try {
            await using var connection = await inbox.ConnectAsync(pause.Token);
            connected = true;
            traffic.Record(new TrafficEntry(start, "out", "connect", host, "/listen", "open", null, time.GetUtcNow() - start));
            // The live push is at most once: a catch-up also runs at each interval while the socket is open.
            var timer = CatchUpTimerAsync(pause.Token);
            while (await connection.ReceiveAsync(pause.Token) is { } message) {
                await HandleAsync(message, host, onHello, pause.Token);
            }
            _ = timer;
            traffic.Record(new TrafficEntry(time.GetUtcNow(), "in", "close", host, "/listen", "closed", null, null));
            return network.IsUp ? ListenerErrors.NotReachable : ListenerErrors.NoNetwork;
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested || !switches.Listener) {
            if (connected) {
                traffic.Record(new TrafficEntry(time.GetUtcNow(), "out", "close", host, "/listen", "paused", null, null));
            }
            return null;
        } catch (InboxRefusedException e) {
            traffic.Record(new TrafficEntry(start, "out", "connect", host, "/listen", "401", null, time.GetUtcNow() - start));
            SocketFailed(log, e.Message);
            return ListenerErrors.TokenRefused;
        } catch (Exception e) when (e is not OperationCanceledException) {
            traffic.Record(new TrafficEntry(time.GetUtcNow(), connected ? "in" : "out", connected ? "close" : "connect", host, "/listen", "error", null, null));
            SocketFailed(log, e.Message);
            return network.IsUp ? ListenerErrors.NotReachable : ListenerErrors.NoNetwork;
        } finally {
            unsubscribe();
        }
    }

    async Task HandleAsync(string message, string host, Action onHello, CancellationToken ct) {
        try {
            using var doc = JsonDocument.Parse(message);
            var kind = doc.RootElement.TryGetProperty("kind", out var k) ? k.GetString() : null;
            switch (kind) {
                case "hello":
                    traffic.Record(new TrafficEntry(time.GetUtcNow(), "in", "hello", host, "/listen", "received", message.Length, null));
                    onHello();
                    await SetState(ListenerState.Connected, null);
                    await CatchUpAsync(ct);
                    await ListSourcesAsync(ct);
                    break;
                case "event":
                    var e = doc.RootElement.GetProperty("event").Deserialize<InboxEvent>()!;
                    traffic.Record(new TrafficEntry(time.GetUtcNow(), "in", "event", host, "/listen", "received", message.Length, null) { EventId = e.Id });
                    await PublishAsync(new InboxEventReceived(e), ct);
                    break;
                default:
                    traffic.Record(new TrafficEntry(time.GetUtcNow(), "in", "message", host, "/listen", "ignored", message.Length, null));
                    break;
            }
        } catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) {
            traffic.Record(new TrafficEntry(time.GetUtcNow(), "in", "message", host, "/listen", "bad JSON", message.Length, null));
            BadMessage(log, e.Message);
        }
    }

    // Gets the events that came while the listener was off, oldest first. The live push is at most once,
    // so this is also the safety net. A failed catch-up waits for the next connect or the next timer.
    public async Task CatchUpAsync(CancellationToken ct) {
        try {
            long after = 0;
            while (true) {
                var page = await client.GetPendingAsync(after, ct);
                foreach (var json in page) {
                    await PublishAsync(new InboxEventReceived(json.Deserialize<InboxEvent>()!), ct);
                }
                if (page.Count < InboxClient.PageSize) {
                    break;
                }
                after = page[^1].GetProperty("id").GetInt64();
            }
        } catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested) {
            CatchUpFailed(log, e.Message);
        }
    }

    async Task CatchUpTimerAsync(CancellationToken ct) {
        try {
            using var timer = new PeriodicTimer(options.CurrentValue.CatchUpInterval, time);
            while (await timer.WaitForNextTickAsync(ct)) {
                await CatchUpAsync(ct);
            }
        } catch (OperationCanceledException) {
        }
    }

    async Task ListSourcesAsync(CancellationToken ct) {
        try {
            await PublishAsync(new SourcesListed(await client.GetSourcesAsync(ct)), ct);
        } catch (Exception e) when (e is HttpRequestException or JsonException or NotSupportedException) {
            CatchUpFailed(log, e.Message);
        }
    }

    async Task PublishAsync<TEvent>(TEvent e, CancellationToken ct) where TEvent : IntegrationEvent {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().PublishAsync(e, ct);
    }

    async Task SetState(ListenerState state, string? reason) {
        if (status.Set(state, reason)) {
            await PublishAsync(new ListenerStateChanged(state.ToString(), reason), CancellationToken.None);
        }
    }

    // Waits for the delay. The listener switch going off ends the wait.
    async Task WaitAsync(TimeSpan delay, CancellationToken stoppingToken) {
        using var pause = PauseToken(stoppingToken, out var unsubscribe);
        try {
            await Task.Delay(delay, time, pause.Token);
        } catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) {
        } finally {
            unsubscribe();
        }
    }

    // A token that cancels when the app stops or when the listener switch goes off.
    CancellationTokenSource PauseToken(CancellationToken stoppingToken, out Action unsubscribe) {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        void OnChanged() {
            if (switches.Listener) {
                return;
            }
            try {
                cts.Cancel();
            } catch (ObjectDisposedException) {
            }
        }
        switches.Changed += OnChanged;
        unsubscribe = () => switches.Changed -= OnChanged;
        if (!switches.Listener) {
            cts.Cancel();
        }
        return cts;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Socket error: {Message}")]
    static partial void SocketFailed(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Bad message from the inbox: {Message}")]
    static partial void BadMessage(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Catch-up failed: {Message}")]
    static partial void CatchUpFailed(ILogger logger, string message);
}
