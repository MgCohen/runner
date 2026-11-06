using System.Threading.Channels;

namespace Listener.Infrastructure;

// Wakes the ack service when an event is finished. Many calls before one wait give one wake.
public sealed class AckSignal {
    readonly Channel<bool> channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Set() => channel.Writer.TryWrite(true);

    // Waits for a wake or for the timeout. A null timeout waits with no limit.
    public async Task WaitAsync(TimeSpan? timeout, CancellationToken ct) {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout is { } t) {
            limit.CancelAfter(t < TimeSpan.Zero ? TimeSpan.Zero : t);
        }
        try {
            await channel.Reader.ReadAsync(limit.Token);
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
        }
    }
}
