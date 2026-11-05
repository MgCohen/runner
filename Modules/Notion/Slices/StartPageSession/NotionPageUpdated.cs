using System.Globalization;
using System.Text;
using Claude.Infrastructure;
using Handling.Domain;
using Handling.Slices.Handling;
using Microsoft.Extensions.Options;
using Notion.Domain;
using NotionApi.Domain;
using NotionApi.Infrastructure;

namespace Notion.Slices.StartPageSession;

// Starts a background Claude session when a Notion page changes. It is off by default: each page edit costs a session.
// It does nothing for a bot edit, a page out of the data sources, or a page that started a session in the cooldown.
sealed class NotionPageUpdated(HandlerContext context, INotionClient notion, IClaudeLauncher claude, PageCooldown cooldown, IOptions<PageUpdatedOptions> options)
    : InboxHandler(context) {
    readonly PageUpdatedOptions o = options.Value;

    public override HandlerConfig Config { get; } = new() {
        Sources = ["notion"],
        Matches = [new Match("page", "properties_updated"), new Match("page", "content_updated")],
        Description = "Any page edit starts a session",
        DefaultOn = false,
    };

    protected override async Task Run(InboxEvent e, CancellationToken ct) {
        if (NotionWebhook.HasBotAuthor(e.Payload)) {
            return;
        }
        var pageId = NotionWebhook.EntityId(e.Payload) ?? throw new PermanentFailureException($"Event {e.Id} has no entity.id.");
        var page = await notion.GetPageAsync(pageId, ct);
        if (o.DataSourceIds.Count > 0 && !o.DataSourceIds.Any(page.IsIn)) {
            return;
        }
        var key = NotionWebhook.NormalizeId(pageId);
        if (!cooldown.TryClaim(key, o.Cooldown)) {
            return;
        }
        try {
            var markdown = await notion.GetPageMarkdownAsync(pageId, ct);
            await claude.StartRemoteSessionAsync(Prompt(page, markdown, e), o.WorkingDirectory, Name(page), ct);
        } catch {
            cooldown.Release(key);
            throw;
        }
    }

    string Prompt(NotionPage page, string markdown, InboxEvent e) {
        if (markdown.Length > o.MaxMarkdownChars) {
            markdown = markdown[..o.MaxMarkdownChars] + "\n\n[The page continues. Read it at the URL.]";
        }
        return new StringBuilder()
            .AppendLine(o.Instructions)
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Event: notion {e.EventType}.{e.Action} (inbox id {e.Id})")
            .AppendLine(CultureInfo.InvariantCulture, $"Title: {page.Title}")
            .AppendLine(CultureInfo.InvariantCulture, $"URL: {page.Url}")
            .AppendLine(CultureInfo.InvariantCulture, $"Page ID: {page.Id}")
            .AppendLine()
            .AppendLine("Page content (Markdown):")
            .AppendLine(markdown)
            .ToString();
    }

    static string Name(NotionPage page) {
        var title = new string([.. page.Title.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-')]).Trim();
        if (title.Length > 40) {
            title = title[..40].Trim();
        }
        return string.IsNullOrEmpty(title) ? $"notion-{NotionWebhook.NormalizeId(page.Id)}" : $"notion {title}";
    }
}
