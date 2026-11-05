using System.Drawing.Drawing2D;
using App.Views;

namespace App.Host;

// The tray icon and the widget (C6). The icon shows the listener state: green (connected), amber (connecting),
// gray (paused), red (error). Left click: open or hide the widget. Right click: the tray menu.
// Exit: stop the app (the hosted services cancel the running handlers), then close the window and the icon.
public sealed class TrayApp : ApplicationContext {
    static readonly Dictionary<string, Color> Colors = new() {
        ["Connected"] = Color.FromArgb(0x2f, 0x7d, 0x32),
        ["Connecting"] = Color.FromArgb(0xd8, 0x8c, 0x1a),
        ["Paused"] = Color.FromArgb(0x8f, 0x8d, 0x86),
        ["Error"] = Color.FromArgb(0xb3, 0x26, 0x1e),
    };

    readonly WebApplication app;
    readonly TrayService tray;
    readonly NotifyIcon icon;
    readonly Dictionary<string, Icon> icons;
    readonly ToolStripMenuItem listenerItem;
    bool exiting;

    public TrayApp(WebApplication app, StartOptions start, bool openOnStart, bool pinOnStart) {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(start);
        this.app = app;
        tray = app.Services.GetRequiredService<TrayService>();
        Form = new WidgetForm(app.Services, pinOnStart);
        app.Services.GetRequiredService<WidgetWindow>().Form = Form;
        icons = Colors.ToDictionary(c => c.Key, c => Dot(c.Value));
        listenerItem = new ToolStripMenuItem("Listener on/off", null, (_, _) => Safe(tray.ToggleListenerAsync));
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => Form.ShowWidget());
        menu.Items.Add(listenerItem);
        if (start.Mock) {
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Send mock task event", null, (_, _) => Safe(() => tray.SendMockAsync("task")));
            menu.Items.Add("Send mock GitHub event", null, (_, _) => Safe(() => tray.SendMockAsync("github")));
        }
        var mode = app.Services.GetRequiredService<AppMode>();
        if (!start.SelfTest) {
            menu.Items.Add(mode.Mock ? "Switch to real mode" : "Switch to mock mode", null, (_, _) => mode.Switch(!mode.Mock));
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Safe(ExitAsync));
        icon = new NotifyIcon { Icon = icons["Paused"], Text = "Runner", ContextMenuStrip = menu, Visible = true };
        icon.MouseClick += (_, e) => {
            if (e.Button == MouseButtons.Left) {
                ToggleWidget();
            }
        };
        tray.Changed += () => {
            var state = tray.State;
            Ui(() => UpdateIcon(state));
        };
        _ = tray.StartAsync();
        if (openOnStart || start.SelfTest) {
            Form.ShowWidget();
        } else {
            // The window handle exists, so the tray service can marshal to the UI thread. The page starts when the widget opens.
            _ = Form.Handle;
        }
    }

    public WidgetForm Form { get; }

    public int ExitCode { get; set; }

    public void ShowWidget() => Ui(Form.ShowWidget);

    readonly List<string> history = ["Paused"];

    public string IconState { get; private set; } = "Paused";

    // Each icon state since start, with no repeats in a row (A29).
    public IReadOnlyList<string> IconHistory {
        get {
            lock (history) {
                return [.. history];
            }
        }
    }

    public async Task ExitAsync() {
        if (exiting) {
            return;
        }
        exiting = true;
        icon.Text = "Runner · stopping";
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try {
            await app.StopAsync(limit.Token);
        } catch (OperationCanceledException) {
        }
        Form.Exiting = true;
        Form.Close();
        icon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            icon.Dispose();
            foreach (var i in icons.Values) {
                i.Dispose();
            }
            Form.Dispose();
        }
        base.Dispose(disposing);
    }

    // A menu action: an error goes to the console, not to the UI thread.
    static async void Safe(Func<Task> action) {
        try {
            await action();
        } catch (Exception e) {
            Console.Error.WriteLine($"Tray action failed: {e.Message}");
        }
    }

    void ToggleWidget() {
        if (Form.Visible) {
            Form.Hide();
        } else {
            Form.ShowWidget();
        }
    }

    void UpdateIcon(string state) {
        IconState = Colors.ContainsKey(state) ? state : "Paused";
        lock (history) {
            if (history.Count == 0 || history[^1] != IconState) {
                history.Add(IconState);
            }
        }
        icon.Icon = icons[IconState];
        var text = tray.Tooltip;
        icon.Text = text.Length > 63 ? text[..63] : text;
        listenerItem.Text = tray.ListenerOn ? "Listener off" : "Listener on";
    }

    void Ui(Action action) {
        if (Form.IsDisposed || exiting) {
            return;
        }
        if (Form.IsHandleCreated && Form.InvokeRequired) {
            Form.BeginInvoke(action);
        } else {
            action();
        }
    }

    // A 16 × 16 dot of the state color, with a white ring.
    static Icon Dot(Color color) {
        using var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap)) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var ring = new SolidBrush(Color.White);
            using var fill = new SolidBrush(color);
            g.FillEllipse(ring, 0, 0, 15, 15);
            g.FillEllipse(fill, 2, 2, 11, 11);
        }
        var handle = bitmap.GetHicon();
        using var temp = System.Drawing.Icon.FromHandle(handle);
        var copy = (Icon)temp.Clone();
        DestroyIcon(handle);
        return copy;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool DestroyIcon(nint handle);
}
