using AppSettings;
using AppSettings.Domain;
using Events.Infrastructure;
using Events.Slices.CleanUp;
using Events.Slices.RunHandlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VSA.Module;

namespace Events;

// StoreEvent, the handler lanes, retries, the storeDays clean-up and the event queries (C1, C3). Settings: Modules:Events.
// Mock mode: short retry delays, so the self-test sees 5 attempts in a few seconds.
public sealed class EventsModule : Module<EventsOptions> {
    public override void Configure(ModuleBuilder module, EventsOptions settings) {
        var services = module.Services;
        if (module.Configuration.GetValue<bool>("Runner:Mock")) {
            services.PostConfigure<EventsOptions>(o => {
                o.RetryBaseDelay = TimeSpan.FromMilliseconds(200);
                o.RetryMaxDelay = TimeSpan.FromSeconds(2);
            });
        }
        services.AddSingleton<LaneSignal>();
        services.AddHostedService<HandlerLanes>();
        services.AddHostedService<CleanUpService>();
        module.AddSettingsPart(new SettingRow(EventsOptions.StoreDaysKey, SettingGroups.Inbox, "Local store", SettingKind.Choice, "7") {
            Description = "Days to keep events",
            Options = ["1", "7", "30"],
        });
    }
}
