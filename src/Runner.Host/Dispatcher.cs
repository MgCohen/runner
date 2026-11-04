using Microsoft.Extensions.Options;
using Runner.Abstractions;

namespace Runner.Host;

/// <summary>
/// Runs the matching handlers for each pending event in the store, oldest id first
/// (contract B, return, exception, permanent failure and no match).
/// </summary>
/// <remarks>
/// The <see cref="RunnerSwitches"/> pause the work. An event waits in the store, pending and not acked, when
/// the listener is off, its source is off, or each matching handler that is not done is off.
/// The dispatcher skips a waiting event and does not wake for it. A switch change wakes the dispatcher.
/// </remarks>
public sealed class Dispatcher(
    EventStore store,
    HandlerCatalog catalog,
    IServiceScopeFactory scopes,
    ILoggerFactory loggers,
    DispatchSignal dispatch,
    AckSignal ack,
    RunnerSwitches switches,
    IActivitySink activity,
    IOptions<InboxOptions> options,
    TimeProvider time) : BackgroundService
{
    private readonly ILogger _logger = loggers.CreateLogger<Dispatcher>();
    private int _passes;

    /// <summary>The number of passes over the store since start. Tests read it.</summary>
    internal int Passes => _passes;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Handlers: {Handlers}",
            catalog.Handlers.Count == 0 ? "none" : string.Join(", ", catalog.Handlers.Select(h => h.Key)));

        void OnSwitch() => dispatch.Set();
        switches.Changed += OnSwitch;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Interlocked.Increment(ref _passes);
                DateTimeOffset? next = null;
                try
                {
                    foreach (var row in store.GetDue())
                    {
                        if (!switches.ListenerEnabled) break;
                        if (IsWaiting(row)) continue;
                        await ProcessAsync(row, stoppingToken);
                    }
                    next = NextWake();
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Dispatch loop error");
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    continue;
                }

                await dispatch.WaitAsync(next is null ? null : next - time.GetUtcNow(), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            switches.Changed -= OnSwitch;
        }
    }

    /// <summary>
    /// Returns true if a switch keeps the event in the store: its source is off.
    /// A handler that is off does not keep an event: the event is done without it.
    /// </summary>
    internal bool IsWaiting(StoredEvent row) => !switches.IsSourceEnabled(row.Event.Source);

    /// <summary>Returns the earliest retry time of a pending event that does not wait for a switch, or null.</summary>
    private DateTimeOffset? NextWake()
    {
        if (!switches.ListenerEnabled) return null;
        return store.GetPending()
            .Where(r => r.NextRetryAt is not null && !IsWaiting(r))
            .Select(r => r.NextRetryAt)
            .Min();
    }

    /// <summary>Runs one attempt for one event and records the result.</summary>
    internal async Task ProcessAsync(StoredEvent row, CancellationToken stoppingToken)
    {
        var e = row.Event;
        var o = options.Value;
        if (!switches.IsSourceEnabled(e.Source)) return;
        var handlers = catalog.Match(e);
        var name = $"{e.Source}/{e.EventType}/{e.Action}";

        if (handlers.Count == 0)
        {
            _logger.LogInformation("Event {Id}: no handler matches", e.Id);
            store.MarkDone(e.Id, row.Attempts);
            ack.Set();
            return;
        }

        // A handler that is done in an earlier attempt does not run again. A handler that is off is skipped.
        var open = handlers.Where(h => !row.DoneHandlers.Contains(h.Key)).ToList();
        var skipped = open.Where(h => !switches.IsHandlerEnabled(h.Type.Name)).ToList();
        var run = open.Except(skipped).ToList();
        if (skipped.Count > 0)
        {
            var names = string.Join(", ", skipped.Select(h => h.Type.Name));
            _logger.LogInformation("Event {Id}: skipped handlers that are off: {Handlers}", e.Id, names);
            activity.Write($"Event {e.Id} {name}: skipped (off): {names}");
        }

        var attempt = row.Attempts + 1;
        Exception? permanent = null;
        Exception? transient = null;

        foreach (var handler in run)
        {
            await using var scope = scopes.CreateAsyncScope();
            var logger = loggers.CreateLogger(handler.Type);
            var start = time.GetTimestamp();
            try
            {
                var instance = (IEventHandler)(scope.ServiceProvider.GetService(handler.Type)
                    ?? ActivatorUtilities.CreateInstance(scope.ServiceProvider, handler.Type));
                await instance.HandleAsync(e, new HandlerContext(attempt, logger, scope.ServiceProvider, stoppingToken));
                store.MarkHandlerDone(e.Id, handler.Key);
                activity.Write($"Event {e.Id} {name}: {handler.Type.Name} done ({Seconds(start)})");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown. The attempt does not count. The event stays pending.
                throw;
            }
            catch (PermanentFailureException ex)
            {
                logger.LogError(ex, "Event {Id}: permanent failure in {Handler}", e.Id, handler.Key);
                activity.Write($"Event {e.Id} {name}: {handler.Type.Name} failed, no retry: {ex.Message}");
                permanent ??= ex;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Event {Id}: attempt {Attempt} failed in {Handler}", e.Id, attempt, handler.Key);
                activity.Write($"Event {e.Id} {name}: {handler.Type.Name} attempt {attempt} failed: {ex.Message}");
                transient ??= ex;
            }
        }

        if (permanent is not null)
        {
            store.MarkFailed(e.Id, attempt, Describe(permanent));
        }
        else if (transient is not null && attempt >= o.MaxAttempts)
        {
            _logger.LogError("Event {Id}: failed after {Attempts} attempts", e.Id, attempt);
            store.MarkFailed(e.Id, attempt, Describe(transient));
        }
        else if (transient is not null)
        {
            var delay = Backoff.Delay(o.RetryBaseDelay, o.RetryMaxDelay, attempt - 1);
            store.ScheduleRetry(e.Id, attempt, Describe(transient), time.GetUtcNow() + delay);
            return;
        }
        else
        {
            store.MarkDone(e.Id, attempt);
        }

        ack.Set();
    }

    private string Seconds(long start) => $"{time.GetElapsedTime(start).TotalSeconds:0.0} s";

    private static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";
}
