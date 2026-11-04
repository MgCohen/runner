using Runner.Host;

namespace Runner.Tray;

/// <summary>
/// The borderless dark widget: header with status and listener switch, sources, handlers,
/// and the Activity and Traffic tabs. A close hides it. The tray menu exits the app.
/// </summary>
internal sealed class WidgetForm : Form
{
    private const int Gutter = 14;

    private readonly RunnerSwitches _switches;
    private readonly StatusDot _dot = new();
    private readonly Label _status = new();
    private readonly ToggleSwitch _listener = new();
    private readonly Dictionary<string, ToggleSwitch> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToggleSwitch> _handlers = new(StringComparer.Ordinal);
    private readonly ToggleSwitch _onTop = new();
    private readonly RichTextBox _activity = new();
    private readonly ListView _traffic = new();
    private readonly Label _activityTab = new();
    private readonly Label _trafficTab = new();
    private int _activityLines;
    private Point? _dragOffset;
    private bool _placed;

    public WidgetForm(string consumer, RunnerSwitches switches, IEnumerable<string> handlerNames)
    {
        _switches = switches;
        SuspendLayout();

        // The layout uses pixels at 96 DPI. The form scales them to the DPI of the screen.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Runner";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        ClientSize = new Size(380, 520);
        BackColor = Theme.Line;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        Padding = new Padding(1);
        KeyPreview = true;

        var header = BuildHeader(consumer);
        var sources = BuildSources();
        var handlers = BuildHandlers(handlerNames.ToList());
        var tabs = BuildTabBar();
        var footer = BuildFooter();
        var content = BuildContent();

        // Docking order is the reverse of the add order: the last control added docks first.
        Control[] order = [header, sources, handlers, tabs, footer, content];
        for (var i = order.Length - 1; i >= 0; i--) Controls.Add(order[i]);
        ResumeLayout(false);
        PerformLayout();

        ShowTab(activity: true);
        SyncSwitches();
    }

    /// <summary>Occurs when the user turns the listener switch.</summary>
    public event Action<bool>? ListenerToggled;

    public void SetState(ListenerState state, string? detail = null)
    {
        _dot.DotColor = Theme.Of(state);
        _status.Text = detail ?? Theme.Label(state);
    }

    /// <summary>Reads the switches again. Call it on the UI thread.</summary>
    public void SyncSwitches()
    {
        _listener.Checked = _switches.ListenerEnabled;
        foreach (var (name, toggle) in _sources) toggle.Checked = _switches.IsSourceEnabled(name);
        foreach (var (name, toggle) in _handlers) toggle.Checked = _switches.IsHandlerEnabled(name);
    }

    /// <summary>Shows the widget. The first time, it goes to the bottom right of the work area.</summary>
    public void ShowWidget()
    {
        if (!_placed)
        {
            var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
            Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
            _placed = true;
        }
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    /// <summary>Adds lines to the Activity tab, newest at the bottom, and keeps the last 1,000 lines. Call it on the UI thread.</summary>
    public void AppendActivity(IEnumerable<string> lines)
    {
        var text = string.Concat(lines.Select(l => l.Replace("\r", "") + "\n"));
        if (text.Length == 0) return;
        _activity.AppendText(text);
        _activityLines += text.Count(c => c == '\n');
        if (_activityLines > ActivityFeed.Capacity)
        {
            var cut = _activity.GetFirstCharIndexFromLine(_activityLines - ActivityFeed.Capacity);
            if (cut > 0)
            {
                _activity.ReadOnly = false;
                _activity.Select(0, cut);
                _activity.SelectedText = "";
                _activity.ReadOnly = true;
            }
            _activityLines = ActivityFeed.Capacity;
        }
        _activity.SelectionStart = _activity.TextLength;
        _activity.ScrollToCaret();
    }

    /// <summary>Adds a row to the top of the Traffic tab and keeps the last 500 rows. Call it on the UI thread.</summary>
    public void AddTraffic(TrafficEntry t)
    {
        var item = new ListViewItem(t.Time.ToLocalTime().ToString("HH:mm:ss"))
        {
            ForeColor = t.Status is "error" or "bad JSON" || t.Status.StartsWith('4') || t.Status.StartsWith('5') ? Theme.Red : Theme.Text,
            BackColor = Theme.Back,
        };
        item.SubItems.Add(t.Direction);
        item.SubItems.Add(t.Kind);
        item.SubItems.Add(t.Target);
        item.SubItems.Add(t.Status);
        item.SubItems.Add(t.Size is { } s ? FormatSize(s) : "");
        item.SubItems.Add(t.Duration is { } d ? $"{d.TotalMilliseconds:0} ms" : "");
        _traffic.BeginUpdate();
        _traffic.Items.Insert(0, item);
        while (_traffic.Items.Count > TrafficLog.Capacity) _traffic.Items.RemoveAt(_traffic.Items.Count - 1);
        _traffic.EndUpdate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) Hide();
    }

