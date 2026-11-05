using System.Collections.Concurrent;

namespace MockHandlers.Infrastructure;

// The controls of the self-test for the mock handlers: a delay for each handler name, and the run count of each.
public sealed class MockControls {
    readonly ConcurrentDictionary<string, TimeSpan> delays = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, int> runs = new(StringComparer.Ordinal);
    readonly ConcurrentQueue<(string Handler, long Event)> order = new();

    public TimeSpan DelayOf(string handler, TimeSpan fallback) => delays.TryGetValue(handler, out var delay) ? delay : fallback;

    public void SetDelay(string handler, TimeSpan delay) => delays[handler] = delay;

    public void Ran(string handler, long eventId) {
        runs.AddOrUpdate(handler, 1, (_, n) => n + 1);
        order.Enqueue((handler, eventId));
    }

    public int Runs(string handler) => runs.GetValueOrDefault(handler);

    // The finished runs, in order: handler name and event ID.
    public IReadOnlyList<(string Handler, long Event)> Order => [.. order];
}
