using System.Globalization;
using Events.Infrastructure;
using Handling.Infrastructure;
using Settings.Slices.SetSetting;
using VSA.Handlers.Events;

namespace Events.Slices.CleanUp;

// A smaller storeDays deletes the older events at once (A32).
sealed class StoreDaysChangedHandler(HandlingStore store, TimeProvider time) : IntegrationEventHandler<SettingChanged> {
    public override Task HandleAsync(SettingChanged integrationEvent, CancellationToken ct) {
        if (integrationEvent.Key == EventsOptions.StoreDaysKey && int.TryParse(integrationEvent.Value, CultureInfo.InvariantCulture, out var days)) {
            CleanUpService.Prune(store, EventsOptions.ValidDays(days), time);
        }
        return Task.CompletedTask;
    }
}
