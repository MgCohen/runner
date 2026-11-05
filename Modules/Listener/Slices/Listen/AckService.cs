using Handling.Infrastructure;
using Listener.Domain;
using Listener.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Listener.Slices.Listen;

// Sends POST /api/consume for each finished event (done or failed). A failed ack tries again with a backoff of
// 5 s to 5 min. At each start it acks the finished events that are not acked. It never runs a handler again.
public sealed partial class AckService(InboxClient client, HandlingStore store, AckSignal signal, TimeProvider time, ILogger<AckService> log) : BackgroundService {
    // The inbox accepts 1 to 500 IDs in one call.
    const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested) {
            var batch = store.AckDue(BatchSize);
            if (batch.Count > 0) {
                try {
                    await client.ConsumeAsync(batch, stoppingToken);
                    store.MarkAcked(batch);
                    failures = 0;
                    continue;
                } catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !stoppingToken.IsCancellationRequested) {
                    var delay = AckRetry.Delay(failures++);
                    AckFailed(log, batch.Count, delay, e.Message);
                    store.ScheduleAckRetry(batch, time.GetUtcNow() + delay);
                }
            }
            var next = store.NextAckTime();
            await signal.WaitAsync(next is null ? null : next - time.GetUtcNow(), stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ack failed for {Count} events. Retry in {Delay}: {Message}")]
    static partial void AckFailed(ILogger logger, int count, TimeSpan delay, string message);
}
