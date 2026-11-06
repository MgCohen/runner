using Handling.Domain;

namespace Handling.Infrastructure;

// The handlers of the app. It reads the handlers one time, on first use.
public sealed class HandlerCatalog(Func<IReadOnlyList<HandlerInfo>> load) {
    readonly Lazy<IReadOnlyList<HandlerInfo>> all = new(() => [.. load().OrderBy(h => h.Name, StringComparer.Ordinal)]);

    public IReadOnlyList<HandlerInfo> All => all.Value;

    public HandlerInfo? Find(string key) => All.FirstOrDefault(h => h.Key == key);

    public IReadOnlyList<HandlerInfo> Match(EventRecord e) => [.. All.Where(h => h.Accepts(e))];
}
