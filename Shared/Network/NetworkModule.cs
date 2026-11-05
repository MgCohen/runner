using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Network.Infrastructure;
using VSA.Module;

namespace Network;

// The traffic sink port. Each HTTP client of the app writes to it, and the socket writes to it.
// The default sink keeps nothing; the Traffic module replaces it with the traffic log.
public sealed class NetworkModule : Module {
    public override void Configure(ModuleBuilder module) {
        var services = module.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ITrafficSink, NoTrafficSink>();
        services.AddTransient<TrafficHttpHandler>();
        services.ConfigureHttpClientDefaults(http => http.AddHttpMessageHandler<TrafficHttpHandler>());
    }
}
