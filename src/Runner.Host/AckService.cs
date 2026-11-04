using Microsoft.Extensions.Options;

namespace Runner.Host;

/// <summary>
/// Sends <c>POST /api/consume</c> for each finished event (done or failed).
/// If the call fails, the service tries the ack again later. It does not run the handlers again.
/// </summary>
public sealed class AckService(
    InboxClient client,
    EventStore store,
    AckSignal signal,
    IOptions<InboxOptions> options,
    TimeProvider time,
    ILogger<AckService> logger) : BackgroundService
{
    /// <summary>The inbox accepts 1 to 500 IDs in one call.</summary>
    private const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        var failures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var batch = store.GetAckDue(BatchSize).Select(r => r.Event.Id).ToList();
            if (batch.Count > 0)
            {
                try
                {
                    await client.ConsumeAsync(batch, stoppingToken);
                    store.MarkAcked(batch);
                    failures = 0;
                    logger.LogInformation("Acked {Count} events, last id {Id}", batch.Count, batch[^1]);
                    continue;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    var delay = Backoff.Delay(o.AckBaseDelay, o.AckMaxDelay, failures++);
                    logger.LogWarning(ex, "Ack failed for {Count} events. Retry in {Delay}", batch.Count, delay);
                    store.ScheduleAckRetry(batch, time.GetUtcNow() + delay);
                }
            }

            var next = store.NextAckTime();
            await signal.WaitAsync(next is null ? null : next - time.GetUtcNow(), stoppingToken);
        }
    }
}
