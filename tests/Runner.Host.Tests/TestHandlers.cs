using System.Collections.Concurrent;
using Runner.Abstractions;

namespace Runner.Host.Tests;

/// <summary>Records each handler call. Each test host has its own instance.</summary>
public sealed class Recorder
{
    public ConcurrentQueue<(string Handler, long Id, int Attempt)> Calls { get; } = new();

    /// <summary>The <c>gate</c> handler waits for this task.</summary>
    public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Count(string handler, long id) => Calls.Count(c => c.Handler == handler && c.Id == id);

    public void Add(string handler, InboxEvent e, HandlerContext c) => Calls.Enqueue((handler, e.Id, c.Attempt));
}

[On("test", "ok")]
public sealed class OkHandler(Recorder recorder) : IEventHandler
{
    public Task HandleAsync(InboxEvent e, HandlerContext context)
    {
        recorder.Add("ok", e, context);
        return Task.CompletedTask;
    }
}

/// <summary>Fails in attempts 1 and 2. Succeeds in attempt 3.</summary>
[On("test", "flaky")]
public sealed class FlakyHandler(Recorder recorder) : IEventHandler
{
    public Task HandleAsync(InboxEvent e, HandlerContext context)
    {
        recorder.Add("flaky", e, context);
        return context.Attempt < 3 ? throw new InvalidOperationException("not yet") : Task.CompletedTask;
    }
}

[On("test", "broken")]
public sealed class BrokenHandler(Recorder recorder) : IEventHandler
{
    public Task HandleAsync(InboxEvent e, HandlerContext context)
    {
        recorder.Add("broken", e, context);
        throw new InvalidOperationException("always fails");
    }
}

[On("test", "permanent")]
public sealed class PermanentHandler(Recorder recorder) : IEventHandler
{
    public Task HandleAsync(InboxEvent e, HandlerContext context)
    {
        recorder.Add("permanent", e, context);
        throw new PermanentFailureException("bad payload");
    }
}

[On("test", "gate")]
public sealed class GateHandler(Recorder recorder) : IEventHandler
{
    public async Task HandleAsync(InboxEvent e, HandlerContext context)
    {
        recorder.Add("gate", e, context);
        await recorder.Gate.Task.WaitAsync(context.CancellationToken);
    }
}

/// <summary>A second handler for the events of <see cref="OkHandler"/> and <see cref="FlakyHandler"/>.</summary>
[On("test", "ok")]
[On("test", "flaky")]
public sealed class SecondHandler(Recorder recorder) : IEventHandler
{
    public Task HandleAsync(InboxEvent e, HandlerContext context)
    {
        recorder.Add("second", e, context);
        return Task.CompletedTask;
    }
}
