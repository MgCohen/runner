namespace App.Host;

// The yellow tray icon while the app starts, so that a shortcut start shows at once that the app runs.
// A click on the icon, or a second start, shows the start message. The icon has its own UI thread, as the main
// thread is busy with the start. TrayApp replaces it.
public sealed class StartNotice : IDisposable {
    const string Message = "The runner is starting. The widget opens when it is ready.";

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

    // A second start while the app starts: show the message.
    public void Remind() => Ui(ShowMessage);

    void ShowMessage() => icon?.ShowBalloonTip(5_000, "Runner", Message, ToolTipIcon.Info);

    void Run() {
        ui = new Control();
        _ = ui.Handle;
        using var dot = TrayApp.Dot(TrayApp.Starting);
        icon = new NotifyIcon { Icon = dot, Text = "Runner: starting", Visible = true };
        icon.MouseClick += (_, _) => ShowMessage();
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
