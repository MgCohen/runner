using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Runner.Host;

namespace Runner.Tray;

/// <summary>
/// The tray icon, its menu and the widget. It starts the in-process host and stops it on Exit.
/// If the host stops (for example Ctrl+C in the console), the app exits too.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly IHost _host;
    private readonly RunnerSwitches _switches;
    private readonly SocketListener _listener;
    private readonly WidgetForm _form;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _listenerItem;
    private readonly Dictionary<ListenerState, Icon> _icons;
    private readonly ConcurrentQueue<string> _pendingLines = new();
    private readonly ConcurrentQueue<TrafficEntry> _pendingTraffic = new();
    private int _linesQueued;
    private int _trafficQueued;
    private string? _startError;
    private bool _exiting;

    public TrayApp(IHost host, ActivityFeed feed, bool mock = false, bool selfTest = false)
    {
        _host = host;
        var services = host.Services;
        _switches = services.GetRequiredService<RunnerSwitches>();
        _listener = services.GetRequiredService<SocketListener>();
        var catalog = services.GetRequiredService<HandlerCatalog>();
        var traffic = services.GetRequiredService<TrafficLog>();

        string consumer;
        try
        {
            consumer = services.GetRequiredService<IOptions<InboxOptions>>().Value.ResolvedConsumer;
        }
        catch (OptionsValidationException ex)
        {
            consumer = "runner";
            _startError = ex.Message;
        }

        _icons = Enum.GetValues<ListenerState>().ToDictionary(s => s, s => Theme.MakeIcon(Theme.Of(s)));
        _form = new WidgetForm(consumer, _switches, catalog.Handlers.Select(h => h.Type.Name).Distinct());
        _ = _form.Handle;
        _form.ListenerToggled += on => _switches.SetListener(on);

        _listenerItem = new ToolStripMenuItem("Listener", null, (_, _) => _switches.SetListener(!_switches.ListenerEnabled));
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Show", null, (_, _) => _form.ShowWidget()));
        menu.Items.Add(_listenerItem);
        if (mock && services.GetService<MockEvents>() is { } events)
        {
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Send mock task event", null, (_, _) => events.SendTaskEvent()));
            menu.Items.Add(new ToolStripMenuItem("Send mock GitHub event", null, (_, _) => events.SendGitHubEvent()));
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, async (_, _) => await ExitAsync()));
        _tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (_form.Visible) _form.Hide();
            else _form.ShowWidget();
        };

        _form.AppendActivity(feed.Snapshot());
        foreach (var entry in traffic.Snapshot().Reverse()) _form.AddTraffic(entry);
        feed.LineAdded += line => Queue(_pendingLines, line, ref _linesQueued, FlushLines);
        traffic.EntryAdded += entry => Queue(_pendingTraffic, entry, ref _trafficQueued, FlushTraffic);
        _switches.Changed += () => Post(() =>
        {
            _form.SyncSwitches();
            UpdateState();
        });
        _listener.StateChanged += _ => Post(UpdateState);
        services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => Post(() => _ = ExitAsync()));

        UpdateState();
        _form.ShowWidget();
        _ = StartAsync().ContinueWith(_ => Post(() =>
        {
            if (selfTest) _ = mock ? new SelfTest(_form, services, ExitAsync).RunAsync() : new SelfTest(_form, services, ExitAsync).RunRealAsync();
        }));
    }

    private ListenerState Current =>
        _startError is not null ? ListenerState.Error
        : !_switches.ListenerEnabled ? ListenerState.Paused
        : _listener.State;

    private async Task StartAsync()
    {
        if (_startError is not null)
        {
            _form.AppendActivity([$"The runner did not start: {_startError}"]);
            UpdateState();
            return;
        }
        try
        {
            await _host.StartAsync();
        }
        catch (Exception ex)
        {
            _startError = ex.Message;
            _form.AppendActivity([$"The runner did not start: {ex.Message}"]);
        }
        UpdateState();
    }

    private void UpdateState()
    {
        var state = Current;
        var label = _startError is not null ? "error: see Activity" : Theme.Label(state);
        _form.SetState(state, label);
        _tray.Icon = _icons[state];
        _tray.Text = $"Runner: {label}";
        _listenerItem.Checked = _switches.ListenerEnabled;
        _listenerItem.Text = _switches.ListenerEnabled ? "Listener: on" : "Listener: off";
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        _tray.Text = "Runner: stopping";
        try
        {
            await _host.StopAsync(TimeSpan.FromSeconds(15));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Stop failed: {ex.Message}");
        }
        _tray.Visible = false;
        ExitThread();
    }

    /// <summary>Collects the items of other threads and adds them on the UI thread in one batch.</summary>
    private void Queue<T>(ConcurrentQueue<T> queue, T item, ref int queued, Action flush)
    {
        queue.Enqueue(item);
        if (Interlocked.Exchange(ref queued, 1) == 0) Post(flush);
    }

    private void FlushLines()
    {
        Interlocked.Exchange(ref _linesQueued, 0);
        var lines = new List<string>();
        while (_pendingLines.TryDequeue(out var line)) lines.Add(line);
        _form.AppendActivity(lines);
    }

    private void FlushTraffic()
    {
        Interlocked.Exchange(ref _trafficQueued, 0);
        while (_pendingTraffic.TryDequeue(out var entry)) _form.AddTraffic(entry);
    }

    private void Post(Action action)
    {
        if (_form.IsDisposed || !_form.IsHandleCreated) return;
        try
        {
            _form.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The form is closing.
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _form.Dispose();
            foreach (var icon in _icons.Values) icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
