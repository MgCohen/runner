using Dispatch.Slices.SetHandler;
using Dispatch.Slices.SetSource;
using Handling.Domain;

namespace App.Tests;

public sealed class SwitchTests {
    const string Echo = "MockHandlers.Slices.MockFlows.MockEcho";

    [Test]
    public async Task A4_a_handler_that_is_off_skips_the_event_and_the_event_is_done() {
        await using var app = await TestApp.StartAsync();
        await app.Bus.Send(new SetHandlerRequest(Echo, false));
        var id = app.Send("echo");

        await app.UntilAcked(id);

        Assert.That(app.Store.Get(id)!.Results.Single().Text, Is.EqualTo("skipped (off)"));
        Assert.That(app.Store.StatusOf(id), Is.EqualTo(EventStatus.Done));
        Assert.That(app.Mocks.Runs("MockEcho"), Is.Zero);
    }

    [Test]
    public async Task A5_a_source_that_is_off_skips_the_event_and_it_does_not_run_when_the_source_turns_on() {
        await using var app = await TestApp.StartAsync();
        await app.Bus.Send(new SetSourceRequest("mock", false));
        var id = app.Send("echo");
        await app.UntilAcked(id);

        await app.Bus.Send(new SetSourceRequest("mock", true));
        await Task.Delay(500);

        Assert.That(app.Store.Get(id)!.Results.Single().Text, Is.EqualTo("skipped (source off)"));
        Assert.That(app.Mocks.Runs("MockEcho"), Is.Zero);
    }

    [Test]
    public async Task A8_a_test_only_handler_shows_under_its_source() {
        await using var app = await TestApp.StartAsync();

        var sources = await app.Bus.Send(new Dispatch.Slices.ListSources.ListSourcesRequest());

        Assert.That(sources.Sources.Single(s => s.Name == "mock").Handlers.Select(h => h.Name), Does.Contain("MockEcho"));
    }

    [Test]
    public async Task The_switch_file_keeps_the_switches_after_a_restart() {
        var app = await TestApp.StartAsync();
        await app.Bus.Send(new SetHandlerRequest(Echo, false));

        await using var next = await app.RestartAsync();

        var sources = await next.Bus.Send(new Dispatch.Slices.ListSources.ListSourcesRequest());
        Assert.That(sources.Sources.Single(s => s.Name == "mock").Handlers.Single(h => h.Name == "MockEcho").On, Is.False);
    }
}
