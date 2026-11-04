using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Runner.Host;

namespace Runner.Tray;

/// <summary>
/// <c>--mock --selftest</c>: runs a fixed scenario against the mock services, saves an image of the widget
/// after each step, writes the results to <c>results.txt</c>, then exits. It needs no screen control.
/// </summary>
internal sealed class SelfTest(WidgetForm form, IServiceProvider services, Func<Task> exit)
{
    public static string Folder => Path.Combine(MockMode.Folder, "selftest");

    private readonly StringBuilder _report = new();
    private int _failures;

    /// <summary>
    /// Real mode (<c>--selftest</c> without <c>--mock</c>): turns the Tasks handler off, turns the listener on, waits for
    /// the inbox connection, saves an image, restores the switches and exits. No handler can start Claude.
    /// </summary>
    public async Task RunRealAsync()
    {
        var folder = Path.Combine(Path.GetDirectoryName(MockMode.Folder)!, "selftest-real");
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.GetFiles(folder)) File.Delete(old);
        var switches = services.GetRequiredService<RunnerSwitches>();
        var listener = services.GetRequiredService<SocketListener>();
        var tasksWasOn = switches.IsHandlerEnabled("TaskTodoStarted");
        var listenerWasOn = switches.ListenerEnabled;

        switches.SetHandler("TaskTodoStarted", false);
        switches.SetHandler("NotionPageUpdated", false);
        form.ShowWidget();
        await Task.Delay(1000);
        Capture(folder, "01-real-paused");

        switches.SetListener(true);
        await WaitFor(() => listener.State == ListenerState.Connected, 30);
        Check("Real inbox: the listener connects and gets hello", listener.State == ListenerState.Connected);
        await Task.Delay(3000);
        Capture(folder, "02-real-connected");
        form.ShowTrafficTab();
        await Task.Delay(500);
        Capture(folder, "03-real-traffic");

        switches.SetListener(listenerWasOn);
        switches.SetHandler("TaskTodoStarted", tasksWasOn);
        _report.AppendLine(_failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({_failures})");
        File.WriteAllText(Path.Combine(folder, "results.txt"), _report.ToString());
        await exit();
    }

    public async Task RunAsync()
    {
        Directory.CreateDirectory(Folder);
        foreach (var old in Directory.GetFiles(Folder)) File.Delete(old);
        var switches = services.GetRequiredService<RunnerSwitches>();
        var events = services.GetRequiredService<MockEvents>();
        var notion = services.GetRequiredService<MockNotion>();
        var store = services.GetRequiredService<EventStore>();

        form.ShowWidget();
        await Task.Delay(1500);
        Check("Listener is off at start", !switches.ListenerEnabled);
        Capture("01-start-paused");

        events.SendTaskEvent();
        await Task.Delay(2500);
        Check("Listener off: a task event does not run", notion.Status == "Todo");
        Capture("02-event-waits-while-paused");

        switches.SetListener(true);
        await WaitFor(() => notion.Status == "In Review", 15);
        Check("Listener on: the waiting task runs to In Review", notion.Status == "In Review");
        Check("Result has the mock answer", notion.Result.Contains("mock task flow works", StringComparison.Ordinal));
        Capture("03-task-in-review");

        switches.SetHandler("TaskTodoStarted", false);
        events.SendTaskEvent();
        await Task.Delay(3000);
        Check("Handler off: the task stays in Todo", notion.Status == "Todo");
        Capture("04-handler-off");

        Check("Handler off: the event is done (skipped), not pending", !store.GetPending().Any());
        switches.SetHandler("TaskTodoStarted", true);
        events.SendTaskEvent();
        await WaitFor(() => notion.Status == "In Review", 15);
        Check("Handler on again: a new task event runs to In Review", notion.Status == "In Review");
        Capture("05-handler-on-again");

        switches.SetSource("github", false);
        var github = events.SendGitHubEvent();
        await Task.Delay(2500);
        Check("Source off: the GitHub event stays pending", store.GetPending().Any(e => e.Event.Id == github));
        Capture("06-source-off");

        switches.SetSource("github", true);
        await WaitFor(() => !store.GetPending().Any(e => e.Event.Id == github), 10);
        Check("Source on again: the GitHub event is done", !store.GetPending().Any(e => e.Event.Id == github));

        form.ShowTrafficTab();
        await Task.Delay(1000);
        Capture("07-traffic");

        _report.AppendLine(_failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({_failures})");
        File.WriteAllText(Path.Combine(Folder, "results.txt"), _report.ToString());
        Console.WriteLine(_report.ToString());
        await exit();
    }

    private void Check(string name, bool ok)
    {
        if (!ok) _failures++;
        _report.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}");
    }

    private void Capture(string name) => Capture(Folder, name);

    private void Capture(string folder, string name)
    {
        using var image = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(image, new Rectangle(0, 0, form.Width, form.Height));
        image.Save(Path.Combine(folder, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
    }

    private static async Task WaitFor(Func<bool> condition, int seconds)
    {
        for (var i = 0; i < seconds * 4 && !condition(); i++) await Task.Delay(250);
    }
}
