using System.Text.Json.Nodes;
using Claude.Infrastructure;
using Handling.Domain;
using Handling.Slices.Handling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotionApi.Domain;
using NotionApi.Infrastructure;
using Tasks.Domain;
using Tasks.Infrastructure;

namespace Tasks.Slices.StartTodoTask;

// Starts Claude when a task of the Tasks database gets the status Todo, and sets In Progress.
// Print mode (default): claude -p runs in the window that started the app, then the task goes to In Review with the answer.
// Background mode: a Remote Control session starts; the session watcher sets In Review.
// A Notion event does not give the old status, so each change to Todo starts a run. The runner edits as a bot,
// and the handler skips bot edits, so its own changes do not start a loop.
sealed partial class TaskTodoStarted(HandlerContext context, INotionClient notion, IClaudePrint print, IClaudeLauncher launcher, IClaudeAgents agents,
    TaskStore store, PageCooldown cooldown, TimeProvider time, IOptions<TaskOptions> options, IOptions<ClaudeOptions> claude) : InboxHandler(context) {
    readonly TaskOptions o = options.Value;

    public override HandlerConfig Config { get; } = new() {
        Sources = ["notion"],
        Matches = [new Match("page", "properties_updated"), new Match("page", "created")],
        Description = "Starts Claude when a Notion task goes to Todo",
        Timeout = TimeSpan.FromMinutes(30),
    };

    protected override async Task Run(InboxEvent e, CancellationToken ct) {
        if (NotionWebhook.HasBotAuthor(e.Payload)) {
            return;
        }
        var pageId = NotionWebhook.EntityId(e.Payload) ?? throw new PermanentFailureException($"Event {e.Id} has no entity.id.");
        var page = await notion.GetPageAsync(pageId, ct);
        if (!page.IsIn(o.DataSourceId) || page.StatusOf(o.StatusProperty) != o.TriggerStatus) {
            return;
        }
        var key = "task:" + NotionWebhook.NormalizeId(pageId);
        if (store.All().Any(t => NotionWebhook.NormalizeId(t.PageId) == NotionWebhook.NormalizeId(pageId)) || !cooldown.TryClaim(key, TimeSpan.FromMinutes(2))) {
            AlreadyRunning(Log, pageId);
            return;
        }
        await notion.UpdatePageAsync(pageId, new JsonObject {
            [o.StatusProperty] = NotionProperties.Status(o.RunningStatus),
            ["Started"] = NotionProperties.Date(time.GetUtcNow()),
            ["Finished"] = NotionProperties.NoDate(),
            ["Result"] = NotionProperties.Text(""),
        }, ct);
        var folder = page.TextOf(o.FolderProperty);
        var directory = string.IsNullOrWhiteSpace(folder) ? o.WorkingDirectory ?? claude.Value.WorkingDirectory : folder;
        try {
            var markdown = await notion.GetPageMarkdownAsync(pageId, ct);
            var prompt = TaskPrompt.Build(o, page, markdown);
            if (o.Mode == TaskRunMode.Print) {
                await RunPrintAsync(page, prompt, directory, ct);
            } else {
                await StartSessionAsync(page, prompt, directory, ct);
            }
        } catch (Exception failure) when (failure is not PermanentFailureException) {
            // Exit, a timeout or an error that can retry: the task goes back to Todo, so the next attempt or the
            // next start runs it again. The change is a bot edit, so it starts nothing by itself.
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await notion.UpdatePageAsync(pageId, new JsonObject { [o.StatusProperty] = NotionProperties.Status(o.TriggerStatus) }, limit.Token);
            throw;
        } finally {
            cooldown.Release(key);
        }
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
        await notion.UpdatePageAsync(page.Id, new JsonObject {
            [o.StatusProperty] = NotionProperties.Status(o.ReviewStatus),
            ["Finished"] = NotionProperties.Date(time.GetUtcNow()),
            ["Result"] = NotionProperties.Text(result.Text.Trim()),
        }, ct);
    }

    async Task StartSessionAsync(NotionPage page, string prompt, string? directory, CancellationToken ct) {
        ClaudeSession session;
        try {
            session = await launcher.StartRemoteSessionAsync(prompt, directory, TaskPrompt.SessionName(page), ct);
        } catch (Exception e) when (e is not OperationCanceledException) {
            await notion.UpdatePageAsync(page.Id, new JsonObject {
                [o.StatusProperty] = NotionProperties.Status(o.FailedStatus),
                ["Result"] = NotionProperties.Text($"The session did not start: {e.Message}"),
            }, ct);
            throw new PermanentFailureException($"Task {page.Id}: the session did not start.", e);
        }
        var link = await agents.GetRemoteLinkAsync(session.SessionId, ct);
        store.Upsert(new TrackedTask(page.Id, session.SessionId, time.GetUtcNow(), link));
        if (link is not null) {
            await notion.UpdatePageAsync(page.Id, new JsonObject { ["Session"] = NotionProperties.Url(link) }, ct);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Task {PageId} already has a run. No run starts.")]
    static partial void AlreadyRunning(ILogger logger, string pageId);
}
