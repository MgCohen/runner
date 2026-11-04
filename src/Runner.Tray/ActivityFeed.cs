using Microsoft.Extensions.Logging;
using Runner.Abstractions;

namespace Runner.Tray;

/// <summary>
/// The lines of the Activity tab: the activity sink lines (for example <c>claude -p</c> output) and the log lines.
/// It keeps the last <see cref="Capacity"/> lines. The sink lines also go to the console.
/// </summary>
internal sealed class ActivityFeed : IActivitySink
{
    public const int Capacity = 1_000;

    private readonly Lock _lock = new();
    private readonly Queue<string> _lines = new();

    /// <summary>Occurs after a line is added. It can occur on any thread.</summary>
    public event Action<string>? LineAdded;

    /// <summary>Writes a sink line to the feed and to the console.</summary>
    public void Write(string line)
    {
        Console.WriteLine(line);
        Add(line);
    }

    /// <summary>Adds a line to the feed only.</summary>
    public void Add(string line)
    {
        lock (_lock)
        {
            _lines.Enqueue(line);
            while (_lines.Count > Capacity) _lines.Dequeue();
        }
        LineAdded?.Invoke(line);
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_lock) return _lines.ToList();
    }
}

/// <summary>Sends each <see cref="ILogger"/> line to the <see cref="ActivityFeed"/>. The console logger stays.</summary>
internal sealed class ActivityLoggerProvider(ActivityFeed feed) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new FeedLogger(feed, ShortName(categoryName));

    public void Dispose()
    {
    }

    private static string ShortName(string category)
    {
        var dot = category.LastIndexOf('.');
        return dot < 0 ? category : category[(dot + 1)..];
    }

    private sealed class FeedLogger(ActivityFeed feed, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var level = logLevel switch
            {
                LogLevel.Information => "info",
                LogLevel.Warning => "warn",
                LogLevel.Error => "fail",
                LogLevel.Critical => "crit",
                _ => logLevel.ToString().ToLowerInvariant(),
            };
            var text = $"{DateTime.Now:HH:mm:ss} {level} {category}: {formatter(state, exception)}";
            if (exception is not null) text += $" ({exception.GetType().Name}: {exception.Message})";
            feed.Add(text);
        }
    }
}
