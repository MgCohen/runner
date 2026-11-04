using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Runner.Abstractions;
using Runner.Handlers.Claude;
using Runner.Handlers.Notion;

namespace Runner.Handlers;

/// <summary>
/// Starts a Claude session when a Notion page changes. The prompt has the page title, URL and Markdown.
/// </summary>
/// <remarks>
/// The handler stops without a session in these cases:
/// the switch is off, a bot made the edit, the page is not in a configured data source,
/// or the page started a session in the cooldown period.
/// </remarks>
[On("notion", "page", "properties_updated")]
[On("notion", "page", "content_updated")]
public sealed class NotionPageUpdated(
    INotionClient notion,
    IClaudeLauncher claude,
    PageCooldown cooldown,
    IOptions<NotionPageUpdatedOptions> options) : IEventHandler
{
    private readonly NotionPageUpdatedOptions _options = options.Value;

    public async Task HandleAsync(InboxEvent e, HandlerContext context)
    {
        var log = context.Logger;
        if (!_options.Enabled)
        {
            log.LogDebug("NotionPageUpdated is off. Event {Id} is skipped.", e.Id);
            return;
        }

        if (NotionWebhook.HasBotAuthor(e.Payload))
        {
            log.LogInformation("Event {Id} has a bot author. No session starts.", e.Id);
            return;
        }

        var pageId = NotionWebhook.EntityId(e.Payload)
            ?? throw new PermanentFailureException($"Event {e.Id} has no entity.id.");

        var page = await notion.GetPageAsync(pageId, context.CancellationToken);
        if (!IsInScope(page))
        {
            log.LogInformation("Page {PageId} is not in a configured data source. No session starts.", pageId);
            return;
        }

        var key = NotionWebhook.NormalizeId(pageId);
        if (!cooldown.TryClaim(key, _options.Cooldown))
        {
            log.LogInformation("Page {PageId} started a session less than {Cooldown} ago. No session starts.",
                pageId, _options.Cooldown);
            return;
        }

        try
        {
            var markdown = await notion.GetPageMarkdownAsync(pageId, context.CancellationToken);
            var request = new ClaudeRequest(BuildPrompt(page, markdown, e), _options.WorkingDirectory,
                SessionName(page));
            await StartAsync(request, context);
        }
        catch
        {
            cooldown.Release(key);
            throw;
        }
    }

    private async Task StartAsync(ClaudeRequest request, HandlerContext context)
    {
        if (_options.Mode == ClaudeMode.Print)
        {
            var result = await claude.RunPrintAsync(request, context.CancellationToken);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"claude -p exited with code {result.ExitCode}: {result.Error}");
            }
            context.Logger.LogInformation("claude -p result: {Output}", result.Output);
            return;
        }

        var session = await claude.StartRemoteSessionAsync(request, context.CancellationToken);
        context.Logger.LogInformation("Claude session {SessionId} ({Name}) started.", session.SessionId, session.Name);
    }

    private bool IsInScope(NotionPage page)
    {
        if (_options.DataSourceIds.Count == 0)
        {
            return true;
        }

        var allowed = _options.DataSourceIds.Select(NotionWebhook.NormalizeId).ToHashSet();
        return (page.ParentDataSourceId is { } ds && allowed.Contains(NotionWebhook.NormalizeId(ds)))
            || (page.ParentDatabaseId is { } db && allowed.Contains(NotionWebhook.NormalizeId(db)));
    }

    internal string BuildPrompt(NotionPage page, string markdown, InboxEvent e)
    {
        if (markdown.Length > _options.MaxMarkdownChars)
        {
            markdown = markdown[.._options.MaxMarkdownChars] + "\n\n[The page continues. Read it at the URL.]";
        }

        return new StringBuilder()
            .AppendLine(_options.Instructions)
            .AppendLine()
            .AppendLine($"Event: notion {e.EventType}.{e.Action} (inbox id {e.Id})")
            .AppendLine($"Title: {page.Title}")
            .AppendLine($"URL: {page.Url}")
            .AppendLine($"Page ID: {page.Id}")
            .AppendLine()
            .AppendLine("Page content (Markdown):")
            .AppendLine(markdown)
            .ToString();
    }

    private static string SessionName(NotionPage page)
    {
        var title = new string(page.Title.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-').ToArray()).Trim();
        if (title.Length > 40)
        {
            title = title[..40].Trim();
        }
        return string.IsNullOrEmpty(title) ? $"notion-{NotionWebhook.NormalizeId(page.Id)}" : $"notion {title}";
    }
}
