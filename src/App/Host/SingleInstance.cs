using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace App.Host;

// One instance only (C6): a second start brings the first window to the front and exits. Real and mock mode share
// the instance, as the mode is a setting of the one app. The self-test has its own instance, so it can run next to
// the app. The app instance also answers commands (CommandClient) on a named pipe for the current user only.
public sealed partial class SingleInstance : IDisposable {
    readonly Mutex mutex;
    readonly EventWaitHandle show;
    readonly RegisteredWaitHandle? wait;
    readonly CancellationTokenSource stop = new();

    SingleInstance(Mutex mutex, EventWaitHandle show, Action onShow, Func<string, string>? onCommand, string name) {
        this.mutex = mutex;
        this.show = show;
        wait = ThreadPool.RegisterWaitForSingleObject(show, (_, _) => onShow(), null, Timeout.Infinite, executeOnlyOnce: false);
        if (onCommand is not null) {
            _ = Task.Run(() => ServeAsync(PipeName(name), onCommand, stop.Token));
        }
    }

    public static string Name(bool selfTest) => $"Runner.Tray.{Environment.UserName}{(selfTest ? ".selftest" : "")}";

    public static string PipeName(string name) => $"{name}.cmd";

    // Returns null if another instance runs; then that instance shows its window. After a mode switch (--restart),
    // the new instance waits for the old one to exit.
    public static SingleInstance? TryStart(bool selfTest, bool waitForPrevious, Action onShow, Func<string, string>? onCommand) {
        var name = Name(selfTest);
        var mutex = new Mutex(initiallyOwned: true, $"Local\\{name}", out var created);
        if (!created && waitForPrevious) {
            try {
                created = mutex.WaitOne(TimeSpan.FromSeconds(20));
            } catch (AbandonedMutexException) {
                created = true;
            }
        }
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, $"Local\\{name}.Show");
        if (!created) {
            // Windows lets only the foreground process give the foreground to another process.
            _ = AllowSetForegroundWindow(AsfwAny);
            show.Set();
            mutex.Dispose();
            show.Dispose();
            return null;
        }
        return new SingleInstance(mutex, show, onShow, selfTest ? null : onCommand, name);
    }

    // One line in, one line out, one client at a time.
    static async Task ServeAsync(string pipeName, Func<string, string> onCommand, CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            try {
                await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var command = await reader.ReadLineAsync(ct) ?? "";
                string reply;
                try {
                    reply = onCommand(command.Trim());
                } catch (Exception e) {
                    reply = $"error: {e.Message}";
                }
                await writer.WriteLineAsync(reply.AsMemory(), ct);
            } catch (OperationCanceledException) {
                return;
            } catch (IOException) {
                // The client left before the reply. Wait for the next client.
            }
        }
    }

    const int AsfwAny = -1;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);

    public void Dispose() {
        stop.Cancel();
        wait?.Unregister(null);
        mutex.ReleaseMutex();
        mutex.Dispose();
        show.Dispose();
        stop.Dispose();
    }
}
