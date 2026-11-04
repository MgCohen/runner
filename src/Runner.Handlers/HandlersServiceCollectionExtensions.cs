using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Runner.Abstractions;
using Runner.Handlers.Claude;
using Runner.Handlers.Notion;
using Runner.Handlers.Tasks;

namespace Runner.Handlers;

public static class HandlersServiceCollectionExtensions
{
    /// <summary>Registers the helpers and each <see cref="IEventHandler"/> class with <see cref="OnAttribute"/>.</summary>
    public static IServiceCollection AddHandlers(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IActivitySink, ConsoleActivitySink>();

        services.AddOptions<NotionOptions>()
            .Bind(configuration.GetSection(NotionOptions.Section))
            .PostConfigure(o =>
            {
                if (string.IsNullOrWhiteSpace(o.Token))
                {
                    o.Token = configuration["NOTION_API_TOKEN"]
                        ?? Environment.GetEnvironmentVariable("NOTION_API_TOKEN");
                }
            });
        services.AddHttpClient<INotionClient, NotionClient>();

        services.AddOptions<ClaudeOptions>()
            .Bind(configuration.GetSection(ClaudeOptions.Section))
            .Validate(o => ClaudeOptions.AllowedPermissionModes.Contains(o.PermissionMode),
                $"Claude:PermissionMode must be one of: {string.Join(", ", ClaudeOptions.AllowedPermissionModes)}.");
        services.TryAddSingleton<IClaudeLauncher, ClaudeLauncher>();

        services.AddOptions<NotionPageUpdatedOptions>()
            .Bind(configuration.GetSection(NotionPageUpdatedOptions.Section));
        services.TryAddSingleton<PageCooldown>();

        services.AddOptions<TaskOptions>().Bind(configuration.GetSection(TaskOptions.Section));
        services.TryAddSingleton<IClaudeAgents, ClaudeAgents>();
        services.TryAddSingleton<IClaudePrint, ClaudePrint>();
        services.TryAddSingleton<TaskStore>();
        services.AddHostedService<TaskSessionWatcher>();

        foreach (var type in HandlerTypes())
        {
            services.TryAddTransient(type);
        }

        return services;
    }

    /// <summary>Returns each concrete <see cref="IEventHandler"/> class in this assembly that has <see cref="OnAttribute"/>.</summary>
    public static IReadOnlyList<Type> HandlerTypes() =>
        typeof(HandlersServiceCollectionExtensions).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                && typeof(IEventHandler).IsAssignableFrom(t)
                && t.GetCustomAttributes<OnAttribute>().Any())
            .ToList();
}
