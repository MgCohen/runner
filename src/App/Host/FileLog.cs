namespace App.Host;

// The log of the app in <data folder>\logs, one file for each day: runner-2026-11-06.log. A shortcut start has no
// terminal, so the log goes to these files. Each start adds to the file of the day, so a restart after a crash keeps
// the log of the crash. At midnight the log goes to a new file. A file older than KeepDays is deleted.
public sealed class FileLogProvider : ILoggerProvider {
    public const int KeepDays = 14;

    readonly string dataFolder;
    readonly TimeProvider time;
    readonly object gate = new();
    StreamWriter? writer;
    DateOnly day;

    public FileLogProvider(string dataFolder, TimeProvider? time = null) {
        this.dataFolder = dataFolder;
        this.time = time ?? TimeProvider.System;
        Directory.CreateDirectory(Folder(dataFolder));
    }

    // The folder of the log files.
    public static string Folder(string dataFolder) => Path.Combine(dataFolder, "logs");

    // The log file of a day.
    public static string FileOf(string dataFolder, DateOnly day) =>
        Path.Combine(Folder(dataFolder), $"runner-{day:yyyy-MM-dd}.log");

    public ILogger CreateLogger(string categoryName) => new FileLog(this, categoryName);

    public void Dispose() {
        lock (gate) {
            writer?.Dispose();
            writer = null;
        }
    }

    void Write(DateTime now, string line) {
        lock (gate) {
            var today = DateOnly.FromDateTime(now);
            if (writer is null || today != day) {
                writer?.Dispose();
                day = today;
                writer = new StreamWriter(new FileStream(FileOf(dataFolder, today), FileMode.Append,
                    FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { AutoFlush = true };
                DeleteOld(today);
            }
            writer.WriteLine(line);
        }
    }

    // The date in the name tells the day of a file. A name with no date is not a log file of this provider.
    void DeleteOld(DateOnly today) {
        foreach (var path in Directory.EnumerateFiles(Folder(dataFolder), "runner-*.log")) {
            var name = Path.GetFileNameWithoutExtension(path)["runner-".Length..];
            if (DateOnly.TryParseExact(name, "yyyy-MM-dd", out var fileDay) && fileDay.DayNumber <= today.DayNumber - KeepDays) {
                try {
                    File.Delete(path);
                } catch (IOException) {
                    // Another process has the file open. The next day tries again.
                }
            }
        }
    }

    sealed class FileLog(FileLogProvider owner, string category) : ILogger {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
            var now = owner.time.GetLocalNow().DateTime;
            var line = $"{now:yyyy-MM-dd HH:mm:ss} {logLevel,-11} {category}: {formatter(state, exception)}";
            owner.Write(now, exception is null ? line : $"{line}{Environment.NewLine}{exception}");
        }
    }
}
