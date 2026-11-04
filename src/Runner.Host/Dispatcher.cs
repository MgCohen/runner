using Microsoft.Extensions.Options;
using Runner.Abstractions;

namespace Runner.Host;

/// <summary>
/// Runs the matching handlers for each pending event in the store, oldest id first
/// (contract B, return, exception, permanent failure and no match).
/// </summary>
public sealed class Dispatcher(
    EventStore store,
    HandlerCatalog catalog,
    IServiceScopeFactory scopes,
    ILoggerFactory loggers,
    DispatchSignal dispatch,
    AckSignal ack,
    IOptions<InboxOptions> options,
    TimeProvider time) : BackgroundService
{
    private readonly ILogger _logger = loggers.CreateLogger<Dispatcher>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Handlers: {Handlers}",
            catalog.Handlers.Count == 0 ? "none" : string.Join(", ", catalog.Handlers.Select(h => h.Key)));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var row in store.GetDue())
                {
                    await ProcessAsync(row, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dispatch loop error");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }

            var next = store.NextRetryTime();
            await dispatch.WaitAsync(next is null ? null : next - time.GetUtcNow(), stoppingToken);
        }
    }

    /// <summary>Runs one attempt for one event and records the result.</summary>
    internal async Task ProcessAsync(StoredEvent row, CancellationToken stoppingToken)
    {
        var e = row.Event;
        var o = options.Value;
        var handlers = catalog.Match(e);

        if (handlers.Count == 0)
        {
            _logger.LogInformation("Event {Id}: no handler matches", e.Id);
            store.MarkDone(e.Id, row.Attempts);
            ack.Set();
            return;
        }

        var attempt = row.Attempts + 1;
        Exception? permanent = null;
        Exception? transient = null;

        // A handler that is done in an earlier attempt does not run again.
        foreach (var handler in handlers.Where(h => !row.DoneHandlers.Contains(h.Key)))
        {
            await using var scope = scopes.CreateAsyncScope();
            var logger = loggers.CreateLogger(handler.Type);
            try
            {
                var instance = (IEventHandler)(scope.ServiceProvider.GetService(handler.Type)
                    ?? ActivatorUtilities.CreateInstance(scope.ServiceProvider, handler.Type));
                await instance.HandleAsync(e, new HandlerContext(attempt, logger, scope.ServiceProvider, stoppingToken));
                store.MarkHandlerDone(e.Id, handler.Key);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown. The attempt does not count. The event stays pending.
                throw;
            }
            catch (PermanentFailureException ex)
            {
                logger.LogError(ex, "Event {Id}: permanent failure in {Handler}", e.Id, handler.Key);
                permanent ??= ex;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Event {Id}: attempt {Attempt} failed in {Handler}", e.Id, attempt, handler.Key);
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

    private static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";
}
