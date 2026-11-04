using Microsoft.Extensions.DependencyInjection;

namespace Runner.Host.Tests;

/// <summary>The pause semantics of <see cref="RunnerSwitches"/> in the listener, the catch-up and the dispatcher.</summary>
public sealed class SwitchTests : IAsyncLifetime
{
    private FakeInbox _inbox = null!;

    public async Task InitializeAsync() => _inbox = await FakeInbox.StartAsync();

    public async Task DisposeAsync() => await _inbox.DisposeAsync();

    private bool Acked(long id) => _inbox.IsConsumed(TestHost.Consumer, id);

    private static async Task Connected(TestHost host, int count = 1) =>
        await TestHost.WaitUntil(() => host.Socket.Connections >= count, $"{count} socket connections");

    [Fact]
    public async Task Listener_off_at_start_does_not_connect_or_catch_up_until_on()
    {
        var switches = RunnerSwitches.InMemory();
        switches.SetListener(false);
        await _inbox.AddAsync(TestHost.Event(1, "ok"), push: false);

        await using var host = await TestHost.StartAsync(_inbox, switches: switches);
        await Task.Delay(400);

        Assert.Equal(0, _inbox.ListenRequests);
        Assert.Equal(0, _inbox.CatchUpCalls);
        Assert.Null(host.Store.Get(1));
        Assert.Equal(ListenerState.Paused, host.Socket.State);

        switches.SetListener(true);

        await TestHost.WaitUntil(() => Acked(1), "ack of event 1");
        Assert.Equal(1, host.Recorder.Count("ok", 1));
        Assert.Equal(ListenerState.Connected, host.Socket.State);
    }

    [Fact]
    public async Task Listener_off_closes_the_socket_does_not_reconnect_and_does_not_run_handlers()
    {
        await using var host = await TestHost.StartAsync(_inbox);
        await Connected(host);

        host.Switches.SetListener(false);
        await TestHost.WaitUntil(() => _inbox.OpenSockets == 0, "socket closed");
        await TestHost.WaitUntil(() => host.Socket.State == ListenerState.Paused, "listener paused");
        var requests = _inbox.ListenRequests;

        // The inbox gets event 2. A stored event 3 is due in the local store.
        await _inbox.AddAsync(TestHost.Event(2, "ok"));
        host.Store.TryAdd(TestHost.Event(3, "ok"));
        host.Host.Services.GetRequiredService<DispatchSignal>().Set();
        await Task.Delay(400);

        Assert.Equal(requests, _inbox.ListenRequests);
        Assert.Null(host.Store.Get(2));
        Assert.Equal(0, host.Recorder.Count("ok", 3));
        Assert.Equal(EventStatus.Pending, host.Store.Get(3)!.Status);

        host.Switches.SetListener(true);

        await TestHost.WaitUntil(() => Acked(2) && Acked(3), "ack of events 2 and 3");
        Assert.Equal(1, host.Recorder.Count("ok", 2));
        Assert.Equal(1, host.Recorder.Count("ok", 3));
    }

    [Fact]
    public async Task Source_off_keeps_the_event_in_the_store_without_a_run_or_an_ack()
    {
        await using var host = await TestHost.StartAsync(_inbox);
        await Connected(host);
        host.Switches.SetSource("test", false);

        await _inbox.AddAsync(TestHost.Event(4, "ok"));
        await TestHost.WaitUntil(() => host.Store.Get(4) is not null, "event 4 stored");
        await Task.Delay(400);

        Assert.Equal(0, host.Recorder.Count("ok", 4));
        Assert.Equal(EventStatus.Pending, host.Store.Get(4)!.Status);
        Assert.False(Acked(4));

        host.Switches.SetSource("test", true);

        await TestHost.WaitUntil(() => Acked(4), "ack of event 4");
        Assert.Equal(1, host.Recorder.Count("ok", 4));
        Assert.Equal(1, host.Recorder.Count("second", 4));
    }

    [Fact]
    public async Task Paused_handler_keeps_the_event_pending_and_the_dispatcher_does_not_spin()
    {
        await using var host = await TestHost.StartAsync(_inbox);
        await Connected(host);
        host.Switches.SetHandler(nameof(SecondHandler), false);

        await _inbox.AddAsync(TestHost.Event(5, "ok"));
        await TestHost.WaitUntil(() => host.Recorder.Count("ok", 5) == 1, "ok handler of event 5");
        await Task.Delay(300);

        var row = host.Store.Get(5)!;
        Assert.Equal(EventStatus.Pending, row.Status);
        Assert.Contains(typeof(OkHandler).FullName!, row.DoneHandlers);
        Assert.Equal(0, host.Recorder.Count("second", 5));
        Assert.False(Acked(5));

        var passes = host.Dispatcher.Passes;
        await Task.Delay(500);
        Assert.InRange(host.Dispatcher.Passes - passes, 0, 1);

        host.Switches.SetHandler(nameof(SecondHandler), true);

        await TestHost.WaitUntil(() => Acked(5), "ack of event 5");
        Assert.Equal(1, host.Recorder.Count("second", 5));
        Assert.Equal(1, host.Recorder.Count("ok", 5));
        Assert.Equal(EventStatus.Done, host.Store.Get(5)!.Status);
    }

    [Fact]
    public async Task Event_whose_only_handler_is_paused_does_not_run()
    {
        await using var host = await TestHost.StartAsync(_inbox);
        await Connected(host);
        host.Switches.SetHandler(nameof(PermanentHandler), false);

        await _inbox.AddAsync(TestHost.Event(6, "permanent"));
        await TestHost.WaitUntil(() => host.Store.Get(6) is not null, "event 6 stored");
        await Task.Delay(400);

        Assert.Equal(0, host.Recorder.Count("permanent", 6));
        Assert.Equal(0, host.Store.Get(6)!.Attempts);
        Assert.False(Acked(6));
    }

    [Fact]
    public async Task Traffic_log_records_the_socket_and_the_http_calls()
    {
        await using var host = await TestHost.StartAsync(_inbox);
        await Connected(host);
        await _inbox.AddAsync(TestHost.Event(7, "ok"));
        await TestHost.WaitUntil(() => Acked(7), "ack of event 7");

        var entries = host.Traffic.Snapshot();
        Assert.Contains(entries, t => t.Kind == "connect" && t.Target.EndsWith("/listen") && t.Status == "open");
        Assert.Contains(entries, t => t.Kind == "hello" && t.Direction == "in");
        Assert.Contains(entries, t => t.Kind == "event" && t.Target == "event 7" && t.Size > 0);
        Assert.Contains(entries, t => t.Kind == "GET" && t.Target.EndsWith("/api/events") && t.Status == "200");
        Assert.Contains(entries, t => t.Kind == "POST" && t.Target.EndsWith("/api/consume") && t.Status == "200");
        Assert.DoesNotContain(entries, t => t.Target.Contains('?') || t.Target.Contains(FakeInbox.Token));
    }
}
