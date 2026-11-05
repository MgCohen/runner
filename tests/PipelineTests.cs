using Handling.Domain;
using Listener.Slices.SetListener;

namespace App.Tests;

// The event pipeline of spec 4.3 on the real app in mock mode: store, handler lanes, retries, results and ack.
public sealed class PipelineTests {
    static HandlerResult Result(TestApp app, long id, string handler) =>
        app.Store.Get(id)!.Results.Single(r => r.HandlerKey.EndsWith("." + handler, StringComparison.Ordinal));

    [Test]
    public async Task A18_catch_up_gets_the_events_that_came_while_paused_and_acks_each_one() {
        await using var app = await TestApp.StartAsync(paused: true);
        long[] ids = [app.Send("echo"), app.Send("echo"), app.Send("echo")];
        await Task.Delay(500);
        Assert.That(ids.Select(app.Store.StatusOf), Has.All.Null, "A paused listener stores nothing.");

        await app.Bus.Send(new SetListenerRequest(true));

        foreach (var id in ids) {
            await app.UntilAcked(id);
            Assert.That(app.Store.StatusOf(id), Is.EqualTo(EventStatus.Done));
        }
        Assert.That(app.Inbox.Consumed, Is.SupersetOf(ids));
        Assert.That(app.Mocks.Order.Where(r => r.Handler == "MockEcho").Select(r => r.Event), Is.EqualTo(ids), "In order.");
    }

    [Test]
    public async Task An_event_with_no_handler_is_done_and_acked_at_once() {
        await using var app = await TestApp.StartAsync();
        var id = app.Send("push", source: "github");

        await app.UntilAcked(id);

        Assert.That(app.Store.StatusOf(id), Is.EqualTo(EventStatus.Done));
    }

    [Test]
    public async Task A33_an_always_failing_handler_runs_5_times_then_the_event_is_failed_and_acked() {
        await using var app = await TestApp.StartAsync();
        var id = app.Send("fail");

        await app.UntilFinished(id, seconds: 30);
        await app.UntilAcked(id);

        Assert.That(app.Mocks.Runs("MockAlwaysFails"), Is.EqualTo(5));
        Assert.That(app.Store.StatusOf(id), Is.EqualTo(EventStatus.Failed));
        var result = Result(app, id, "MockAlwaysFails");
        Assert.That((result.Kind, result.Final, result.Attempt, result.Text), Is.EqualTo((ResultKind.Failed, true, 5, "failed: mock failure")));
    }

    [Test]
    public async Task A34_a_permanent_failure_is_final_at_once() {
        await using var app = await TestApp.StartAsync();
        var id = app.Send("permanent");

        await app.UntilFinished(id);

        Assert.That(app.Mocks.Runs("MockPermanent"), Is.EqualTo(1));
        Assert.That(app.Store.StatusOf(id), Is.EqualTo(EventStatus.Failed));
    }

    [Test]
    public async Task A35_a_timeout_is_a_failure_and_the_handler_runs_again_after_the_backoff() {
        await using var app = await TestApp.StartAsync();
        var id = app.Send("timeout");

        await TestApp.Until(() => app.Mocks.Runs("MockTimeout") >= 2, "a second run", seconds: 20);

        Assert.That(Result(app, id, "MockTimeout").Text, Is.EqualTo("failed: timeout"));
    }

    [Test]
    public async Task A36_a_duplicate_of_a_finished_event_runs_nothing_and_is_acked_again() {
        await using var app = await TestApp.StartAsync();
        var id = app.Send("echo");
        await app.UntilAcked(id);
        var acked = app.Store.Get(id)!.AckedAt;
        await Task.Delay(50);

        app.Inbox.SendAgain(id);

        await TestApp.Until(() => app.Store.Get(id)!.AckedAt > acked, "a second ack");
        Assert.That(app.Mocks.Runs("MockEcho"), Is.EqualTo(1));
    }

    [Test]
    public async Task A38_a_slow_handler_delays_only_its_own_lane() {
        await using var app = await TestApp.StartAsync();
        app.Mocks.SetDelay("MockSlow", TimeSpan.FromSeconds(1.5));
        var first = app.Send("pair");
        var second = app.Send("pair");

        await app.UntilFinished(second, seconds: 20);

        var order = app.Mocks.Order.Select(r => $"{r.Handler}:{r.Event}").ToList();
        Assert.That(order.IndexOf($"MockFast:{second}"), Is.LessThan(order.IndexOf($"MockSlow:{first}")), string.Join(", ", order));
        Assert.That(app.Store.StatusOf(first), Is.EqualTo(EventStatus.Done));
    }

    [Test]
    public async Task A28_exit_cancels_a_running_handler_and_the_event_runs_again_at_the_next_start() {
        var app = await TestApp.StartAsync();
        app.Mocks.SetDelay("MockSlow", TimeSpan.FromSeconds(30));
        var id = app.Send("slow");
        await TestApp.Until(() => app.Mocks.Starts("MockSlow") == 1, "the slow run started");

        await using var next = await app.RestartAsync();

        var stored = next.Store.Get(id)!;
        Assert.That(stored.Status, Is.EqualTo(EventStatus.Pending));
        await TestApp.Until(() => next.Store.StatusOf(id) == EventStatus.Done, "the run after the restart");
        Assert.That(next.Mocks.Runs("MockSlow"), Is.EqualTo(1));
    }

    [Test]
    public async Task A28_the_result_of_the_canceled_run_is_cancelled() {
        var app = await TestApp.StartAsync();
        app.Mocks.SetDelay("MockSlow", TimeSpan.FromSeconds(30));
        var id = app.Send("slow");
        await TestApp.Until(() => app.Mocks.Starts("MockSlow") == 1, "the slow run started");
        var store = app.Store;

        await app.App.StopAsync();

        var result = store.Get(id)!.Results.Single();
        Assert.That((result.Kind, result.Text, store.PendingOf(id, result.HandlerKey)?.Attempts), Is.EqualTo((ResultKind.Cancelled, "cancelled", 0)));
        await app.DisposeAsync();
    }

    [Test]
    public async Task A2_a_task_event_runs_the_task_to_in_review() {
        await using var app = await TestApp.StartAsync();
        var notion = (Notion.Infrastructure.MockNotion)app.Services.GetService(typeof(Notion.Infrastructure.MockNotion))!;
        var id = (await app.Bus.Send(new Listener.Slices.SendMockEvent.SendMockEventRequest("task"))).Id;

        await app.UntilFinished(id, seconds: 20);

        Assert.That(app.Store.StatusOf(id), Is.EqualTo(EventStatus.Done));
        Assert.That(notion.Status, Is.EqualTo("In Review"));
        Assert.That(notion.Result, Is.EqualTo(Claude.Infrastructure.MockClaude.Answer));
    }
}
