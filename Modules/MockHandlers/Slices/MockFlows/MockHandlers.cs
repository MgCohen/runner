using Handling.Domain;
using Handling.Slices.Handling;
using MockHandlers.Infrastructure;

namespace MockHandlers.Slices.MockFlows;

// A8: a test-only handler that shows under its source on Home, with no UI change.
sealed class MockEcho(HandlerContext context, MockControls controls) : InboxHandler(context) {
    public override HandlerConfig Config { get; } = new() { Sources = ["mock"], Matches = [new Match("echo")], Description = "Mock: done at once" };

    protected override Task Run(InboxEvent e, CancellationToken ct) {
        controls.Ran(nameof(MockEcho), e.Id);
        return Task.CompletedTask;
    }
}

// A33: always fails, so it runs 5 times with backoff.
sealed class MockAlwaysFails(HandlerContext context, MockControls controls) : InboxHandler(context) {
    public override HandlerConfig Config { get; } = new() { Sources = ["mock"], Matches = [new Match("fail")], Description = "Mock: always fails" };

    protected override Task Run(InboxEvent e, CancellationToken ct) {
        controls.Ran(nameof(MockAlwaysFails), e.Id);
        throw new InvalidOperationException("mock failure");
    }
}

// A34: a permanent failure, with no retry.
sealed class MockPermanent(HandlerContext context, MockControls controls) : InboxHandler(context) {
    public override HandlerConfig Config { get; } = new() { Sources = ["mock"], Matches = [new Match("permanent")], Description = "Mock: permanent failure" };

    protected override Task Run(InboxEvent e, CancellationToken ct) {
        controls.Ran(nameof(MockPermanent), e.Id);
        throw new PermanentFailureException("mock permanent failure");
    }
}

// A35: runs longer than its timeout of 1 s.
sealed class MockTimeout(HandlerContext context, MockControls controls) : InboxHandler(context) {
    public override HandlerConfig Config { get; } = new() {
        Sources = ["mock"],
        Matches = [new Match("timeout")],
        Description = "Mock: runs longer than its timeout",
        Timeout = TimeSpan.FromSeconds(1),
    };

    protected override async Task Run(InboxEvent e, CancellationToken ct) {
        controls.Ran(nameof(MockTimeout), e.Id);
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
    }
}

// A38: two handlers match one event; the slow one has a delay that the self-test sets.
sealed class MockFast(HandlerContext context, MockControls controls) : InboxHandler(context) {
    public override HandlerConfig Config { get; } = new() { Sources = ["mock"], Matches = [new Match("pair")], Description = "Mock: fast handler of a pair" };

    protected override Task Run(InboxEvent e, CancellationToken ct) {
        controls.Ran(nameof(MockFast), e.Id);
        return Task.CompletedTask;
    }
}

sealed class MockSlow(HandlerContext context, MockControls controls) : InboxHandler(context) {
    public override HandlerConfig Config { get; } = new() { Sources = ["mock"], Matches = [new Match("pair"), new Match("slow")], Description = "Mock: slow handler" };

    protected override async Task Run(InboxEvent e, CancellationToken ct) {
        controls.Started(nameof(MockSlow));
        await Task.Delay(controls.DelayOf(nameof(MockSlow), TimeSpan.FromMilliseconds(100)), ct);
        controls.Ran(nameof(MockSlow), e.Id);
    }
}
