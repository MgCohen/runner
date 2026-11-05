using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace App.Host;

// Commands to the app that runs, for tests and scripts:
//   App.Host.exe status          the mode, the listener state, the process ID and the data folder
//   App.Host.exe mode mock|real  saves the mode and restarts the app in it, then shows the new status
//   App.Host.exe show            opens the widget
//   App.Host.exe quit            stops the app
public static partial class CommandClient {
    static readonly string[] Commands = ["status", "mode", "show", "quit"];

    public static bool IsCommand(IReadOnlyList<string> args) => args.Count > 0 && Commands.Contains(args[0]);

    public static int Run(IReadOnlyList<string> args) {
        ArgumentNullException.ThrowIfNull(args);
        ParentConsole.Attach();
        if (args[0] == "mode" && (args.Count < 2 || args[1] is not ("mock" or "real"))) {
            Console.Error.WriteLine("Usage: App.Host.exe mode mock|real");
            return 2;
        }
        var command = string.Join(' ', args);
        var reply = Send(command, TimeSpan.FromSeconds(3));
        if (reply is null) {
            Console.Error.WriteLine("The runner does not run. Start it with the desktop shortcut.");
            return 1;
        }
        Console.WriteLine(reply);
        if (args[0] == "mode" && reply.StartsWith("restarting", StringComparison.Ordinal)) {
            return WaitFor(s => s.StartsWith(args[1], StringComparison.Ordinal) && !s.Contains("starting", StringComparison.Ordinal), TimeSpan.FromSeconds(90));
        }
        if (args[0] == "quit") {
            return WaitFor(s => s.Length == 0, TimeSpan.FromSeconds(20));
        }
        return reply.StartsWith("error", StringComparison.Ordinal) ? 1 : 0;
    }

    // Asks for the status until the condition is true. No answer is the empty status.
    static int WaitFor(Func<string, bool> done, TimeSpan limit) {
        var end = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < end) {
            Thread.Sleep(500);
            var status = Send("status", TimeSpan.FromSeconds(1)) ?? "";
            if (done(status)) {
                Console.WriteLine(status.Length == 0 ? "stopped" : status);
                return 0;
            }
        }
        Console.Error.WriteLine("No answer in time. See %LOCALAPPDATA%\\Runner\\runner.log.");
        return 1;
    }

    static string? Send(string command, TimeSpan timeout) {
        try {
            using var pipe = new NamedPipeClientStream(".", SingleInstance.PipeName(SingleInstance.Name(selfTest: false)), PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(timeout);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);
            writer.WriteLine(command);
            return reader.ReadLine();
        } catch (Exception e) when (e is TimeoutException or IOException) {
            return null;
        }
    }
}

// The app is a Windows app, so it has no console. If a terminal starts it, it uses the console of that terminal
// for its output. A start from the desktop shortcut has no parent console, and nothing changes.
public static partial class ParentConsole {
    public static void Attach() {
        if (GetStdHandle(StdOutput) is 0 or -1) {
            _ = AttachConsole(AttachParentProcess);
        }
    }

    const int StdOutput = -11;
    const int AttachParentProcess = -1;

    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int handle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);
}
