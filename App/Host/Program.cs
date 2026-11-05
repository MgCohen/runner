using System.Diagnostics;

namespace App.Host;

// The documented exception to "do not edit Program.cs": it adds the widget window, the tray icon and the start order
// (spec 4.5). Start order: commands to a running app (CommandClient), the instance check, the tokens (real mode),
// build the app with AddVsa and the BlazorWebView services, start Kestrel on 127.0.0.1 port 0, then open the form
// with BlazorWebView.Services = app.Services. One DI container for the modules and the views.
// A mode switch saves the mode, stops the app, and starts App.Host.exe again with --restart.
public static class Program {
    [STAThread]
    public static int Main(string[] args) {
        try {
            return Run(args);
        } catch (Exception e) {
            // A shortcut start has no terminal: a start error goes to crash.log and to a message box.
            var path = Path.Combine(ModeFile.BaseFolder, "crash.log");
            Directory.CreateDirectory(ModeFile.BaseFolder);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {e}{Environment.NewLine}");
            Console.Error.WriteLine(e);
            if (!args.Contains("--selftest")) {
                MessageBox.Show($"The runner could not start: {e.Message}{Environment.NewLine}{Environment.NewLine}Details: {path}", "Runner",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return 1;
        }
    }

    static int Run(string[] args) {
        if (CommandClient.IsCommand(args)) {
            return CommandClient.Run(args);
        }
        // A start from a terminal (tray.ps1) keeps its output in that terminal. A shortcut start has no console.
        ParentConsole.Attach();
        var start = StartOptions.Parse(args);
        TrayApp? tray = null;
        AppMode? mode = null;
        bool? restartIn = null;
        var instance = SingleInstance.TryStart(start.SelfTest, start.Restart, () => tray?.ShowWidget(), Command);
        if (instance is null) {
            Console.WriteLine("The runner already runs. Its window comes to the front.");
            // A self-test that did not run is not a pass (A37).
            return start.SelfTest ? 1 : 0;
        }
        // Each data folder has its own WebView2 profile: real mode, mock mode and the self-test do not share a cache.
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(start.DataFolder, "WebView2"));
        var problems = start.Mock ? [] : Secrets.Load();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var app = RunnerApp.Build(start, builder => builder.Services.AddWindowsFormsBlazorWebView());
        foreach (var problem in problems) {
            app.Logger.LogWarning("{Problem}", problem);
        }
        app.Start();
        var config = app.Configuration;
        tray = new TrayApp(app, start, Flag(config, "Modules:Settings:openOnStart", true), Flag(config, "Modules:Settings:pinOnStart", false));
        mode = app.Services.GetRequiredService<AppMode>();
        if (!start.SelfTest) {
            mode.Restart = mock => {
                restartIn ??= mock;
                ModeFile.Write(mock);
                app.Logger.LogInformation("Mode switch: the app restarts in {Mode} mode.", mock ? "mock" : "real");
                Exit();
            };
        }
        // Ctrl+C in the console stops the host: the app then exits as with Exit in the tray menu.
        app.Lifetime.ApplicationStopping.Register(Exit);
        if (start.SelfTest) {
            _ = SelfTest.RunAsync(tray, app);
        }
        Application.Run(tray);
        var code = tray.ExitCode;
        tray.Dispose();
        // Off the UI thread: the UI message loop has ended, so a continuation there would never run.
        Task.Run(() => app.DisposeAsync().AsTask()).GetAwaiter().GetResult();
        instance.Dispose();
        if (restartIn is not null && Environment.ProcessPath is { } path) {
            var again = new ProcessStartInfo(path) { UseShellExecute = false };
            again.ArgumentList.Add("--restart");
            using var _ = Process.Start(again);
        }
        return code;

        void Exit() => tray?.Form.BeginInvoke(async () => await tray.ExitAsync());

        string Command(string command) {
            var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (parts.FirstOrDefault()) {
                case "status":
                    return Status();
                case "show":
                    tray?.ShowWidget();
                    return "ok";
                case "mode" when parts.Length > 1 && parts[1] is "mock" or "real":
                    var mock = parts[1] == "mock";
                    if (mode is null || tray is null) {
                        return $"error: the app is still starting. {Status()}";
                    }
                    if (mock == mode.Mock) {
                        return Status();
                    }
                    mode.Switch(mock);
                    return $"restarting in {parts[1]} mode";
                case "quit":
                    Exit();
                    return "stopping";
                default:
                    return $"error: unknown command '{command}'";
            }
        }

        string Status() => tray is null
            ? $"{Name(start.Mock)} | starting"
            : $"{Name(start.Mock)} | {tray.IconState} | pid {Environment.ProcessId} | data {start.DataFolder}";
    }

    static string Name(bool mock) => mock ? "mock" : "real";

    static bool Flag(IConfiguration config, string key, bool fallback) => bool.TryParse(config[key], out var value) ? value : fallback;
}
