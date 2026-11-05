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
            // A self-test that did not run is not a pass (A37).
            return start.SelfTest ? 1 : 0;
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var app = RunnerApp.Build(start, builder => builder.Services.AddWindowsFormsBlazorWebView());
        app.Start();
        var config = app.Configuration;
        tray = new TrayApp(app, start, Flag(config, "Modules:Settings:openOnStart", true), Flag(config, "Modules:Settings:pinOnStart", false));
        // Ctrl+C in the console stops the host: the app then exits as with Exit in the tray menu.
        app.Lifetime.ApplicationStopping.Register(() => tray.Form.BeginInvoke(async () => await tray.ExitAsync()));
        if (start.SelfTest) {
            _ = SelfTest.RunAsync(tray, app);
        }
        Application.Run(tray);
        var code = tray.ExitCode;
        tray.Dispose();
        // Off the UI thread: the UI message loop has ended, so a continuation there would never run.
        Task.Run(() => app.DisposeAsync().AsTask()).GetAwaiter().GetResult();
        return code;
    }

    static bool Flag(IConfiguration config, string key, bool fallback) => bool.TryParse(config[key], out var value) ? value : fallback;
}
