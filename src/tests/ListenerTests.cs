using Listener.Slices.GetListenerState;
using Listener.Slices.SetListener;
using Listener.Slices.SetMockNetwork;
using Settings.Slices.SetSetting;

namespace App.Tests;

public sealed class ListenerTests {
    static async Task<GetListenerStateResponse> State(TestApp app) => await app.Bus.Send(new GetListenerStateRequest());

    static async Task UntilState(TestApp app, string state, string? reason = null) {
        GetListenerStateResponse? last = null;
        await TestApp.Until(() => {
            last = State(app).GetAwaiter().GetResult();
            return last.State == state && (reason is null || last.Reason == reason);
        }, $"state {state} {reason}, last {last?.State} {last?.Reason}");
    }

    [Test]
    public async Task A1_with_paused_the_listener_is_off_and_paused() {
        await using var app = await TestApp.StartAsync(paused: true);

        await UntilState(app, "Paused");

        Assert.That((await State(app)).On, Is.False);
    }

    [Test]
    public async Task Paused_at_start_does_not_change_the_saved_switch() {
        var app = await TestApp.StartAsync(paused: true);

        await using var next = await app.RestartAsync();

        await UntilState(next, "Connected");
    }

    [Test]
    public async Task A23_no_network_gives_an_error_then_the_listener_connects_again() {
        await using var app = await TestApp.StartAsync();
        await UntilState(app, "Connected");

        await app.Bus.Send(new SetMockNetworkRequest(false));
        await UntilState(app, "Error", "No network");
        await app.Bus.Send(new SetMockNetworkRequest(true));

        await UntilState(app, "Connected");
    }

    [Test]
    public async Task A19_A24_listener_on_at_start_off_starts_the_listener_off() {
        var app = await TestApp.StartAsync();
        await app.Bus.Send(new SetListenerRequest(true));
        await app.Bus.Send(new SetSettingRequest("listenerOnStart", "false"));

        await using var next = await app.RestartAsync();

        await UntilState(next, "Paused");
        Assert.That((await State(next)).On, Is.False);
    }
}
