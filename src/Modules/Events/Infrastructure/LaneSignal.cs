using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Events.Infrastructure;

// Wakes the lane of a handler when it has a new item or a new retry time.
public sealed class LaneSignal {
    readonly ConcurrentDictionary<string, Channel<bool>> lanes = new(StringComparer.Ordinal);

    public void Wake(string handler) => Lane(handler).Writer.TryWrite(true);

    public void WakeAll() {
        foreach (var lane in lanes.Values) {
            lane.Writer.TryWrite(true);
        }
    }

    // Waits for a wake or for the timeout. A null timeout waits with no limit.
    public async Task WaitAsync(string handler, TimeSpan? timeout, CancellationToken ct) {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout is { } t) {
            limit.CancelAfter(t < TimeSpan.Zero ? TimeSpan.Zero : t);
        }
        try {
            await Lane(handler).Reader.ReadAsync(limit.Token);
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
        }
    }

    Channel<bool> Lane(string handler) =>
        lanes.GetOrAdd(handler, _ => Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite }));
}
