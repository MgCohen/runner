using System.Text.Json.Nodes;
using Events.Slices.GetEvent;
using Settings.Slices.GetSettings;
using Settings.Slices.SetSetting;
using Traffic.Slices.ListTraffic;
using VSA.Presenters;

namespace App.Tests;

public sealed class SettingsTests {
    static string File(TestApp app) => Path.Combine(app.Folder, "settings.json");

    [Test]
    public async Task The_page_has_the_groups_in_order() {
        await using var app = await TestApp.StartAsync();

        var settings = await app.Bus.Send(new GetSettingsRequest());

        Assert.That(settings.Groups.Select(g => g.Name), Is.EqualTo(new[] { "At start", "Traffic", "Inbox", "Appearance", "About" }));
        Assert.That(settings.Groups[0].Rows.Select(r => r.Key), Is.EqualTo(new[] { "listenerOnStart", "openOnStart", "pinOnStart", "startWithWindows" }));
    }

    [Test]
    public async Task A21_a_theme_is_saved_in_the_settings_file() {
        await using var app = await TestApp.StartAsync();

        await app.Bus.Send(new SetSettingRequest("theme", "Dark"));

        Assert.That(JsonNode.Parse(System.IO.File.ReadAllText(File(app)))!["Modules"]!["Settings"]!["theme"]!.GetValue<string>(), Is.EqualTo("Dark"));
        var row = (await app.Bus.Send(new GetSettingsRequest())).Groups.SelectMany(g => g.Rows).Single(r => r.Key == "theme");
        Assert.That(row.Value, Is.EqualTo("Dark"));
    }

    [Test]
    public async Task A22_a_later_setting_does_not_change_and_the_file_does_not_change() {
        await using var app = await TestApp.StartAsync();

        var error = Assert.ThrowsAsync<RequestException>(() => app.Bus.Send(new SetSettingRequest("startWithWindows", "true")));

        Assert.That(error!.Code, Is.EqualTo("settings.read_only"));
        Assert.That(System.IO.File.Exists(File(app)), Is.False);
    }

    [Test]
    public async Task A20_entries_to_keep_sets_the_capacity_of_the_traffic_log() {
        await using var app = await TestApp.StartAsync();

        await app.Bus.Send(new SetSettingRequest("trafficKeep", "200"));

        var page = await app.Bus.Send(new ListTrafficRequest([], [], [], null, null));
        Assert.That(page.Kept, Is.EqualTo(200));
    }

    [Test]
    public async Task A32_a_smaller_store_days_deletes_the_older_events_at_once() {
        await using var app = await TestApp.StartAsync();
        var old = app.Inbox.Send("github", "push", null, "{}", TimeSpan.FromDays(3));
        var recent = app.Send("push", source: "github");
        await app.UntilAcked(old);
        await app.UntilAcked(recent);

        await app.Bus.Send(new SetSettingRequest("storeDays", "1"));

        Assert.That(app.Store.Get(old), Is.Null);
        Assert.That(app.Store.Get(recent), Is.Not.Null);
        Assert.ThrowsAsync<RequestException>(() => app.Bus.Send(new GetEventRequest(old)));
    }

    [Test]
    public async Task A_broken_settings_file_gives_the_defaults() {
        var folder = Path.Combine(Path.GetTempPath(), "runner-tests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        await System.IO.File.WriteAllTextAsync(Path.Combine(folder, "settings.json"), "{ not json");

        await using var app = await TestApp.StartAsync(folder: folder);

        var row = (await app.Bus.Send(new GetSettingsRequest())).Groups.SelectMany(g => g.Rows).Single(r => r.Key == "trafficKeep");
        Assert.That(row.Value, Is.EqualTo("500"));
        Assert.That(System.IO.File.Exists(Path.Combine(folder, "settings.json.bad")), Is.True);
    }
}
