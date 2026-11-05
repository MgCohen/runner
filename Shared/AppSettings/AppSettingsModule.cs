using VSA.Module;

namespace AppSettings;

// The settings parts of the modules. Each module gives its rows with module.AddSettingsPart in its Configure,
// and the Settings module joins them in one file and one page.
public sealed class AppSettingsModule : Module {
    public override void Configure(ModuleBuilder module) {
    }
}
