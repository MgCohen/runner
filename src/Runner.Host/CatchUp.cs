using Microsoft.Extensions.Options;

namespace Runner.Host;

/// <summary>
/// Reads all pending events from the inbox and stores the unknown ones.
/// The live push is at most once, so this is the safety net (contract A).
/// </summary>
public sealed class CatchUp(
    InboxClient client,
    EventStore store,
    DispatchSignal dispatch,
    IOptions<InboxOptions> options,
    ILogger<CatchUp> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Returns the number of events that the store did not have.</summary>
    public async Task<int> RunAsync(string reason, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var added = 0;
            long after = 0;
            while (true)
            {
                var page = await client.GetPendingAsync(after, cancellationToken);
                foreach (var e in page)
                {
                    if (store.TryAdd(e)) added++;
                }
                if (page.Count < InboxClient.PageSize) break;
                after = page[^1].Id;
            }

            if (added > 0) dispatch.Set();
            store.Prune(options.Value.StoreRetention);
            logger.LogInformation("Catch-up ({Reason}) stored {Count} events", reason, added);
            return added;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Runs the catch-up and logs a failure. The next trigger tries again.</summary>
    public async Task TryRunAsync(string reason, CancellationToken cancellationToken)
    {
        try
        {
            await RunAsync(reason, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Catch-up ({Reason}) failed", reason);
        }
    }
}

/// <summary>Runs the catch-up at start and at each <see cref="InboxOptions.CatchUpInterval"/>.</summary>
public sealed class CatchUpService(CatchUp catchUp, IOptions<InboxOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await catchUp.TryRunAsync("start", stoppingToken);
        using var timer = new PeriodicTimer(options.Value.CatchUpInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await catchUp.TryRunAsync("timer", stoppingToken);
        }
    }
}
