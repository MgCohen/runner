using AppSettings;
using AppSettings.Domain;
using Microsoft.Extensions.DependencyInjection;
using Network.Infrastructure;
using Traffic.Infrastructure;
using Traffic.Slices.RecordTraffic;
using VSA.Module;

namespace Traffic;

// The traffic log of each HTTP call and socket message, and its queries (C4). Settings: Modules:Traffic.
// It replaces the default sink of Shared/Network with the log.
public sealed class TrafficModule : Module<TrafficOptions> {
    public override void Configure(ModuleBuilder module, TrafficOptions settings) {
        var services = module.Services;
        services.AddSingleton<TrafficLog>();
        for (var i = services.Count - 1; i >= 0; i--) {
            if (services[i].ServiceType == typeof(ITrafficSink)) {
                services.RemoveAt(i);
            }
        }
        services.AddSingleton<ITrafficSink>(provider => provider.GetRequiredService<TrafficLog>());
        services.AddHostedService<TrafficRelay>();
        module.AddSettingsPart(
            new SettingRow(TrafficOptions.CaptureBodiesKey, SettingGroups.Traffic, "Capture bodies", SettingKind.Switch, "false") {
                Description = "Memory only, secrets removed",
                Order = 0,
            },
            new SettingRow(TrafficOptions.KeepKey, SettingGroups.Traffic, "Entries to keep", SettingKind.Choice, "500") { Options = ["200", "500", "1000"], Order = 1 });
    }
}
