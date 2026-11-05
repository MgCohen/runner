using Events.Infrastructure;
using Handling.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Events.Slices.CleanUp;

// Deletes the finished events older than storeDays, at start and once a day.
public sealed partial class CleanUpService(HandlingStore store, IOptionsMonitor<EventsOptions> options, TimeProvider time, ILogger<CleanUpService> log) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1), time);
        do {
            try {
                Prune(store, EventsOptions.ValidDays(options.CurrentValue.StoreDays), time);
            } catch (Exception e) when (!stoppingToken.IsCancellationRequested) {
                CleanUpFailed(log, e);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The clean-up failed. The next run tries again.")]
    static partial void CleanUpFailed(ILogger logger, Exception e);

    public static int Prune(HandlingStore store, int days, TimeProvider time) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(time);
        return store.Prune(time.GetUtcNow() - TimeSpan.FromDays(days));
    }
}
