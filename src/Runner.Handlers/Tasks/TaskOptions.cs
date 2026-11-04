namespace Runner.Handlers.Tasks;

/// <summary>Settings for the Notion Tasks flow. Configuration section: <c>Handlers:Tasks</c>.</summary>
public sealed class TaskOptions
{
    public const string Section = "Handlers:Tasks";

    public bool Enabled { get; set; } = true;

    /// <summary>The data source of the Tasks database (Workflow / Tasks).</summary>
    public string DataSourceId { get; set; } = "f0db2108-3a1e-4e80-8f74-f081643e0a29";

    public string StatusProperty { get; set; } = "Status";

    /// <summary>A task with this status starts a session.</summary>
    public string TriggerStatus { get; set; } = "Todo";

    /// <summary>The runner sets this status when the session starts.</summary>
    public string RunningStatus { get; set; } = "In Progress";

    /// <summary>The runner sets this status when the session finishes or stops.</summary>
    public string ReviewStatus { get; set; } = "In Review";

    /// <summary>The runner sets this status when the session cannot start.</summary>
    public string FailedStatus { get; set; } = "Backlog";

    /// <summary>A rich text property with the working folder. If empty, the runner uses <see cref="WorkingDirectory"/>.</summary>
    public string FolderProperty { get; set; } = "Folder";

    /// <summary>The default working folder. If empty, the runner uses <c>Claude:WorkingDirectory</c>.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>The file that remembers the running tasks. A relative path starts at the application folder.</summary>
    public string StatePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Runner", "tasks.json");

    /// <summary>The time between two checks of the running sessions.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>After this time, the runner moves the task to review, also if the session still runs.</summary>
    public TimeSpan MaxRunTime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>The maximum number of Markdown characters of the task page in the prompt.</summary>
    public int MaxMarkdownChars { get; set; } = 20_000;

    public string Instructions { get; set; } =
        """
        You work on a task from the Notion Tasks database. The task page is below.
        - Do the task. If you change a repository, use a feature branch and open a PR. Never merge.
        - Do not change the Status of the task. The runner moves it to In Review when your turn ends.
        - If you need a decision from the user, stop and ask it as your last message.
        - End with a short summary (at most 8 lines): what you did, links to PRs or files, and what the user must review.
        """;
}
