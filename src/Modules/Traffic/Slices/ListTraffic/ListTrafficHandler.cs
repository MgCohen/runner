using System.Globalization;
using Traffic.Infrastructure;
using VSA.Domain;
using VSA.Handlers.Requests;

namespace Traffic.Slices.ListTraffic;

// The same search and filter rule as the Inbox: text matches kind, target and host; OR in a group, AND between groups.
sealed class ListTrafficHandler(TrafficLog log) : RoutedHandler<ListTrafficRequest, ListTrafficResponse> {
    protected override Task<Result<ListTrafficResponse>> HandleRequestAsync(ListTrafficRequest request, CancellationToken ct) {
        var all = log.Snapshot();
        bool Match(LoggedEntry e, string? skip) =>
            (skip == "dir" || request.Directions is not { Count: > 0 } d || d.Contains(e.Entry.Direction))
            && (skip == "kind" || request.Kinds is not { Count: > 0 } k || k.Contains(e.Entry.Kind))
            && (skip == "res" || request.Results is not { Count: > 0 } r || r.Contains(Result(e)))
            && (string.IsNullOrWhiteSpace(request.Text) || $"{e.Entry.Kind} {Label(e)} {e.Entry.Host}".Contains(request.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        var matching = all.Where(e => Match(e, null)).ToList();
        var page = matching.Where(e => request.Before is not { } before || e.Seq < before).Take(Math.Clamp(request.Limit, 1, 1000)).ToList();
        IReadOnlyDictionary<string, int> Facet(string group, Func<LoggedEntry, string> value) =>
            all.Where(e => Match(e, group)).GroupBy(value).ToDictionary(g => g.Key, g => g.Count());
        var facets = new Dictionary<string, IReadOnlyDictionary<string, int>> {
            ["dir"] = Facet("dir", e => e.Entry.Direction),
            ["kind"] = Facet("kind", e => e.Entry.Kind),
            ["res"] = Facet("res", Result),
        };
        var kinds = all.Select(e => e.Entry.Kind).Distinct(StringComparer.Ordinal).OrderBy(k => k == "event" ? 0 : 1).ThenBy(k => k, StringComparer.Ordinal).ToList();
        var response = new ListTrafficResponse([.. page.Select(Item)], matching.Count, log.Capacity, matching.Count(e => e.Entry.IsError),
            Kb(matching.Where(e => e.Entry.Direction == "in")), Kb(matching.Where(e => e.Entry.Direction == "out")),
            matching.Count > 0 && page.Count > 0 && matching[^1].Seq < page[^1].Seq, kinds, facets);
        return Task.FromResult<Result<ListTrafficResponse>>(response);
    }

    static string Result(LoggedEntry e) => e.Entry.IsError ? "error" : "ok";

    static string Label(LoggedEntry e) => e.Entry.EventId is { } id ? $"event {id}" : $"{e.Entry.Host} {e.Entry.Path}";

    static double Kb(IEnumerable<LoggedEntry> entries) => entries.Sum(e => e.Entry.Size ?? 0) / 1024.0;

    static TrafficItem Item(LoggedEntry e) {
        var x = e.Entry;
        var size = x.Size is not { } bytes ? "—" : bytes < 1024 ? $"{bytes} B" : (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        var ms = x.Duration is { } d ? Math.Round(d.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) : "—";
        return new TrafficItem(e.Seq, x.Time, x.Direction, x.Kind, Label(e), x.Status, size, ms, x.IsError, x.EventId);
    }
}
