using System.Text.Json.Nodes;
using Claude.Infrastructure;
using Handling.Domain;
using Microsoft.Extensions.Options;
using Notion.Domain;
using Notion.Infrastructure;
using Tasks.Domain;

namespace Tasks.Infrastructure;

// One Claude run for one task page: In Progress, then claude -p, then In Review. If the task has an open question
// after the run, it goes to Waiting instead. Each change is a bot edit, so it starts no handler by itself.
public sealed class TaskRun(INotionClient notion, IClaudePrint print, PageCooldown cooldown, TimeProvider time,
    IOptions<TaskOptions> options, IOptions<ClaudeOptions> claude) {
    readonly TaskOptions o = options.Value;

    // Runs the task. False if the task already has a run. The answered question, if any, goes into the prompt.
    public async Task<bool> RunAsync(NotionPage page, NotionPage? answered, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(page);
        var key = "task:" + NotionWebhook.NormalizeId(page.Id);
        if (!cooldown.TryClaim(key, TimeSpan.FromMinutes(2))) {
            return false;
        }
        var resetStatus = answered is null ? o.TriggerStatus : o.WaitingStatus;
        try {
            await notion.UpdatePageAsync(page.Id, new JsonObject {
                [o.StatusProperty] = NotionProperties.Status(o.RunningStatus),
                ["Started"] = NotionProperties.Date(time.GetUtcNow()),
                ["Finished"] = NotionProperties.NoDate(),
                ["Result"] = NotionProperties.Text(""),
            }, ct);
            var folder = page.TextOf(o.FolderProperty);
            var directory = string.IsNullOrWhiteSpace(folder) ? o.WorkingDirectory ?? claude.Value.WorkingDirectory : folder;
            var markdown = await notion.GetPageMarkdownAsync(page.Id, ct);
            var prompt = TaskPrompt.Build(o, page, markdown, answered);
            await RunPrintAsync(page, prompt, directory, ct);
            return true;
        } catch (Exception failure) when (failure is not PermanentFailureException) {
            // Exit, a timeout or an error that can retry: the task goes back to the status that started the run, so
            // the next attempt or the next start runs it again.
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await notion.UpdatePageAsync(page.Id, new JsonObject { [o.StatusProperty] = NotionProperties.Status(resetStatus) }, limit.Token);
            throw;
        } finally {
            cooldown.Release(key);
        }
    }

    // True if one or more questions of the task have the open status.
    public async Task<bool> HasOpenQuestionAsync(NotionPage task, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(task);
        foreach (var id in task.RelationOf(o.QuestionsProperty)) {
            var question = await notion.GetPageAsync(id, ct);
            if (question.StatusOf(o.StatusProperty) == o.OpenQuestionStatus) {
                return true;
            }
        }
        return false;
    }

    async Task RunPrintAsync(NotionPage page, string prompt, string? directory, CancellationToken ct) {
        ClaudeOutput result;
        try {
            result = await print.RunAsync(prompt, directory, ct);
        } catch (Exception e) when (e is not OperationCanceledException) {
            result = new ClaudeOutput(-1, e.Message);
        }
        if (result.ExitCode != 0) {
            await notion.UpdatePageAsync(page.Id, new JsonObject {
                [o.StatusProperty] = NotionProperties.Status(o.FailedStatus),
                ["Result"] = NotionProperties.Text($"Claude failed (exit {result.ExitCode}): {result.Text}".Trim()),
            }, ct);
            throw new PermanentFailureException($"Task {page.Id}: claude -p exited with code {result.ExitCode}.");
        }
        // Claude can add a question during the run, so read the task again.
        var after = await notion.GetPageAsync(page.Id, ct);
        var status = await HasOpenQuestionAsync(after, ct) ? o.WaitingStatus : o.ReviewStatus;
        await notion.UpdatePageAsync(page.Id, new JsonObject {
            [o.StatusProperty] = NotionProperties.Status(status),
            ["Finished"] = NotionProperties.Date(time.GetUtcNow()),
            ["Result"] = NotionProperties.Text(result.Text.Trim()),
        }, ct);
    }
}
