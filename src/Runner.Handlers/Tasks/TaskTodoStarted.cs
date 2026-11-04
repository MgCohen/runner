using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Runner.Abstractions;
using Runner.Handlers.Claude;
using Runner.Handlers.Notion;

namespace Runner.Handlers.Tasks;

/// <summary>
/// Starts a Claude session when a task in the Tasks database gets the status Todo.
/// It sets the status In Progress, starts a background Remote Control session and records the task in
/// <see cref="TaskStore"/>. <see cref="TaskSessionWatcher"/> moves the task to In Review when the session finishes.
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

        ClaudeSession session;
        try
        {
            var markdown = await notion.GetPageMarkdownAsync(pageId, ct);
            var folder = page.TextOf(_options.FolderProperty);
            var request = new ClaudeRequest(BuildPrompt(page, markdown),
                string.IsNullOrWhiteSpace(folder) ? _options.WorkingDirectory ?? claudeOptions.Value.WorkingDirectory : folder,
                SessionName(page));
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
