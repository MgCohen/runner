using Claude.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VSA.Module;

namespace Claude;

// The claude CLI for the business modules. Settings: Modules:Claude. Mock mode: a fake Claude, no process starts.
public sealed class ClaudeModule : Module<ClaudeOptions> {
    protected override IEnumerable<string> Validate(ClaudeOptions settings) {
        ArgumentNullException.ThrowIfNull(settings);
        if (!ClaudeOptions.AllowedPermissionModes.Contains(settings.PermissionMode)) {
            yield return $"PermissionMode must be one of: {string.Join(", ", ClaudeOptions.AllowedPermissionModes)}.";
        }
    }

    public override void Configure(ModuleBuilder module, ClaudeOptions settings) {
        var services = module.Services;
        if (module.Configuration.GetValue<bool>("Runner:Mock")) {
            services.AddSingleton<MockClaude>();
            services.AddSingleton<IClaudePrint>(provider => provider.GetRequiredService<MockClaude>());
        } else {
            services.AddSingleton<IClaudePrint, ClaudePrint>();
        }
    }
}
