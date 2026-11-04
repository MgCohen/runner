namespace Runner.Host.Tests;

public sealed class ConnectorTests : IAsyncLifetime
{
    private FakeInbox _inbox = null!;

    public async Task InitializeAsync() => _inbox = await FakeInbox.StartAsync();

    public async Task DisposeAsync() => await _inbox.DisposeAsync();

    private bool Acked(long id) => _inbox.IsConsumed(TestHost.Consumer, id);

    private static async Task Connected(TestHost host, int count = 1) =>
        await TestHost.WaitUntil(() => host.Socket.Connections >= count, $"{count} socket connections");

    [Fact]
    public async Task Pushed_event_runs_each_matching_handler_and_is_acked()
    {
        await using var host = await TestHost.StartAsync(_inbox);
        await Connected(host);

        await _inbox.AddAsync(TestHost.Event(1, "ok"));

        await TestHost.WaitUntil(() => Acked(1), "ack of event 1");
        Assert.Equal(1, host.Recorder.Count("ok", 1));
        Assert.Equal(1, host.Recorder.Count("second", 1));
        Assert.Equal(EventStatus.Done, host.Store.Get(1)!.Status);
        Assert.NotNull(host.Store.Get(1)!.AckedAt);
    }

    [Fact]
    public async Task Same_event_from_push_and_catch_up_runs_one_time()
    {
        await _inbox.AddAsync(TestHost.Event(7, "ok"), push: false);
        await using var host = await TestHost.StartAsync(_inbox);
        await Connected(host);

        // The catch-up at start stores event 7. The pushes give the same id again.
        await _inbox.PushAsync(TestHost.Event(7, "ok"));
        await _inbox.PushAsync(TestHost.Event(7, "ok"));
        await TestHost.WaitUntil(() => Acked(7), "ack of event 7");
        await Task.Delay(300);

        Assert.Equal(1, host.Recorder.Count("ok", 7));
        Assert.Single(_inbox.ConsumeCalls, c => c.Contains(7L));
    }

    [Fact]
    public async Task Failed_handler_retries_and_done_handlers_do_not_run_again()
    {
        await using var host = await TestHost.StartAsync(_inbox);
        await Connected(host);

        await _inbox.AddAsync(TestHost.Event(2, "flaky"));

        await TestHost.WaitUntil(() => Acked(2), "ack of event 2");
        Assert.Equal(3, host.Recorder.Count("flaky", 2));
        Assert.Equal(1, host.Recorder.Count("second", 2));
        var row = host.Store.Get(2)!;
        Assert.Equal(EventStatus.Done, row.Status);
        Assert.Equal(3, row.Attempts);
    }

    [Fact]
    public async Task Handler_that_always_fails_stops_after_5_attempts_and_is_acked()
    {
        await using var host = await TestHost.StartAsync(_inbox);
        await Connected(host);

        await _inbox.AddAsync(TestHost.Event(3, "broken"));

        await TestHost.WaitUntil(() => Acked(3), "ack of event 3");
        await Task.Delay(300);
        Assert.Equal(5, host.Recorder.Count("broken", 3));
        var row = host.Store.Get(3)!;
        Assert.Equal(EventStatus.Failed, row.Status);
        Assert.Equal(5, row.Attempts);
        Assert.Contains("always fails", row.LastError);
    }

    [Fact]
    public async Task Permanent_failure_does_not_retry_and_is_acked()
    {
        await using var host = await TestHost.StartAsync(_inbox);
        await Connected(host);

        await _inbox.AddAsync(TestHost.Event(4, "permanent"));

        await TestHost.WaitUntil(() => Acked(4), "ack of event 4");
        await Task.Delay(300);
        Assert.Equal(1, host.Recorder.Count("permanent", 4));
        var row = host.Store.Get(4)!;
        Assert.Equal(EventStatus.Failed, row.Status);
        Assert.Contains("bad payload", row.LastError);
    }

    [Fact]
    public async Task Event_without_a_matching_handler_is_acked()
    {
        await using var host = await TestHost.StartAsync(_inbox);
        await Connected(host);

        await _inbox.AddAsync(TestHost.Event(5, "page", source: "notion"));

        await TestHost.WaitUntil(() => Acked(5), "ack of event 5");
        Assert.Empty(host.Recorder.Calls);
        Assert.Equal(EventStatus.Done, host.Store.Get(5)!.Status);
    }

    [Fact]
    public async Task Ack_comes_only_after_the_handler_is_done()
    {
        await using var host = await TestHost.StartAsync(_inbox);
        await Connected(host);

        await _inbox.AddAsync(TestHost.Event(6, "gate"));
        await TestHost.WaitUntil(() => host.Recorder.Count("gate", 6) == 1, "start of the gate handler");
        await Task.Delay(300);
        Assert.False(Acked(6));

        host.Recorder.Gate.SetResult();
        await TestHost.WaitUntil(() => Acked(6), "ack of event 6");
    }

    [Fact]
    public async Task Failed_ack_retries_without_a_second_handler_run()
    {
        _inbox.FailConsume(3);
        await using var host = await TestHost.StartAsync(_inbox);
        await Connected(host);

        await _inbox.AddAsync(TestHost.Event(8, "ok"));

        await TestHost.WaitUntil(() => Acked(8), "ack of event 8");
        Assert.Equal(1, host.Recorder.Count("ok", 8));
    }

    [Fact]
    public async Task Ack_that_fails_before_a_restart_is_sent_after_the_restart()
    {
        var folder = Path.Combine(Path.GetTempPath(), "runner-host-tests", Guid.NewGuid().ToString("N"));
        try
        {
            _inbox.FailConsume(int.MaxValue);
            await using (var first = await TestHost.StartAsync(_inbox, folder))
            {
                await Connected(first);
                await _inbox.AddAsync(TestHost.Event(9, "ok"));
                await TestHost.WaitUntil(() => first.Store.Get(9)?.Status == EventStatus.Done, "handler of event 9");
                Assert.Equal(1, first.Recorder.Count("ok", 9));
            }

            _inbox.FailConsume(0);
            await using var second = await TestHost.StartAsync(_inbox, folder);
            await TestHost.WaitUntil(() => Acked(9), "ack of event 9 after the restart");
            await Task.Delay(300);
            Assert.Equal(0, second.Recorder.Count("ok", 9));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Reconnect_runs_a_catch_up_that_gets_the_missed_events()
    {
        await using var host = await TestHost.StartAsync(_inbox);
        await Connected(host);
        await _inbox.AddAsync(TestHost.Event(10, "ok"));
        await TestHost.WaitUntil(() => Acked(10), "ack of event 10");

        // Simulate a network failure. Event 11 arrives while no socket is open, so no push reaches the host.
        _inbox.DropSockets();
        await _inbox.AddAsync(TestHost.Event(11, "ok"), push: false);

        await Connected(host, 2);
        await TestHost.WaitUntil(() => Acked(11), "ack of event 11 after the reconnect");
        Assert.Equal(1, host.Recorder.Count("ok", 11));
        Assert.True(_inbox.ListenRequests >= 2);
    }
}
