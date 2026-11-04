// runner: commands that you run yourself in a terminal. Nothing runs in the background.
//
//   runner tasks           Asks before each Todo task, then runs it with claude -p in this window.
//   runner tasks --list    Lists the Todo tasks and stops.
//   runner tasks --yes     Runs each Todo task without a question.
//
// The Notion token comes from NOTION_API_TOKEN (tasks.ps1 gets it from 1Password).
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using Runner.Handlers.Notion;
using Runner.Handlers.Tasks;

if (args.Length == 0 || args[0] != "tasks")
{
    Console.WriteLine("Usage: runner tasks [--list] [--yes]");
    return 2;
}

var token = Environment.GetEnvironmentVariable("NOTION_API_TOKEN");
if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("NOTION_API_TOKEN is not set. Start this command with tasks.ps1.");
    return 1;
}

var folder = Environment.GetEnvironmentVariable("Claude__WorkingDirectory")
    ?? Environment.GetEnvironmentVariable("Claude__WorkingDirectory", EnvironmentVariableTarget.User)
    ?? Environment.CurrentDirectory;
var mode = Environment.GetEnvironmentVariable("Claude__PermissionMode") ?? "auto";

using var http = new HttpClient();
var notion = new NotionClient(http, Options.Create(new NotionOptions { Token = token }));
var command = new TaskCommand(notion, RunClaudeAsync, Confirm, Console.Out, new TaskOptions(), TimeProvider.System, folder);

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
var done = await command.RunAsync(args.Contains("--yes"), args.Contains("--list"), cancel.Token);
Console.WriteLine($"Done. {done} task(s) moved to review.");
return 0;

static bool Confirm(string question)
{
    Console.Write($"{question} [y/N] ");
    return Console.ReadLine()?.Trim().ToLowerInvariant() is "y" or "yes";
}

// Runs claude -p in this window. The output shows here and also goes to the Result of the task.
async Task<ClaudeOutput> RunClaudeAsync(string prompt, string? workingDirectory, CancellationToken ct)
{
    if (mode == "bypassPermissions") throw new InvalidOperationException("bypassPermissions is not allowed.");
    var start = new ProcessStartInfo("claude")
    {
        WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        UseShellExecute = false,
    };
    foreach (var argument in new[] { "-p", "--permission-mode", mode }) start.ArgumentList.Add(argument);

    Console.WriteLine($"> claude -p --permission-mode {mode}   (folder: {start.WorkingDirectory})");
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start claude.");
    await process.StandardInput.WriteAsync(prompt.AsMemory(), ct);
    process.StandardInput.Close();

    var text = new StringBuilder();
    while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
    {
        Console.WriteLine(line);
        text.AppendLine(line);
    }
    await process.WaitForExitAsync(ct);
    return new ClaudeOutput(process.ExitCode, text.ToString());
}
