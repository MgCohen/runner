using System.Threading.Channels;

namespace Runner.Host;

/// <summary>A wake signal. Many calls to <see cref="Set"/> before one wait give one wake.</summary>
public class Signal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Set() => _channel.Writer.TryWrite(true);

    /// <summary>Waits for a signal or for the timeout. A null timeout waits without a limit.</summary>
    public async Task WaitAsync(TimeSpan? timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout is { } t) cts.CancelAfter(t < TimeSpan.Zero ? TimeSpan.Zero : t);
        try
        {
            await _channel.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The timeout is reached.
        }
    }
}

/// <summary>Wakes the dispatcher when an event is stored.</summary>
public sealed class DispatchSignal : Signal;

/// <summary>Wakes the ack service when an event is finished.</summary>
public sealed class AckSignal : Signal;

internal static class Backoff
{
    /// <summary>Returns base * 2^failures, limited to the maximum. Jitter takes a random value from half to all of it.</summary>
    public static TimeSpan Delay(TimeSpan baseDelay, TimeSpan maxDelay, int failures, bool jitter = true)
    {
        var exponent = Math.Min(Math.Max(failures, 0), 30);
        var ms = Math.Min(baseDelay.TotalMilliseconds * Math.Pow(2, exponent), maxDelay.TotalMilliseconds);
        if (jitter) ms *= 0.5 + Random.Shared.NextDouble() * 0.5;
        return TimeSpan.FromMilliseconds(ms);
    }
}
