namespace Runner.Handlers;

/// <summary>Settings for <see cref="NotionPageUpdated"/>. Configuration section: <c>Handlers:NotionPageUpdated</c>.</summary>
public sealed class NotionPageUpdatedOptions
{
    public const string Section = "Handlers:NotionPageUpdated";

    /// <summary>If false, the handler does nothing. Each page edit costs a Claude session, so the default is off.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The data sources (or databases) whose pages start a session. Dashes and case do not matter.
    /// If the list is empty, pages from all parents start a session.
    /// </summary>
    public List<string> DataSourceIds { get; set; } = [];

    /// <summary>How the handler starts Claude.</summary>
    public ClaudeMode Mode { get; set; } = ClaudeMode.Background;

    /// <summary>The working folder of the session. If empty, the handler uses <c>Claude:WorkingDirectory</c>.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>The instruction text before the page context in the prompt.</summary>
    public string Instructions { get; set; } =
        "A Notion page was edited. Read the page below and do the work that it asks for. " +
        "If the page does not ask for work, stop and do nothing.";

    /// <summary>The handler cuts the page Markdown at this length, to keep the prompt in the command line limit.</summary>
    public int MaxMarkdownChars { get; set; } = 20_000;

    /// <summary>
    /// The minimum time between two sessions for one page. One edit can send more than one event,
    /// and the connector can send one event two times.
    /// </summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(5);
}

public enum ClaudeMode
{
    /// <summary>Start <c>claude --bg --remote-control</c>. Open the session on claude.ai.</summary>
    Background,

    /// <summary>Run <c>claude -p</c> and wait for the result.</summary>
    Print,
}