    private Panel BuildHeader(string consumer)
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Panel };
        _dot.Location = new Point(Gutter, 17);
        var name = new Label
        {
            Text = consumer,
            Font = Theme.Bold,
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(Gutter + 18, 6),
        };
        _status.AutoSize = true;
        _status.ForeColor = Theme.Muted;
        _status.Location = new Point(Gutter + 18, 25);

        var close = new Label
        {
            Text = "✕",
            ForeColor = Theme.Muted,
            AutoSize = false,
            Size = new Size(26, 26),
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(378 - 8 - 26, 10),
        };
        close.Click += (_, _) => Hide();
        close.MouseEnter += (_, _) => close.ForeColor = Theme.Text;
        close.MouseLeave += (_, _) => close.ForeColor = Theme.Muted;

        _listener.Location = new Point(close.Left - 6 - _listener.Width, 14);
        _listener.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _listener.Toggled += (_, _) => ListenerToggled?.Invoke(_listener.Checked);
        var label = new Label { Text = "Listener", ForeColor = Theme.Text, AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        label.Location = new Point(_listener.Left - 6 - TextRenderer.MeasureText(label.Text, Theme.Body).Width, 15);

        header.Controls.AddRange([_dot, name, _status, label, _listener, close]);
        foreach (Control c in new Control[] { header, _dot, name, _status, label }) EnableDrag(c);
        return header;
    }

    private Panel BuildSources()
    {
        var panel = Section("SOURCES", 58, out var top);
        var x = Gutter;
        foreach (var source in RunnerSwitches.KnownSources)
        {
            var label = new Label { Text = source, AutoSize = true, ForeColor = Theme.Text, Location = new Point(x, top + 1) };
            var width = TextRenderer.MeasureText(source, Theme.Body).Width;
            var toggle = new ToggleSwitch { Location = new Point(x + width + 4, top) };
            var name = source;
            toggle.Toggled += (_, _) => _switches.SetSource(name, toggle.Checked);
            _sources[source] = toggle;
            panel.Controls.AddRange([label, toggle]);
            x += width + 4 + toggle.Width + 24;
        }
        return panel;
    }

    private Panel BuildHandlers(IReadOnlyList<string> names)
    {
        const int row = 24;
        var panel = Section("HANDLERS", 28 + Math.Max(names.Count, 1) * row + 4, out var top);
        if (names.Count == 0)
        {
            panel.Controls.Add(new Label { Text = "No handlers", ForeColor = Theme.Muted, AutoSize = true, Location = new Point(Gutter, top) });
        }
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            var y = top + i * row;
            var label = new Label { Text = name, AutoSize = true, ForeColor = Theme.Text, Location = new Point(Gutter, y + 1) };
            var toggle = new ToggleSwitch { Location = new Point(378 - Gutter - 34, y) };
            toggle.Toggled += (_, _) => _switches.SetHandler(name, toggle.Checked);
            _handlers[name] = toggle;
            panel.Controls.AddRange([label, toggle]);
        }
        return panel;
    }

    private Panel BuildTabBar()
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Theme.Panel };
        SetupTab(_activityTab, "Activity", Gutter, () => ShowTab(activity: true));
        SetupTab(_trafficTab, "Traffic", Gutter + 80, () => ShowTab(activity: false));
        bar.Controls.AddRange([_activityTab, _trafficTab]);
        return bar;
    }

    private Panel BuildFooter()
    {
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Theme.Panel };
        var label = new Label { Text = "Always on top", AutoSize = true, ForeColor = Theme.Muted, Location = new Point(Gutter, 8) };
        _onTop.Location = new Point(Gutter + TextRenderer.MeasureText(label.Text, Theme.Body).Width + 6, 6);
        _onTop.Toggled += (_, _) => TopMost = _onTop.Checked;
        footer.Controls.AddRange([label, _onTop]);
        return footer;
    }

    private Panel BuildContent()
    {
        var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back };

        _activity.Dock = DockStyle.Fill;
        _activity.ReadOnly = true;
        _activity.BorderStyle = BorderStyle.None;
        _activity.BackColor = Theme.Back;
        _activity.ForeColor = Theme.Text;
        _activity.Font = Theme.Mono;
        _activity.WordWrap = false;
        _activity.DetectUrls = false;
        _activity.ScrollBars = RichTextBoxScrollBars.Both;

        _traffic.Dock = DockStyle.Fill;
        _traffic.View = View.Details;
        _traffic.FullRowSelect = true;
        _traffic.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _traffic.BorderStyle = BorderStyle.None;
        _traffic.BackColor = Theme.Back;
        _traffic.ForeColor = Theme.Text;
        _traffic.Font = Theme.Body;
        _traffic.OwnerDraw = true;
        _traffic.DrawColumnHeader += (_, e) =>
        {
            using var back = new SolidBrush(Theme.Panel);
            e.Graphics.FillRectangle(back, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text, Theme.Small, e.Bounds, Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        _traffic.DrawItem += (_, e) => e.DrawDefault = true;
        _traffic.DrawSubItem += (_, e) => e.DrawDefault = true;
        foreach (var (title, width) in new[] { ("Time", 56), ("Dir", 30), ("Kind", 52), ("Target", 112), ("Status", 48), ("Size", 48), ("Duration", 56) })
        {
            _traffic.Columns.Add(title, width);
        }

        content.Controls.AddRange([_activity, _traffic]);
        return content;
    }

    private Panel Section(string title, int height, out int top)
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = height, BackColor = Theme.Back };
        panel.Controls.Add(new Label { Text = title, Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(Gutter, 8) });
        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Line);
            e.Graphics.DrawLine(pen, 0, panel.Height - 1, panel.Width, panel.Height - 1);
        };
        top = 28;
        return panel;
    }

    private void SetupTab(Label tab, string text, int x, Action onClick)
    {
        tab.Text = text;
        tab.AutoSize = false;
        tab.Size = new Size(72, 30);
        tab.Location = new Point(x, 0);
        tab.TextAlign = ContentAlignment.MiddleCenter;
        tab.Cursor = Cursors.Hand;
        tab.Click += (_, _) => onClick();
        tab.Paint += (_, e) =>
        {
            if (tab.Tag is not true) return;
            using var accent = new SolidBrush(Theme.Green);
            e.Graphics.FillRectangle(accent, 8, tab.Height - 3, tab.Width - 16, 3);
        };
    }

    private void ShowTab(bool activity)
    {
        _activity.Visible = activity;
        _traffic.Visible = !activity;
        _activityTab.Tag = activity;
        _trafficTab.Tag = !activity;
        _activityTab.ForeColor = activity ? Theme.Text : Theme.Muted;
        _trafficTab.ForeColor = activity ? Theme.Muted : Theme.Text;
        _activityTab.Invalidate();
        _trafficTab.Invalidate();
    }

    private void EnableDrag(Control control)
    {
        control.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _dragOffset = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top);
        };
        control.MouseMove += (_, _) =>
        {
            if (_dragOffset is { } o) Location = new Point(Cursor.Position.X - o.X, Cursor.Position.Y - o.Y);
        };
        control.MouseUp += (_, _) => _dragOffset = null;
    }

    private static string FormatSize(long bytes) => bytes < 1024 ? $"{bytes} B" : $"{bytes / 1024.0:0.0} KB";
}
