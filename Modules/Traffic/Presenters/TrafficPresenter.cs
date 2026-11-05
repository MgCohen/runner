using Traffic.Slices.GetTrafficEntry;
using Traffic.Slices.ListTraffic;
using Traffic.Slices.RecordTraffic;
using VSA.Presenters;

namespace Traffic.Presenters;

// The Traffic page, its filter panel and the traffic detail page (C4).
public sealed class TrafficPresenter(IBus bus) : Presenter(bus), IPresenterEvent<TrafficRecorded> {
    public const string DirectionGroup = "dir";
    public const string KindGroup = "kind";
    public const string ResultGroup = "res";
    const int PageSize = 50;

    readonly Dictionary<string, HashSet<string>> filters = new() {
        [DirectionGroup] = new(StringComparer.Ordinal),
        [KindGroup] = new(StringComparer.Ordinal),
        [ResultGroup] = new(StringComparer.Ordinal),
    };
    int loaded = PageSize;

    public OpState Load { get; } = new();

    public TrafficPageView? Page { get; private set; }

    IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> facets = new Dictionary<string, IReadOnlyDictionary<string, int>>();

    public string Text { get; private set; } = "";

    public bool PanelOpen { get; private set; }

    public bool Visible { get; set; }

    public TrafficEntryView? Detail { get; private set; }

    public IReadOnlySet<string> Selected(string group) => filters[group];

    public int ActiveCount => filters.Values.Sum(f => f.Count);

    public int FacetCount(string group, string value) =>
        facets.TryGetValue(group, out var counts) && counts.TryGetValue(value, out var n) ? n : 0;

    public Task LoadAsync() {
        loaded = PageSize;
        return Reload();
    }

    public Task LoadOlderAsync() {
        loaded += PageSize;
        return Reload();
    }

    public Task ToggleAsync(string group, string value) {
        if (!filters[group].Remove(value)) {
            filters[group].Add(value);
        }
        return LoadAsync();
    }

    public Task RemoveAsync(string group, string value) {
        filters[group].Remove(value);
        return LoadAsync();
    }

    public Task ClearAsync() {
        foreach (var f in filters.Values) {
            f.Clear();
        }
        return LoadAsync();
    }

    public Task SearchAsync(string text) {
        Text = text ?? "";
        return LoadAsync();
    }

    public void SetPanel(bool open) => PanelOpen = open;

    public Task OpenAsync(long seq) => Run(Load, async () => Detail = Map(await Bus.Send(new GetTrafficEntryRequest(seq))));

    // A new entry reloads the list only while the Traffic page shows.
    public Task OnEventAsync(TrafficRecorded integrationEvent, CancellationToken ct) => Visible ? Reload() : Task.CompletedTask;

    Task Reload() => Run(Load, async () => {
        var page = await Bus.Send(new ListTrafficRequest([.. filters[DirectionGroup]], [.. filters[KindGroup]], [.. filters[ResultGroup]], Text, null, loaded));
        facets = page.Facets;
        Page = new TrafficPageView([.. page.Entries.Select(e => new TrafficRowView(e.Seq, e.Direction, e.Kind, e.Label, e.Status, e.Size, e.Ms, e.IsError))],
            page.Matching, page.Kept, page.Errors, page.KbIn, page.KbOut, page.HasMore, page.Kinds);
    });

    static TrafficEntryView Map(GetTrafficEntryResponse e) =>
        new(e.Seq, e.Direction, e.Kind, e.Host, e.Path, e.Status, e.Size, e.Ms, e.IsError, e.EventId, e.Body, e.CaptureBodies);
}
