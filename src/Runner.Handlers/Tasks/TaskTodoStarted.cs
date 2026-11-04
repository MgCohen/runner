using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Runner.Abstractions;
using Runner.Handlers.Claude;
using Runner.Handlers.Notion;

namespace Runner.Handlers.Tasks;

/// <summary>
/// Starts Claude when a task in the Tasks database gets the status Todo, and sets In Progress.
/// In <see cref="TaskRunMode.Print"/> (default) it runs <c>claude -p</c> in the runner window, then sets In Review.
/// In <see cref="TaskRunMode.Background"/> it starts a Remote Control session and records it in <see cref="TaskStore"/>;
/// <see cref="TaskSessionWatcher"/> then sets In Review.
/// </summary>
/// <remarks>
/// A Notion event does not give the previous status. Each change to Todo starts a session, also from In Review.
/// The runner makes its own changes as a bot, and the handler skips bot changes, so they do not start a loop.
/// </remarks>
[On("notion", "page", "properties_updated")]
[On("notion", "page", "created")]
public sealed class TaskTodoStarted(
    INotionClient notion,
    IClaudeLauncher claude,
    IClaudePrint print,
    IClaudeAgents agents,
    TaskStore store,
    PageCooldown cooldown,
    TimeProvider time,
    IOptions<TaskOptions> options,
    IOptions<ClaudeOptions> claudeOptions) : IEventHandler
{
    private readonly TaskOptions _options = options.Value;

    /// <summary>The time that the handler waits for the claude.ai link of a new session.</summary>
    internal TimeSpan LinkWait { get; set; } = TimeSpan.FromSeconds(20);

    public async Task HandleAsync(InboxEvent e, HandlerContext context)
    {
        var log = context.Logger;
        var ct = context.CancellationToken;
        if (!_options.Enabled || NotionWebhook.HasBotAuthor(e.Payload)) return;

        var pageId = NotionWebhook.EntityId(e.Payload)
            ?? throw new PermanentFailureException($"Event {e.Id} has no entity.id.");
        var page = await notion.GetPageAsync(pageId, ct);
        if (!IsTask(page) || page.StatusOf(_options.StatusProperty) != _options.TriggerStatus) return;

        var key = NotionWebhook.NormalizeId(pageId);
        if (store.All().Any(t => NotionWebhook.NormalizeId(t.PageId) == key)
            || !cooldown.TryClaim("task:" + key, TimeSpan.FromMinutes(2)))
        {
            log.LogInformation("Task {PageId} already has a session. No session starts.", pageId);
            return;
        }

        var now = time.GetUtcNow();
        await notion.UpdatePageAsync(pageId, new JsonObject
        {
            [_options.StatusProperty] = NotionProperties.Status(_options.RunningStatus),
            ["Started"] = NotionProperties.Date(now),
            ["Finished"] = new JsonObject { ["date"] = null },
            ["Result"] = NotionProperties.Text(""),
        }, ct);

        var folder = page.TextOf(_options.FolderProperty);
        var workingDirectory = string.IsNullOrWhiteSpace(folder)
            ? _options.WorkingDirectory ?? claudeOptions.Value.WorkingDirectory
            : folder;

        if (_options.Mode == TaskRunMode.Print)
        {
            await RunPrintAsync(page, workingDirectory, log, ct);
            return;
        }

        ClaudeSession session;
        try
        {
            var markdown = await notion.GetPageMarkdownAsync(pageId, ct);
            var request = new ClaudeRequest(BuildPrompt(page, markdown), workingDirectory, SessionName(page));
            session = await claude.StartRemoteSessionAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            cooldown.Release("task:" + key);
            await notion.UpdatePageAsync(pageId, new JsonObject
            {
                [_options.StatusProperty] = NotionProperties.Status(_options.FailedStatus),
                ["Result"] = NotionProperties.Text($"The session did not start: {ex.Message}"),
            }, ct);
            throw new PermanentFailureException($"Task {pageId}: the session did not start.", ex);
        }

        var link = await WaitForLinkAsync(session.SessionId, ct);
        store.Upsert(new TrackedTask(pageId, session.SessionId, now, link));
        if (link is not null)
        {
            await notion.UpdatePageAsync(pageId, new JsonObject { ["Session"] = NotionProperties.Url(link) }, ct);
        }
        log.LogInformation("Task {PageId} ({Title}): session {SessionId} started. Link: {Link}",
            pageId, page.Title, session.SessionId, link ?? "not yet");
    }

    /// <summary>Runs <c>claude -p</c> in the runner window, then sets In Review with the answer, or Backlog on a failure.</summary>
    private async Task RunPrintAsync(NotionPage page, string? workingDirectory, ILogger log, CancellationToken ct)
    {
        var key = "task:" + NotionWebhook.NormalizeId(page.Id);
        log.LogInformation("Task '{Title}': {Status}. Claude runs in this window.", page.Title, _options.RunningStatus);
        ClaudeOutput result;
        try
        {
            var markdown = await notion.GetPageMarkdownAsync(page.Id, ct);
            result = await print.RunAsync(BuildPrompt(page, markdown), workingDirectory, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new ClaudeOutput(-1, ex.Message);
        }
        cooldown.Release(key);

        if (result.ExitCode != 0)
        {
            await notion.UpdatePageAsync(page.Id, new JsonObject
            {
                [_options.StatusProperty] = NotionProperties.Status(_options.FailedStatus),
                ["Result"] = NotionProperties.Text($"Claude failed (exit {result.ExitCode}): {result.Text}".Trim()),
            }, ct);
            throw new PermanentFailureException($"Task {page.Id}: claude -p exited with code {result.ExitCode}.");
        }

        await notion.UpdatePageAsync(page.Id, new JsonObject
        {
            [_options.StatusProperty] = NotionProperties.Status(_options.ReviewStatus),
            ["Finished"] = NotionProperties.Date(time.GetUtcNow()),
            ["Result"] = NotionProperties.Text(result.Text.Trim()),
        }, ct);
        log.LogInformation("Task '{Title}': {Status}.", page.Title, _options.ReviewStatus);
    }

    private bool IsTask(NotionPage page)
    {
        var target = NotionWebhook.NormalizeId(_options.DataSourceId);
        return (page.ParentDataSourceId is { } ds && NotionWebhook.NormalizeId(ds) == target)
            || (page.ParentDatabaseId is { } db && NotionWebhook.NormalizeId(db) == target);
    }

    private async Task<string?> WaitForLinkAsync(string sessionId, CancellationToken ct)
    {
        var end = time.GetUtcNow() + LinkWait;
        while (true)
        {
            try
            {
                if (await agents.GetRemoteLinkAsync(sessionId, ct) is { } link) return link;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The watcher tries again later.
            }
            if (time.GetUtcNow() >= end) return null;
            await Task.Delay(TimeSpan.FromSeconds(2), time, ct);
        }
    }

    internal string BuildPrompt(NotionPage page, string markdown) => TaskPrompt.Build(_options, page, markdown);

    private static string SessionName(NotionPage page)
    {
        var title = new string(page.Title.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-').ToArray()).Trim();
        if (title.Length > 40) title = title[..40].Trim();
        return string.IsNullOrEmpty(title) ? "task" : $"task {title}";
    }
}
