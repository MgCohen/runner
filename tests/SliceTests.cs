using AppSettings.Domain;
using Dispatch.Slices.Common;
using Dispatch.Slices.GetHandler;
using Dispatch.Slices.ListSources;
using Dispatch.Slices.SetHandler;
using Dispatch.Slices.SetSource;
using Events.Slices.GetEvent;
using Events.Slices.ListEvents;
using Handling.Domain;
using Handling.Infrastructure;
using Listener.Domain;
using Listener.Infrastructure;
using Listener.Slices.GetListenerState;
using Listener.Slices.SendMockEvent;
using Listener.Slices.SetListener;
using Listener.Slices.SetMockNetwork;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MockHandlers.Infrastructure;
using MockHandlers.Slices.SetMockDelay;
using Network.Infrastructure;
using Settings.Infrastructure;
using Settings.Slices.GetSettings;
using Settings.Slices.SetSetting;
using Traffic.Infrastructure;
using Traffic.Slices.GetTrafficEntry;
using Traffic.Slices.ListTraffic;
using VSA.Testing.Unit;

namespace App.Tests;

// One VsaUnitTest for each request slice: the handler runs with no HTTP, on a store in a temporary folder.
public abstract class SliceTest : VsaUnitTest {
    protected static readonly HandlerInfo Echo = new("Mock.Echo", "Echo", ["mock"], [new EventMatch("echo", null)], "Done at once", true, TimeSpan.FromSeconds(5));
    protected static readonly HandlerInfo Quiet = new("Mock.Quiet", "Quiet", ["mock", "github"], [new EventMatch(null, null)], "Off by default", false, TimeSpan.FromSeconds(5));

    protected string Folder { get; } = Path.Combine(Path.GetTempPath(), "runner-tests", Guid.NewGuid().ToString("N")[..8]);

    protected HandlingStore Store => Provider.GetRequiredService<HandlingStore>();

    protected override void Configure(IServiceCollection services) {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Runner:DataDir"] = Folder,
            ["Runner:Mock"] = "true",
        }).Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new HandlerCatalog(() => [Echo, Quiet]));
        services.AddSingleton<HandlingStore>();
        services.AddSingleton<HandlerSwitches>();
        services.AddSingleton<ListenerStatus>();
        services.AddSingleton<MockInbox>();
        services.AddSingleton<MockControls>();
        services.AddSingleton<TrafficLog>();
        services.AddSingleton<SettingsFile>();
        services.AddOptions<ListenerOptions>();
        services.AddOptions<TrafficOptions>().Configure(o => o.TrafficKeep = 200);
        services.AddSingleton(new SettingsPart("Traffic", [
            new SettingRow("trafficKeep", SettingGroups.Traffic, "Entries to keep", SettingKind.Choice, "500") { Options = ["200", "500", "1000"] },
            new SettingRow("startWithWindows", SettingGroups.AtStart, "Start with Windows", SettingKind.Switch, "false") { Later = true },
        ]));
    }

    protected EventRecord Add(long id, string source, string type, params string[] handlers) {
        var e = new EventRecord(id, source, $"d{id}", type, null, DateTimeOffset.UtcNow.AddSeconds(id), "{}");
        Store.TryAdd(e, handlers);
        return e;
    }

    [TearDown]
    public void DeleteFolder() {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(Folder)) {
            Directory.Delete(Folder, recursive: true);
        }
    }
}

public sealed class ListenerSliceTests : SliceTest {
    [Test]
    public async Task GetListenerState_gives_the_switch_and_the_state() {
        Provider.GetRequiredService<ListenerStatus>().Set(ListenerState.Error, ListenerErrors.NoNetwork);

        var state = (await Handle(new GetListenerStateRequest())).Value;

        Assert.That((state.On, state.State, state.Reason, state.Mock), Is.EqualTo((true, "Error", "No network", true)));
    }

