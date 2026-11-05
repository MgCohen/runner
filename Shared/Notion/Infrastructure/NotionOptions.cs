namespace Notion.Infrastructure;

// Settings of the Notion client. Section: Modules:Notion.
public sealed class NotionOptions {
    // The internal API token of the "Claude workflow" connection. Use NOTION_API_TOKEN (op run), never git.
    public string? Token { get; set; }

    public string BaseUrl { get; set; } = "https://api.notion.com/";

    public string Version { get; set; } = "2026-03-11";
}
