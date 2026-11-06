using System.Globalization;
using Handling.Domain;

namespace Dispatch.Domain;

public static class LastText {
    // "#12480 page.created · 16:59", or "No events yet".
    public static string Of(EventRecord? last) =>
        last is null ? "No events yet" : $"#{last.Id} {last.TypeText} · {last.ReceivedAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)}";

    // "notion · page · properties_updated, created", or "notion · all".
    public static string ListensTo(HandlerInfo handler) {
        ArgumentNullException.ThrowIfNull(handler);
        var matches = handler.Matches.Where(m => m.Type is not null || m.Action is not null).ToList();
        if (matches.Count == 0) {
            return $"{string.Join(", ", handler.Sources)} · all";
        }
        var parts = matches.GroupBy(m => m.Type ?? "any").Select(g => $"{g.Key} · {string.Join(", ", g.Select(m => m.Action ?? "any"))}");
        return $"{string.Join(", ", handler.Sources)} · {string.Join(" · ", parts)}";
    }
}