    [Test]
    public async Task SetListener_saves_the_switch() {
        var result = (await Handle(new SetListenerRequest(false))).Value;

        Assert.That(result.On, Is.False);
        Assert.That(File.ReadAllText(Path.Combine(Folder, "switches.json")), Does.Contain("\"listener\": false"));
    }

    [Test]
    public async Task SendMockEvent_adds_a_custom_event_to_the_mock_inbox() {
        var id = (await Handle(new SendMockEventRequest("custom", "mock", "echo"))).Value.Id;

        Assert.That(id, Is.GreaterThan(0));
    }

    [Test]
    public async Task SendMockEvent_refuses_an_unknown_kind() =>
        Assert.That((await Handle(new SendMockEventRequest("nope"))).Error!.Code, Is.EqualTo("listener.mock_kind"));

    [Test]
    public async Task SetMockNetwork_cuts_the_mock_network() {
        await Handle(new SetMockNetworkRequest(false));

        Assert.That(Provider.GetRequiredService<MockInbox>().IsUp, Is.False);
    }
}

public sealed class EventsSliceTests : SliceTest {
    [Test]
    public async Task ListEvents_pages_newest_first_with_the_handler_names() {
        Add(1, "mock", "echo", Echo.Key);
        Add(2, "github", "push");
        Add(3, "mock", "echo", Echo.Key);

        var page = (await Handle(new ListEventsRequest([], [], [], null, null, 2))).Value;
        var older = (await Handle(new ListEventsRequest([], [], [], null, page.Events[^1].Id, 2))).Value;

        Assert.That(page.Events.Select(e => (e.Id, e.Handlers)), Is.EqualTo(new[] { (3L, "Echo"), (2L, "no handler") }));
        Assert.That((page.HasMore, older.HasMore, older.Events.Single().Id), Is.EqualTo((true, false, 1L)));
        Assert.That(page.Facets["state"], Is.EquivalentTo(new Dictionary<string, int> { ["new"] = 2, ["done"] = 1 }));
    }

    [Test]
    public async Task ListEvents_filters_by_handler() {
        Add(1, "mock", "echo", Echo.Key);
        Add(2, "github", "push", Quiet.Key);

        var page = (await Handle(new ListEventsRequest([], [], [Quiet.Key], null, null))).Value;

        Assert.That(page.Events.Select(e => e.Id), Is.EqualTo(new[] { 2L }));
        Assert.That(page.Total, Is.EqualTo(2));
    }

    [Test]
    public async Task GetEvent_shows_waiting_for_a_handler_with_no_result() {
        Add(7, "mock", "echo", Echo.Key);

        var e = (await Handle(new GetEventRequest(7))).Value;

        Assert.That((e.State, e.Results.Single().Handler, e.Results.Single().Result), Is.EqualTo(("new", "Echo", "waiting")));
    }

    [Test]
    public async Task GetEvent_of_an_unknown_id_is_not_found() =>
        Assert.That((await Handle(new GetEventRequest(99))).Error!.Code, Is.EqualTo("events.not_found"));
}

public sealed class DispatchSliceTests : SliceTest {
    [Test]
    public async Task ListSources_shows_each_handler_under_each_of_its_sources() {
        Store.SaveSources(["github", "notion"]);

        var sources = (await Handle(new ListSourcesRequest())).Value.Sources;

        Assert.That(sources.Select(s => s.Name), Is.EqualTo(new[] { "github", "notion", "mock" }));
        Assert.That(sources.Single(s => s.Name == "notion").Handlers, Is.Empty, "A source with no handlers shows.");
        Assert.That(sources.Single(s => s.Name == "github").Handlers.Single().On, Is.False, "DefaultOn = false.");
        Assert.That(sources.Single(s => s.Name == "mock").Handlers.Select(h => h.Name), Is.EqualTo(new[] { "Echo", "Quiet" }));
    }

    [Test]
    public async Task GetHandler_gives_the_description_and_the_listens_to_text() {
        var handler = (await Handle(new GetHandlerRequest(Echo.Key))).Value;

        Assert.That((handler.Description, handler.ListensTo, handler.Last), Is.EqualTo(("Done at once", "mock · echo · any", "No events yet")));
    }

