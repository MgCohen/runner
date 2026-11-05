using Handling.Infrastructure;
using Handling.Slices.Handling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VSA.Module;

namespace Handling;

// The shared base of the inbox handlers: the local store, the switches, the handler catalog and the handler context.
// Each module layer gets the same layer of this module, so all modules use the same store and the same switches.
public sealed class HandlingModule : Module {
    public override void Configure(ModuleBuilder module) {
        var services = module.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<HandlingStore>();
        services.AddSingleton<HandlerSwitches>();
        // The catalog reads the config of each InboxHandler when it is first used: the handlers of the business
        // modules are registered after this module.
        services.AddSingleton(provider => new HandlerCatalog(() => HandlerInfos.Load(provider)));
        services.AddScoped<HandlerContext>();
    }
}
