using AppSettings.Domain;
using Microsoft.Extensions.DependencyInjection;
using VSA.Module;

namespace AppSettings;

public static class SettingsPartExtensions {
    // Registers the settings rows of a module. The value of a row is in the configuration key Modules:<module>:<key>.
    public static ModuleBuilder AddSettingsPart(this ModuleBuilder module, params SettingRow[] rows) {
        ArgumentNullException.ThrowIfNull(module);
        module.Services.AddSingleton(new SettingsPart(module.Name, rows));
        return module;
    }
}
