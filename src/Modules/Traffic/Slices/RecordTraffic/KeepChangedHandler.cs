using Settings.Slices.SetSetting;
using Traffic.Infrastructure;
using VSA.Handlers.Events;

namespace Traffic.Slices.RecordTraffic;

// A smaller trafficKeep removes the oldest entries at once (A20).
sealed class KeepChangedHandler(TrafficLog log) : IntegrationEventHandler<SettingChanged> {
    public override Task HandleAsync(SettingChanged integrationEvent, CancellationToken ct) {
        if (integrationEvent.Key == TrafficOptions.KeepKey) {
            log.Trim();
        }
        return Task.CompletedTask;
    }
}
