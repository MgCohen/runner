using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Runner.Abstractions;

namespace Runner.Host;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the connector: options, store, socket, catch-up, dispatch, ack, switches and traffic log.
    /// Register <see cref="RunnerSwitches"/> or <see cref="IActivitySink"/> before this call to replace the defaults.
    /// </summary>
    public static IServiceCollection AddRunnerHost(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<InboxOptions>()
            .Bind(configuration.GetSection(InboxOptions.Section))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Token),
                "Set the inbox token: user secret Inbox:Token or environment variable INBOX_TOKEN.")
            .Validate(o => ConsumerName.IsValid(o.ResolvedConsumer),
                "Inbox:Consumer must match ^[a-z0-9][a-z0-9_-]{0,63}$.")
            .Validate(o => o.MaxAttempts >= 1, "Inbox:MaxAttempts must be 1 or more.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(_ => RunnerSwitches.InMemory());
        services.TryAddSingleton<IActivitySink, ConsoleActivitySink>();
        services.AddSingleton<TrafficLog>();
        services.AddTransient<TrafficHandler>();
        services.ConfigureHttpClientDefaults(http => http.AddHttpMessageHandler<TrafficHandler>());
        services.AddSingleton<EventStore>();
        services.AddSingleton<HandlerCatalog>();
        services.AddSingleton<DispatchSignal>();
        services.AddSingleton<AckSignal>();
        services.AddSingleton<CatchUp>();
        services.AddHttpClient<InboxClient>();

        services.AddHostedService<Dispatcher>();
        services.AddHostedService<AckService>();
        services.AddHostedService<CatchUpService>();
        services.AddSingleton<SocketListener>();
        services.AddHostedService(sp => sp.GetRequiredService<SocketListener>());
        return services;
    }

    /// <summary>Adds an assembly that the connector scans for <c>[On]</c> handlers.</summary>
    public static IServiceCollection AddHandlerAssembly(this IServiceCollection services, Assembly assembly)
    {
        services.AddSingleton(new HandlerAssembly(assembly));
        return services;
    }

    /// <summary>Adds the INBOX_TOKEN environment variable as <c>Inbox:Token</c>. It overrides user secrets.</summary>
    public static IConfigurationBuilder AddInboxTokenVariable(this IConfigurationBuilder builder)
    {
        var token = Environment.GetEnvironmentVariable("INBOX_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            builder.AddInMemoryCollection([new($"{InboxOptions.Section}:Token", token)]);
        }
        return builder;
    }
}
