using System.Text.Json;
using App.Views;
using VSA.Host;

namespace App.Host;

// Builds the runner without its window: the VSA modules, the request endpoints on 127.0.0.1 (T8) and the settings file.
// Program adds the window and the tray icon. The tests use the same builder with no window.
public static class RunnerApp {
    public static WebApplication Build(StartOptions start, Action<WebApplicationBuilder>? configure = null) {
        ArgumentNullException.ThrowIfNull(start);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        var dataDir = start.DataFolder;
        Directory.CreateDirectory(dataDir);
        if (start.Mock && start.SelfTest) {
            ClearMockData(dataDir);
        }
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["Runner:Mock"] = start.Mock ? "true" : "false",
            ["Runner:Paused"] = start.Paused ? "true" : "false",
            ["Runner:DataDir"] = dataDir,
        });
        var warning = AddSettingsFile(builder.Configuration, Path.Combine(dataDir, "settings.json"));
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        // The terminal shows the runner, not each request of the views.
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning).AddFilter("System.Net.Http", LogLevel.Warning)
            .AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information).AddFilter("FastEndpoints", LogLevel.Warning);
        builder.AddVsa();
        builder.Services.AddSingleton<WidgetWindow>();
        builder.Services.AddSingleton<IWidgetWindow>(provider => provider.GetRequiredService<WidgetWindow>());
        builder.Services.AddSingleton<TrayService>();
        if (start.Mock) {
            builder.Services.AddSingleton<SelfTestProbe>();
        }
        configure?.Invoke(builder);
        var app = builder.Build();
        if (warning is not null) {
            app.Logger.LogWarning("{Warning}", warning);
        }
        app.MapVsaRequests();
        return app;
    }

    // The settings file is configuration: Modules:<module>:<key>. A broken file moves to settings.json.bad, and the
    // defaults apply. Returns the warning, or null.
    // The mock self-test starts from an empty store: its checks count the events (A1, A25).
    static void ClearMockData(string dataDir) {
        foreach (var name in new[] { "inbox.db", "inbox.db-wal", "inbox.db-shm", "switches.json", "settings.json", "settings.json.bad" }) {
            File.Delete(Path.Combine(dataDir, name));
        }
    }

    static string? AddSettingsFile(ConfigurationManager configuration, string path) {
        string? warning = null;
        if (File.Exists(path)) {
            try {
                using var _ = JsonDocument.Parse(File.ReadAllText(path));
            } catch (JsonException e) {
                File.Move(path, path + ".bad", overwrite: true);
                warning = $"{path} is not valid ({e.Message}). The settings use the defaults; the file is now {path}.bad.";
            }
        }
        configuration.AddJsonFile(path, optional: true, reloadOnChange: false);
        return warning;
    }
}
