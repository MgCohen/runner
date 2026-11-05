using Events.Slices.Common;
using Events.Slices.GetEvent;
using Events.Slices.ListEvents;
using Events.Slices.StoreEvent;
using Handling.Slices.Handling;
using VSA.Presenters;

namespace Events.Presenters;

// The Inbox page, its filter panel and the event detail page (C3).
// A live event goes at the top only when it matches the active filters; the loaded older pages do not change.
public sealed class InboxPresenter(IBus bus) : Presenter(bus),
    IPresenterEvent<EventStored>, IPresenterEvent<EventFinished>, IPresenterEvent<HandlerResultRecorded> {
    public const string StateGroup = "state";
    public const string SourceGroup = "source";
    public const string HandlerGroup = "handler";
    const int PageSize = 50;

    readonly Dictionary<string, HashSet<string>> filters = new() {
        [StateGroup] = new(StringComparer.Ordinal),
        [SourceGroup] = new(StringComparer.Ordinal),
        [HandlerGroup] = new(StringComparer.Ordinal),
    };
    List<EventRowView> events = [];

    public OpState Load { get; } = new();

    public IReadOnlyList<EventRowView> Events => events;

    public int Matching { get; private set; }

    public int Total { get; private set; }

    public bool HasMore { get; private set; }

    public bool Loaded { get; private set; }

    public string Text { get; private set; } = "";

    public bool PanelOpen { get; private set; }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Facets { get; private set; } = new Dictionary<string, IReadOnlyDictionary<string, int>>();

    public IReadOnlyList<string> SourceOptions { get; private set; } = [];

    public IReadOnlyList<HandlerOptionView> HandlerOptions { get; private set; } = [];

    public EventView? Detail { get; private set; }

    public IReadOnlySet<string> Selected(string group) => filters[group];

    public int ActiveCount => filters.Values.Sum(f => f.Count);

    public int FacetCount(string group, string value) =>
        Facets.TryGetValue(group, out var counts) && counts.TryGetValue(value, out var n) ? n : 0;

    // The handlers of the filter panel, grouped by source. With sources selected, only their handlers.
    public IEnumerable<(string Source, IReadOnlyList<HandlerOptionView> Handlers)> HandlerGroups =>
        (filters[SourceGroup].Count > 0 ? SourceOptions.Where(filters[SourceGroup].Contains) : SourceOptions)
            .Select(s => (s, (IReadOnlyList<HandlerOptionView>)[.. HandlerOptions.Where(h => h.Sources.Contains(s, StringComparer.OrdinalIgnoreCase))]))
            .Where(g => g.Item2.Count > 0);

    public string HandlerName(string key) => HandlerOptions.FirstOrDefault(h => h.Key == key)?.Name ?? key.Split('.')[^1];

    public Task LoadAsync() => Run(Load, async () => {
        var page = await Bus.Send(Query(null, PageSize));
        Apply(page);
        HasMore = page.HasMore;
        events = [.. page.Events.Select(Map)];
        Loaded = true;
    });

    public Task LoadOlderAsync() => Run(Load, async () => {
        if (events.Count == 0) {
            return;
        }
        var page = await Bus.Send(Query(events[^1].Id, PageSize));
        Apply(page);
        HasMore = page.HasMore;
        events.AddRange(page.Events.Select(Map).Where(e => events.All(x => x.Id != e.Id)));
    });

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

    // "Open in inbox" of a handler page: the filter is only that handler (A7).
    public Task ShowHandlerAsync(string key) {
        foreach (var f in filters.Values) {
            f.Clear();
        }
        filters[HandlerGroup].Add(key);
        Text = "";
        PanelOpen = false;
        return LoadAsync();
    }

    public Task OpenAsync(long id) => Run(Load, async () => Detail = Map(await Bus.Send(new GetEventRequest(id))));

    public Task OnEventAsync(EventStored integrationEvent, CancellationToken ct) => RefreshAsync(null);

    public Task OnEventAsync(EventFinished integrationEvent, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RefreshAsync(integrationEvent.InboxId);
    }

    public Task OnEventAsync(HandlerResultRecorded integrationEvent, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RefreshAsync(integrationEvent.InboxId);
    }

    // Updates the loaded rows in place and puts new matching events at the top.
    Task RefreshAsync(long? changed) => Run(Load, async () => {
        if (!Loaded) {
            return;
        }
        var page = await Bus.Send(Query(null, PageSize));
        Apply(page);
        var rows = page.Events.Select(Map).ToList();
        var fresh = rows.ToDictionary(e => e.Id);
        events = [.. events.Select(e => fresh.GetValueOrDefault(e.Id) ?? e)];
        var newest = events.Count == 0 ? (DateTimeOffset?)null : events[0].ReceivedAt;
        var added = rows.Where(e => events.All(x => x.Id != e.Id) && (newest is null || e.ReceivedAt >= newest)).ToList();
        events.InsertRange(0, added);
        if (changed is { } id && Detail?.Id == id) {
            Detail = Map(await Bus.Send(new GetEventRequest(id)));
        }
    });

    ListEventsRequest Query(long? before, int limit) =>
        new([.. filters[StateGroup]], [.. filters[SourceGroup]], [.. filters[HandlerGroup]], Text, before, limit);

    // The counts and the filter values. HasMore changes only with the pages that the user loads.
    void Apply(ListEventsResponse page) {
        Matching = page.Matching;
        Total = page.Total;
        Facets = page.Facets;
        SourceOptions = page.Sources;
        HandlerOptions = [.. page.Handlers.Select(h => new HandlerOptionView(h.Key, h.Name, h.Sources))];
    }

    static EventRowView Map(EventItem e) => new(e.Id, e.Source, e.Type, e.State, e.ReceivedAt, e.Handlers);

    static EventView Map(GetEventResponse e) =>
        new(e.Id, e.Source, e.Type, e.State, e.ReceivedAt, e.Attempts, e.AckedAt, [.. e.Results.Select(r => (r.Handler, r.Result))], e.Payload);
}
