using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MockHandlers.Infrastructure;
using VSA.Module;

namespace MockHandlers;

// Test-only handlers for the self-test (A8, A33 to A35, A38). They listen to the source "mock". The host registers
// the handlers of each module; in real mode this module removes its own, so the real app does not show them.
public sealed class MockHandlersModule : Module {
    public override void Configure(ModuleBuilder module) {
        var services = module.Services;
        if (!module.Configuration.GetValue<bool>("Runner:Mock")) {
            var own = typeof(MockHandlersModule).Assembly.GetName().Name + ".";
            for (var i = services.Count - 1; i >= 0; i--) {
                if (services[i].ImplementationType?.Assembly.GetName().Name?.StartsWith(own, StringComparison.Ordinal) == true) {
                    services.RemoveAt(i);
                }
            }
            return;
        }
        services.AddSingleton<MockControls>();
    }
}
