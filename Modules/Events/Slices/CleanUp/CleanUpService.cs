using Events.Infrastructure;
using Handling.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Events.Slices.CleanUp;

// Deletes the finished events older than storeDays, at start and once a day.
public sealed class CleanUpService(HandlingStore store, IOptionsMonitor<EventsOptions> options, TimeProvider time) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1), time);
        do {
            Prune(store, EventsOptions.ValidDays(options.CurrentValue.StoreDays), time);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public static int Prune(HandlingStore store, int days, TimeProvider time) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(time);
        return store.Prune(time.GetUtcNow() - TimeSpan.FromDays(days));
    }
}
