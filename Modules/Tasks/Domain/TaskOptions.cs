namespace Tasks.Domain;

// Settings of the Notion Tasks flow. Section: Modules:Tasks.
public sealed class TaskOptions {
    // The data source of the Tasks database (Workflow / Tasks).
    public string DataSourceId { get; set; } = "f0db2108-3a1e-4e80-8f74-f081643e0a29";

    public string StatusProperty { get; set; } = "Status";

    public string TriggerStatus { get; set; } = "Todo";

    public string RunningStatus { get; set; } = "In Progress";

    public string ReviewStatus { get; set; } = "In Review";

    public string FailedStatus { get; set; } = "Backlog";

    // A rich text property with the working folder. Empty: WorkingDirectory, else Modules:Claude:WorkingDirectory.
    public string FolderProperty { get; set; } = "Folder";

    public string? WorkingDirectory { get; set; }

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
