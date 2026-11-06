using Events.Infrastructure;
using Handling.Infrastructure;
using Handling.Slices.Handling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VSA.Contracts.Events;

namespace Events.Slices.RunHandlers;

// Step 3 of the event pipeline: one lane for each handler. Each lane takes its oldest due item (received time,
// then event ID), makes a new scope, and publishes InboxEventDispatched with the handler key and the attempt.
// A lane runs one item at a time, so a slow handler delays only its own lane. The listener switch pauses all lanes.
// At exit the stopping token cancels the running handlers: their result is "cancelled" and the items stay pending.
public sealed partial class HandlerLanes(HandlingStore store, HandlerCatalog catalog, HandlerSwitches switches, LaneSignal signal,
    IServiceScopeFactory scopes, IHostApplicationLifetime lifetime, TimeProvider time, ILogger<HandlerLanes> log) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        void OnSwitch() => signal.WakeAll();
        switches.Changed += OnSwitch;
        // Exit (spec 4.5): the running handlers are cancelled first, when the app starts to stop, before the other services.
        using var exit = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, lifetime.ApplicationStopping);
        try {
            await Task.WhenAll(catalog.All.Select(h => Task.Run(() => Lane(h.Key, exit.Token), CancellationToken.None)));
        } finally {
            switches.Changed -= OnSwitch;
        }
    }

    async Task Lane(string handler, CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                await switches.WaitForListenerAsync(stoppingToken);
                if (store.NextDue(handler) is { } item) {
                    if (store.Get(item.EventId) is not { } stored) {
                        store.RemovePending(item.EventId, handler);
                        continue;
                    }
                    await using var scope = scopes.CreateAsyncScope();
                    var events = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
                    await events.PublishAsync(new InboxEventDispatched(stored.Event.ToEvent(), handler, item.Attempts + 1), stoppingToken);
                    continue;
                }
                var next = store.NextRetry(handler);
                await signal.WaitAsync(handler, next is null ? null : next - time.GetUtcNow(), stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                return;
            } catch (Exception e) {
                LaneFailed(log, e, handler);
                await Task.Delay(TimeSpan.FromSeconds(1), time, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The lane of {Handler} failed")]
    static partial void LaneFailed(ILogger logger, Exception e, string handler);
}
