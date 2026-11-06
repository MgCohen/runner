using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Traffic.Infrastructure;
using VSA.Contracts.Events;

namespace Traffic.Slices.RecordTraffic;

// Publishes TrafficRecorded for each new entry of the log.
public sealed class TrafficRelay(TrafficLog log, IServiceScopeFactory scopes) : IHostedService {
    public Task StartAsync(CancellationToken cancellationToken) {
        log.Recorded += OnRecorded;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        log.Recorded -= OnRecorded;
        return Task.CompletedTask;
    }

    void OnRecorded(LoggedEntry entry) => _ = Publish(entry.Seq);

    async Task Publish(long seq) {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().PublishAsync(new TrafficRecorded(seq), CancellationToken.None);
    }
}
