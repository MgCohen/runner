using App.Host;
using Microsoft.Extensions.Logging;

namespace App.Tests;

// The log files: one file for each day, a restart adds to the file of the day, old files are deleted.
public sealed class FileLogTests {
    string folder = "";

    [SetUp]
    public void CreateFolder() {
        folder = Path.Combine(Path.GetTempPath(), "runner-tests", Guid.NewGuid().ToString("N")[..8]);
    }

    [TearDown]
    public void DeleteFolder() {
        if (Directory.Exists(folder)) {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Test]
    public void A_restart_on_the_same_day_adds_to_the_file_of_the_day() {
        var clock = new Clock(new DateTime(2026, 11, 6, 9, 0, 0));
        Write(clock, "first start");
        Write(clock, "second start");

        var lines = File.ReadAllLines(FileLogProvider.FileOf(folder, new DateOnly(2026, 11, 6)));

        Assert.That(lines, Has.Length.EqualTo(2));
        Assert.That(lines[0], Does.EndWith("first start"));
        Assert.That(lines[1], Does.EndWith("second start"));
    }

    [Test]
    public void At_midnight_the_log_goes_to_a_new_file() {
        var clock = new Clock(new DateTime(2026, 11, 6, 23, 59, 0));
        using (var provider = new FileLogProvider(folder, clock)) {
            var log = provider.CreateLogger("Test");
            log.LogInformation("before midnight");
            clock.Now = clock.Now.AddMinutes(2);
            log.LogInformation("after midnight");
        }

        Assert.That(File.ReadAllText(FileLogProvider.FileOf(folder, new DateOnly(2026, 11, 6))), Does.Contain("before midnight"));
        Assert.That(File.ReadAllText(FileLogProvider.FileOf(folder, new DateOnly(2026, 11, 7))), Does.Contain("after midnight"));
    }

    [Test]
    public void A_file_older_than_the_keep_days_is_deleted() {
        var today = new DateOnly(2026, 11, 20);
        var old = FileLogProvider.FileOf(folder, today.AddDays(-FileLogProvider.KeepDays));
        var kept = FileLogProvider.FileOf(folder, today.AddDays(-FileLogProvider.KeepDays + 1));
        var other = Path.Combine(FileLogProvider.Folder(folder), "runner-notes.log");
        Directory.CreateDirectory(FileLogProvider.Folder(folder));
        File.WriteAllText(old, "old");
        File.WriteAllText(kept, "kept");
        File.WriteAllText(other, "not a day file");

        Write(new Clock(today.ToDateTime(new TimeOnly(8, 0))), "start");

        Assert.That(File.Exists(old), Is.False);
        Assert.That(File.Exists(kept), Is.True);
        Assert.That(File.Exists(other), Is.True);
    }

    void Write(TimeProvider clock, string message) {
        using var provider = new FileLogProvider(folder, clock);
        provider.CreateLogger("Test").LogInformation("{Message}", message);
    }

    // A clock that the test sets. The local time zone is UTC, so local time is the time that the test gives.
    sealed class Clock(DateTime now) : TimeProvider {
        public DateTime Now { get; set; } = now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Now, DateTimeKind.Utc));
    }
}
