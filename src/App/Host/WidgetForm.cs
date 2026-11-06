using System.Runtime.InteropServices;
using App.Views;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Web.WebView2.Core;

namespace App.Host;

// The widget (C6): 350 × 560 logical pixels, no title bar, rounded corners, a thin border. It holds the BlazorWebView.
// A click outside does not hide it. Closing it hides it; only Exit in the tray menu stops the app.
public sealed partial class WidgetForm : Form {
    const int LogicalWidth = 350;
    const int LogicalHeight = 560;
    readonly BlazorWebView view;
    Point? dragged;

    public WidgetForm(IServiceProvider services, bool pinned) {
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = pinned;
        Text = "Runner";
        BackColor = Color.FromArgb(0x30, 0x30, 0x2e);
        view = new BlazorWebView { Dock = DockStyle.Fill, HostPage = "wwwroot/index.html", Services = services };
        view.RootComponents.Add<Main>("#app");
        Controls.Add(view);
        Size = Scaled();
        Location = HomeLocation();
    }

    // The WebView2 control, after it starts. The self-test uses it for screenshots and DOM checks.
    public CoreWebView2? Web => view.WebView.CoreWebView2;

    // Exit sets it, so the close is not a hide.
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Exiting { get; set; }

    public void ShowWidget() {
        if (!Visible) {
            // At each start the widget is bottom right; after a drag it keeps the position until exit.
            Location = dragged ?? HomeLocation();
            Show();
        }
        if (WindowState == FormWindowState.Minimized) {
            WindowState = FormWindowState.Normal;
        }
        Activate();
    }

    public void BeginDrag() {
        // The mouse event comes from the page after a round trip: after a fast click the button is up again.
        if (MouseButtons != MouseButtons.Left) {
            return;
        }
        ReleaseCapture();
        _ = SendMessage(Handle, WmNcLButtonDown, HtCaption, 0);
        dragged = Location;
    }

    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        var round = DwmwcpRound;
        _ = DwmSetWindowAttribute(Handle, DwmwaWindowCornerPreference, ref round, sizeof(int));
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e) {
        base.OnDpiChanged(e);
        Size = Scaled();
    }

    // No maximize from Win+Up or a drag to the top edge: the widget keeps its fixed size.
    protected override void WndProc(ref Message m) {
        if (m.Msg == WmSysCommand && ((int)m.WParam & 0xFFF0) == ScMaximize) {
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e) {
        if (!Exiting && e.CloseReason == CloseReason.UserClosing) {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    Size Scaled() {
        var scale = DeviceDpi / 96f;
        return new Size((int)(LogicalWidth * scale), (int)(LogicalHeight * scale));
    }

    Point HomeLocation() {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        var margin = (int)(12 * DeviceDpi / 96f);
        return new Point(area.Right - Width - margin, area.Bottom - Height - margin);
    }

    const int WmNcLButtonDown = 0xA1;
    const int WmSysCommand = 0x112;
    const int ScMaximize = 0xF030;
    const int HtCaption = 2;
    const int DwmwaWindowCornerPreference = 33;
    const int DwmwcpRound = 2;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint hwnd, int msg, nint wParam, nint lParam);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
