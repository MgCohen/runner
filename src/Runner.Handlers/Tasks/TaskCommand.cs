using System.Text.Json.Nodes;
using Runner.Handlers.Notion;

namespace Runner.Handlers.Tasks;

/// <summary>The result of one <c>claude -p</c> run.</summary>
public sealed record ClaudeOutput(int ExitCode, string Text);

/// <summary>
/// Runs the Todo tasks one by one, in the window of the person who starts it.
/// For each task it asks first, sets In Progress, runs Claude, then sets In Review with the answer.
/// </summary>
public sealed class TaskCommand(
    INotionClient notion,
    Func<string, string?, CancellationToken, Task<ClaudeOutput>> runClaude,
    Func<string, bool> confirm,
    TextWriter output,
    TaskOptions options,
    TimeProvider time,
    string? defaultFolder)
{
    /// <summary>Runs the command. Returns the number of tasks that went to review.</summary>
    public async Task<int> RunAsync(bool yes, bool listOnly, CancellationToken ct)
    {
        var tasks = await notion.QueryDataSourceAsync(options.DataSourceId,
            TaskPrompt.StatusFilter(options, options.TriggerStatus), ct);
        if (tasks.Count == 0)
        {
            output.WriteLine($"No tasks in {options.TriggerStatus}.");
            return 0;
        }

        output.WriteLine($"{tasks.Count} task(s) in {options.TriggerStatus}:");
        foreach (var task in tasks) output.WriteLine($"  - {task.Title}  {task.Url}");
        if (listOnly) return 0;

        var done = 0;
        foreach (var task in tasks)
        {
            if (!yes && !confirm($"Run '{task.Title}'?"))
            {
                output.WriteLine($"Skipped '{task.Title}'. It stays in {options.TriggerStatus}.");
                continue;
            }
            if (await RunOneAsync(task, ct)) done++;
        }
        return done;
    }

    private async Task<bool> RunOneAsync(NotionPage task, CancellationToken ct)
    {
        await notion.UpdatePageAsync(task.Id, new JsonObject
        {
            [options.StatusProperty] = NotionProperties.Status(options.RunningStatus),
            ["Started"] = NotionProperties.Date(time.GetUtcNow()),
            ["Finished"] = new JsonObject { ["date"] = null },
            ["Result"] = NotionProperties.Text(""),
        }, ct);
        output.WriteLine($"'{task.Title}': {options.RunningStatus}. Claude starts in this window.");

        ClaudeOutput result;
        try
        {
            var markdown = await notion.GetPageMarkdownAsync(task.Id, ct);
            var folder = task.TextOf(options.FolderProperty);
            result = await runClaude(TaskPrompt.Build(options, task, markdown),
                string.IsNullOrWhiteSpace(folder) ? defaultFolder : folder, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new ClaudeOutput(-1, ex.Message);
        }

        if (result.ExitCode != 0)
        {
            await notion.UpdatePageAsync(task.Id, new JsonObject
            {
                [options.StatusProperty] = NotionProperties.Status(options.FailedStatus),
                ["Result"] = NotionProperties.Text($"Claude failed (exit {result.ExitCode}): {result.Text}".Trim()),
            }, ct);
            output.WriteLine($"'{task.Title}': Claude failed. Status: {options.FailedStatus}.");
            return false;
        }

        await notion.UpdatePageAsync(task.Id, new JsonObject
        {
            [options.StatusProperty] = NotionProperties.Status(options.ReviewStatus),
            ["Finished"] = NotionProperties.Date(time.GetUtcNow()),
            ["Result"] = NotionProperties.Text(result.Text.Trim()),
        }, ct);
        output.WriteLine($"'{task.Title}': {options.ReviewStatus}.");
        return true;
    }
}