    [Test]
    public async Task SetSource_saves_the_switch_and_publishes_SwitchChanged() {
        await Handle(new SetSourceRequest("mock", false));

        Assert.That(Provider.GetRequiredService<HandlerSwitches>().IsSourceOn("mock"), Is.False);
        Assert.That(Events.OfType<SwitchChanged>().Single(), Is.EqualTo(new SwitchChanged("source", "mock", false)).Using<SwitchChanged>((a, b) => a.Kind == b.Kind && a.Name == b.Name && a.On == b.On));
    }

    [Test]
    public async Task SetHandler_of_an_unknown_key_is_not_found() =>
        Assert.That((await Handle(new SetHandlerRequest("Nope", true))).Error!.Code, Is.EqualTo("dispatch.not_found"));
}

public sealed class SettingsSliceTests : SliceTest {
    [Test]
    public async Task GetSettings_gives_the_default_of_a_value_that_is_not_set() {
        var groups = (await Handle(new GetSettingsRequest())).Value.Groups;

        Assert.That(groups.Select(g => g.Name), Is.EqualTo(new[] { "At start", "Traffic" }));
        Assert.That(groups[1].Rows.Single().Value, Is.EqualTo("500"));
    }

    [Test]
    public async Task SetSetting_saves_the_value_and_publishes_SettingChanged() {
        var result = (await Handle(new SetSettingRequest("trafficKeep", "1000"))).Value;

        Assert.That(result.Value, Is.EqualTo("1000"));
        Assert.That(File.ReadAllText(Path.Combine(Folder, "settings.json")), Does.Contain("\"trafficKeep\": 1000"));
        Assert.That(Events.OfType<SettingChanged>().Single().Key, Is.EqualTo("trafficKeep"));
    }

    [TestCase("trafficKeep", "7", "settings.value")]
    [TestCase("startWithWindows", "true", "settings.read_only")]
    [TestCase("nope", "1", "settings.not_found")]
    public async Task SetSetting_refuses_a_value_that_is_not_valid(string key, string value, string code) =>
        Assert.That((await Handle(new SetSettingRequest(key, value))).Error!.Code, Is.EqualTo(code));
}

public sealed class TrafficSliceTests : SliceTest {
    TrafficLog Log => Provider.GetRequiredService<TrafficLog>();

    [Test]
    public async Task ListTraffic_counts_the_errors_and_pages_by_seq() {
        for (var i = 0; i < 3; i++) {
            Log.Record(new TrafficEntry(DateTimeOffset.UtcNow, "out", "GET", "api.mock.invalid", "/x", i == 1 ? "500" : "200", 1024, TimeSpan.FromMilliseconds(5)));
        }

        var page = (await Handle(new ListTrafficRequest([], [], [], null, null, 2))).Value;

        Assert.That((page.Matching, page.Errors, page.Kept, page.HasMore, page.Entries.Count), Is.EqualTo((3, 1, 200, true, 2)));
        Assert.That(page.Entries[0].Size, Is.EqualTo("1.0 KB"));
    }

    [Test]
    public async Task GetTrafficEntry_has_no_body_when_capture_is_off() {
        Log.Record(new TrafficEntry(DateTimeOffset.UtcNow, "out", "GET", "api.mock.invalid", "/x", "200", 10, null) { Body = "{}" });

        var entry = (await Handle(new GetTrafficEntryRequest(1))).Value;

        Assert.That((entry.Body, entry.CaptureBodies), Is.EqualTo(((string?)null, false)));
    }
}

public sealed class MockSliceTests : SliceTest {
    [Test]
    public async Task SetMockDelay_sets_the_delay_of_a_mock_handler() {
        await Handle(new SetMockDelayRequest("MockSlow", 1500));

        Assert.That(Provider.GetRequiredService<MockControls>().DelayOf("MockSlow", TimeSpan.Zero), Is.EqualTo(TimeSpan.FromMilliseconds(1500)));
    }
}
