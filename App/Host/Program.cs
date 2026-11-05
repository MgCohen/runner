namespace App.Host;

// The documented exception to "do not edit Program.cs": it adds the widget window and the tray icon (spec 4.5).
// Start order: build the app with AddVsa and the BlazorWebView services, start Kestrel on 127.0.0.1 port 0,
// then open the form with BlazorWebView.Services = app.Services. One DI container for the modules and the views.
public static class Program {
    [STAThread]
    public static int Main(string[] args) {
        var start = StartOptions.Parse(args);
        TrayApp? tray = null;
        using var instance = SingleInstance.TryStart(start.Mock, () => tray?.ShowWidget());
        if (instance is null) {
            Console.WriteLine("The runner already runs. Its window comes to the front.");
            return 0;
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var app = RunnerApp.Build(start, builder => builder.Services.AddWindowsFormsBlazorWebView());
        app.Start();
        var config = app.Configuration;
        tray = new TrayApp(app, start, Flag(config, "Modules:Settings:openOnStart", true), Flag(config, "Modules:Settings:pinOnStart", false));
        if (start.SelfTest) {
            _ = SelfTest.RunAsync(tray, app);
        }
        Application.Run(tray);
        var code = tray.ExitCode;
        tray.Dispose();
        ((IDisposable)app).Dispose();
        return code;
    }

    static bool Flag(IConfiguration config, string key, bool fallback) => bool.TryParse(config[key], out var value) ? value : fallback;
}
