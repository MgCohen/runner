using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Runner.Abstractions;
using Runner.Handlers;
using Runner.Host;

namespace Runner.Tray;

internal static class Program
{
    /// <summary>The user secrets of Runner.Host. The tray reads the same secrets.</summary>
    private const string HostUserSecretsId = "runner-host-8f0c2d4e-5b1a-4f7e-9c3d-2a6b1e0f7d91";

    /// <summary>
    /// Builds the connector host in this process, with the services of Runner.Host and Runner.Handlers,
    /// and runs it next to the WinForms message loop. This process starts no other runner process.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var feed = new ActivityFeed();
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Configuration.AddUserSecrets(HostUserSecretsId);
        builder.Configuration.AddInboxTokenVariable();
        builder.Logging.AddProvider(new ActivityLoggerProvider(feed));

        // Registered before AddRunnerHost, so these replace the console defaults.
        builder.Services.AddSingleton(RunnerSwitches.Load(RunnerSwitches.DefaultPath));
        builder.Services.AddSingleton<IActivitySink>(feed);

        builder.Services.AddRunnerHost(builder.Configuration);
        builder.Services.AddHandlers(builder.Configuration);
        builder.Services.AddHandlerAssembly(typeof(HandlersServiceCollectionExtensions).Assembly);

        using var host = builder.Build();
        using var app = new TrayApp(host, feed);
        Application.Run(app);
        return 0;
    }
}
