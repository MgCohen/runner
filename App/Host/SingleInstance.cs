using System.Runtime.InteropServices;

namespace App.Host;

// One instance only (C6): a second start brings the first window to the front and exits.
// Mock mode has its own instance, so a mock self-test can run next to the real app.
public sealed partial class SingleInstance : IDisposable {
    readonly Mutex mutex;
    readonly EventWaitHandle show;
    readonly RegisteredWaitHandle? wait;

    SingleInstance(Mutex mutex, EventWaitHandle show, Action onShow) {
        this.mutex = mutex;
        this.show = show;
        wait = ThreadPool.RegisterWaitForSingleObject(show, (_, _) => onShow(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    // Returns null if another instance runs; then that instance shows its window.
    public static SingleInstance? TryStart(bool mock, Action onShow) {
        var name = $"Runner.Tray.{Environment.UserName}{(mock ? ".mock" : "")}";
        var mutex = new Mutex(initiallyOwned: true, $"Local\\{name}", out var created);
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, $"Local\\{name}.Show");
        if (!created) {
            // Windows lets only the foreground process give the foreground to another process.
            _ = AllowSetForegroundWindow(AsfwAny);
            show.Set();
            mutex.Dispose();
            show.Dispose();
            return null;
        }
        return new SingleInstance(mutex, show, onShow);
    }

    const int AsfwAny = -1;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);

    public void Dispose() {
        wait?.Unregister(null);
        mutex.ReleaseMutex();
        mutex.Dispose();
        show.Dispose();
    }
}
