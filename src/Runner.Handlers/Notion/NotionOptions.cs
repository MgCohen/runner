namespace Runner.Handlers.Notion;

/// <summary>Settings for <see cref="NotionClient"/>. Configuration section: <c>Notion</c>.</summary>
public sealed class NotionOptions
{
    public const string Section = "Notion";

    /// <summary>
    /// The internal API token of the "Claude workflow" connection.
    /// Keep it in user secrets or in the <c>NOTION_API_TOKEN</c> environment variable, never in git.
    /// </summary>
    public string? Token { get; set; }

    public string BaseUrl { get; set; } = "https://api.notion.com/";

    public string Version { get; set; } = "2026-03-11";
}
