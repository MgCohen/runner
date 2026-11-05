namespace Notion.Domain;

// Settings of NotionPageUpdated. Section: Modules:Notion.
public sealed class PageUpdatedOptions {
    // The data sources (or databases) whose pages start a session. Empty: all pages.
    public IReadOnlyList<string> DataSourceIds { get; set; } = [];

    // The working folder of the session. Empty: Modules:Claude:WorkingDirectory.
    public string? WorkingDirectory { get; set; }

    public string Instructions { get; set; } =
        "A Notion page was edited. Read the page below and do the work that it asks for. If the page does not ask for work, stop and do nothing.";

    public int MaxMarkdownChars { get; set; } = 20_000;

    // The minimum time between two sessions for one page.
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(5);
}
