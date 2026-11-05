namespace App.Host;

// The tray icon while the app starts. The start can take some minutes after an install, because the virus scanner
// examines each new DLL when the app loads it. Without this icon, a shortcut start shows nothing for that time.
// The icon has its own UI thread, as the main thread is busy with the start. TrayApp replaces it.
public sealed class StartNotice : IDisposable {
    const string Message = "The runner is starting. The first start after an install can take some minutes.";

    readonly Thread thread;
    readonly ManualResetEventSlim ready = new();
    Control? ui;
    NotifyIcon? icon;

    StartNotice() {
        thread = new Thread(Run) { IsBackground = true, Name = "Runner start notice" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(10));
    }

    public static StartNotice Show() => new();

    // A second start while the app starts: show the message again.
    public void Remind() => Ui(() => icon?.ShowBalloonTip(10_000, "Runner", Message, ToolTipIcon.Info));

    void Run() {
        ui = new Control();
        _ = ui.Handle;
        var path = Environment.ProcessPath;
        icon = new NotifyIcon {
            Icon = path is null ? SystemIcons.Application : Icon.ExtractAssociatedIcon(path) ?? SystemIcons.Application,
            Text = "Runner: starting",
            Visible = true,
        };
        icon.ShowBalloonTip(10_000, "Runner", Message, ToolTipIcon.Info);
        ready.Set();
        Application.Run();
        icon.Visible = false;
        icon.Dispose();
        ui.Dispose();
    }

    void Ui(Action action) {
        if (ui is { IsHandleCreated: true, IsDisposed: false }) {
            try {
                ui.BeginInvoke(action);
            } catch (InvalidOperationException) {
                // The notice has closed.
            }
        }
    }

    public void Dispose() {
        Ui(Application.ExitThread);
        thread.Join(TimeSpan.FromSeconds(5));
        ready.Dispose();
    }
}
