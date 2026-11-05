using System.Runtime.InteropServices;

namespace Claude.Infrastructure;

// A visible console window for the child processes of the app (T3): claude -p, and op read at start.
// The app has no console when a shortcut starts it. Open gives it one, and a child that has no CreateNoWindow
// writes to that window. The window closes when the last user disposes it. The close button is off: a closed
// console would stop the app. If the app already has a console (tray.ps1), Open uses it and closes nothing.
public static class VisibleConsole {
    static readonly object Gate = new();
    static int users;
    static bool owned;

    public static IDisposable Open(string title) {
        lock (Gate) {
            users++;
            if (users == 1 && GetConsoleWindow() == 0 && AllocConsole()) {
                owned = true;
                var window = GetConsoleWindow();
                var menu = GetSystemMenu(window, false);
                _ = DeleteMenu(menu, ScClose, MfByCommand);
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            }
            if (owned) {
                Console.Title = title;
            }
            return new Use();
        }
    }

    static void Close() {
        lock (Gate) {
            users--;
            if (users == 0 && owned) {
                Console.SetOut(TextWriter.Null);
                Console.SetError(TextWriter.Null);
                _ = FreeConsole();
                owned = false;
            }
        }
    }

    sealed class Use : IDisposable {
        int disposed;

        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) == 0) {
                Close();
            }
        }
    }

    const uint ScClose = 0xF060;
    const uint MfByCommand = 0x0;

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool FreeConsole();

    [DllImport("kernel32.dll")]
    static extern nint GetConsoleWindow();

    [DllImport("user32.dll")]
    static extern nint GetSystemMenu(nint window, [MarshalAs(UnmanagedType.Bool)] bool revert);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DeleteMenu(nint menu, uint position, uint flags);
}
