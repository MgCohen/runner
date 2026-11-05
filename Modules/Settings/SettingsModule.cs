using AppSettings;
using AppSettings.Domain;
using Handling.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Settings.Infrastructure;
using VSA.Module;

namespace Settings;

// The settings file <data folder>\settings.json (C5). It joins the settings parts of the modules: the value of each row
// is the configuration key Modules:<module>:<key>, and the host loads the file as configuration.
// This module owns the rows of the window and the appearance.
public sealed class SettingsModule : Module<WindowOptions> {
    public override void Configure(ModuleBuilder module, WindowOptions settings) {
        module.Services.AddSingleton<SettingsFile>();
        var dataDir = StorePaths.DataDir(module.Configuration);
        module.AddSettingsPart(
            new SettingRow("openOnStart", SettingGroups.AtStart, "Open the widget at start", SettingKind.Switch, "true") { Order = 1 },
            new SettingRow("pinOnStart", SettingGroups.AtStart, "Pin by default", SettingKind.Switch, "false") { Description = "Stays on top and does not hide", Order = 2 },
            new SettingRow("startWithWindows", SettingGroups.AtStart, "Start with Windows", SettingKind.Switch, "false") {
                Description = "Needs the PC owner's OK first",
                Later = true,
                Order = 3,
            },
            new SettingRow("theme", SettingGroups.Appearance, "Theme", SettingKind.Choice, "System") { Options = WindowOptions.Themes, Order = 0 },
            new SettingRow("notifyOnFailure", SettingGroups.Appearance, "Notify on failed handlers", SettingKind.Switch, "false") {
                Description = "Windows notification",
                Later = true,
                Order = 1,
            },
            new SettingRow("store", SettingGroups.About, "Store", SettingKind.Info, "") { Order = 2, Value = () => dataDir },
            new SettingRow("version", SettingGroups.About, "Version", SettingKind.Info, "") {
                Order = 3,
                Value = () => typeof(SettingsModule).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            });
    }
}
