using Settings.Slices.SetSetting;
using Traffic.Infrastructure;
using Traffic.Slices.GetTrafficEntry;
using Traffic.Slices.ListTraffic;

namespace App.Tests;

public sealed class TrafficTests {
    [Test]
    public void A27_secret_values_are_hidden_in_a_kept_body() =>
        Assert.That(TrafficLog.Redact("""{"token":"abc","name":"x","nested":{"apiKey":"k","list":[{"Password":"p"}]}}"""),
            Is.EqualTo("""{"token":"•••","name":"x","nested":{"apiKey":"•••","list":[{"Password":"•••"}]}}"""));

    [Test]
    public async Task A17_in_mock_mode_each_host_is_a_mock_host() {
        await using var app = await TestApp.StartAsync();
        var id = (await app.Bus.Send(new Listener.Slices.SendMockEvent.SendMockEventRequest("task"))).Id;
        await app.UntilAcked(id, seconds: 20);

        var log = (TrafficLog)app.Services.GetService(typeof(TrafficLog))!;

        Assert.That(log.Snapshot().Select(e => e.Entry.Host).Distinct(), Has.All.EndsWith(".mock.invalid"));
        Assert.That(log.Snapshot().Select(e => e.Entry.Kind), Does.Contain("PATCH").And.Contain("hello").And.Contain("event"));
    }

    [Test]
    public async Task A11_A12_errors_filter_and_bodies_are_not_kept_when_capture_is_off() {
        await using var app = await TestApp.StartAsync();
        await app.Bus.Send(new Listener.Slices.SetMockNetwork.SetMockNetworkRequest(false));
        await TestApp.Until(() => ((TrafficLog)app.Services.GetService(typeof(TrafficLog))!).Snapshot().Any(e => e.Entry.IsError), "an error entry");

        var errors = await app.Bus.Send(new ListTrafficRequest([], [], ["error"], null, null));
        var http = (await app.Bus.Send(new ListTrafficRequest([], ["GET"], [], null, null))).Entries.First();
        var entry = await app.Bus.Send(new GetTrafficEntryRequest(http.Seq));

        Assert.That(errors.Entries, Has.All.Matches<TrafficItem>(e => e.IsError));
        Assert.That(errors.Errors, Is.EqualTo(errors.Matching));
        Assert.That((entry.Body, entry.CaptureBodies), Is.EqualTo(((string?)null, false)));
    }

    [Test]
    public async Task A27_with_capture_on_a_kept_notion_body_has_its_token_hidden() {
        await using var app = await TestApp.StartAsync();
        await app.Bus.Send(new SetSettingRequest("captureBodies", "true"));
        var id = (await app.Bus.Send(new Listener.Slices.SendMockEvent.SendMockEventRequest("task"))).Id;
        await app.UntilAcked(id, seconds: 20);

        var get = (await app.Bus.Send(new ListTrafficRequest([], ["GET"], [], "notion", null))).Entries.First(e => e.Label.Contains("/v1/pages/", StringComparison.Ordinal) && !e.Label.EndsWith("/markdown", StringComparison.Ordinal));
        var entry = await app.Bus.Send(new GetTrafficEntryRequest(get.Seq));

        Assert.That(entry.Body, Does.Contain("\"token\":\"•••\"").And.Not.Contain("abc"));
    }
}
