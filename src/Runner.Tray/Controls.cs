using System.ComponentModel;
using System.Drawing.Drawing2D;
using Runner.Host;

namespace Runner.Tray;

/// <summary>The dark theme colors.</summary>
internal static class Theme
{
    public static readonly Color Back = Color.FromArgb(30, 31, 34);
    public static readonly Color Panel = Color.FromArgb(43, 45, 49);
    public static readonly Color Line = Color.FromArgb(60, 63, 69);
    public static readonly Color Text = Color.FromArgb(220, 221, 222);
    public static readonly Color Muted = Color.FromArgb(142, 146, 151);
    public static readonly Color Green = Color.FromArgb(59, 165, 93);
    public static readonly Color Amber = Color.FromArgb(250, 166, 26);
    public static readonly Color Red = Color.FromArgb(237, 66, 69);
    public static readonly Color Gray = Color.FromArgb(116, 127, 141);

    public static readonly Font Body = new("Segoe UI", 9f);
    public static readonly Font Bold = new("Segoe UI Semibold", 9.5f);
    public static readonly Font Small = new("Segoe UI", 7.5f, FontStyle.Bold);
    public static readonly Font Mono = new("Consolas", 8.5f);

    public static Color Of(ListenerState state) => state switch
    {
        ListenerState.Connected => Green,
        ListenerState.Connecting => Amber,
        ListenerState.Paused => Gray,
        _ => Red,
    };

    public static string Label(ListenerState state) => state switch
    {
        ListenerState.Connected => "connected",
        ListenerState.Connecting => "connecting",
        ListenerState.Paused => "paused",
        _ => "error",
    };

    /// <summary>Makes a round tray icon in the state color.</summary>
    public static Icon MakeIcon(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var ring = new SolidBrush(Color.FromArgb(30, 31, 34));
            using var fill = new SolidBrush(color);
            g.FillEllipse(ring, 1, 1, 30, 30);
            g.FillEllipse(fill, 5, 5, 22, 22);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }
}

/// <summary>An on/off switch. A click by the user raises <see cref="Toggled"/>. A set of <see cref="Checked"/> by code does not.</summary>
internal sealed class ToggleSwitch : Control
{
    private bool _checked;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(34, 18);
        Cursor = Cursors.Hand;
        TabStop = true;
        BackColor = Color.Transparent;
    }

    public event EventHandler? Toggled;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
        }
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Checked = !Checked;
        Toggled?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space) OnClick(EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Rounded(track, track.Height / 2))
        using (var brush = new SolidBrush(Checked ? Theme.Green : Theme.Line))
        {
            g.FillPath(brush, path);
        }
        var d = Height - 6;
        var x = Checked ? Width - d - 4 : 3;
        using var knob = new SolidBrush(Color.White);
        g.FillEllipse(knob, x, 3, d, d);
        if (Focused)
        {
            using var pen = new Pen(Theme.Muted) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 90, 180);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 180);
        path.CloseFigure();
        return path;
    }
}

/// <summary>A round status dot.</summary>
internal sealed class StatusDot : Control
{
    private Color _color = Theme.Gray;

    public StatusDot()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(12, 12);
        BackColor = Color.Transparent;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color DotColor
    {
        get => _color;
        set
        {
            _color = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(_color);
        e.Graphics.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
    }
}
