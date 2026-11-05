namespace App.Host;

// The log of the app in <data folder>\runner.log. A shortcut start has no terminal, so the log goes to this file.
// At each start, the last log moves to runner.old.log.
public sealed class FileLogProvider : ILoggerProvider {
    readonly StreamWriter writer;
    readonly object gate = new();

    public FileLogProvider(string folder) {
        var path = Path.Combine(folder, "runner.log");
        if (File.Exists(path)) {
            File.Move(path, Path.Combine(folder, "runner.old.log"), overwrite: true);
        }
        writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { AutoFlush = true };
    }

    public ILogger CreateLogger(string categoryName) => new FileLog(this, categoryName);

    public void Dispose() {
        lock (gate) {
            writer.Dispose();
        }
    }

    void Write(string line) {
        lock (gate) {
            writer.WriteLine(line);
        }
    }

    sealed class FileLog(FileLogProvider owner, string category) : ILogger {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {logLevel,-11} {category}: {formatter(state, exception)}";
            owner.Write(exception is null ? line : $"{line}{Environment.NewLine}{exception}");
        }
    }
}
