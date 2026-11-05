using Dispatch.Slices.GetHandler;
using Dispatch.Slices.ListSources;
using Dispatch.Slices.SetHandler;
using Dispatch.Slices.SetSource;
using Dispatch.Slices.Common;
using VSA.Presenters;

namespace Dispatch.Presenters;

// Home and the handler detail page (C2). The fold state stays until the app exits.
public sealed class SourcesPresenter(IBus bus) : Presenter(bus),
    IPresenterEvent<SwitchChanged>, IPresenterEvent<SourcesChanged> {
    readonly HashSet<string> open = new(StringComparer.OrdinalIgnoreCase);
    bool firstLoad = true;

    public OpState Load { get; } = new();

    public IReadOnlyList<SourceView> Sources { get; private set; } = [];

    public HandlerView? Handler { get; private set; }

    public string Query { get; private set; } = "";

    // The sources and handlers that match the search, as a substring of the source or handler name, case-insensitive.
    public IEnumerable<(SourceView Source, IReadOnlyList<HandlerRowView> Handlers)> Visible {
        get {
            var q = Query.Trim();
            foreach (var source in Sources) {
                if (q.Length == 0) {
                    yield return (source, source.Handlers);
                    continue;
                }
                var sourceMatch = source.Name.Contains(q, StringComparison.OrdinalIgnoreCase);
                var handlers = source.Handlers.Where(h => sourceMatch || h.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
                if (sourceMatch || handlers.Count > 0) {
                    yield return (source, handlers);
                }
            }
        }
    }

    // A search shows the matching sources unfolded.
    public bool IsOpen(string source) => Query.Trim().Length > 0 || open.Contains(source);

    public void ToggleFold(string source) {
        if (!open.Remove(source)) {
            open.Add(source);
        }
    }

    public void Search(string query) => Query = query ?? "";

    public Task LoadAsync() => Run(Load, async () => {
        Sources = Map(await Bus.Send(new ListSourcesRequest()));
        if (firstLoad && Sources.Count > 0) {
            // Sources that have handlers are unfolded at the first open.
            open.UnionWith(Sources.Where(s => s.Handlers.Count > 0).Select(s => s.Name));
            firstLoad = false;
        }
    });

    public Task OpenHandlerAsync(string key) => Run(Load, async () => Handler = Map(await Bus.Send(new GetHandlerRequest(key))));

    public Task SetSourceAsync(string name, bool on) => Run(Load, async () => {
        await Bus.Send(new SetSourceRequest(name, on));
        Sources = Map(await Bus.Send(new ListSourcesRequest()));
    });

    public Task SetHandlerAsync(string key, bool on) => Run(Load, async () => {
        await Bus.Send(new SetHandlerRequest(key, on));
        Sources = Map(await Bus.Send(new ListSourcesRequest()));
        if (Handler?.Key == key) {
            Handler = Map(await Bus.Send(new GetHandlerRequest(key)));
        }
    });

    public Task OnEventAsync(SwitchChanged integrationEvent, CancellationToken ct) => Refresh();

    public Task OnEventAsync(SourcesChanged integrationEvent, CancellationToken ct) => Refresh();

    Task Refresh() => Run(Load, async () => {
        Sources = Map(await Bus.Send(new ListSourcesRequest()));
        if (Handler is { } handler) {
            Handler = Map(await Bus.Send(new GetHandlerRequest(handler.Key)));
        }
    });

    static List<SourceView> Map(ListSourcesResponse response) =>
        [.. response.Sources.Select(s => new SourceView(s.Name, s.On, s.Events, [.. s.Handlers.Select(h => new HandlerRowView(h.Key, h.Name, h.On, h.Last))]))];

    static HandlerView Map(GetHandlerResponse h) =>
        new(h.Key, h.Name, h.On, h.Description, string.Join(", ", h.Sources), h.ListensTo, h.Last, h.Events, h.Failed);
}
