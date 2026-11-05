namespace Claude.Infrastructure;

// Settings of the claude CLI. Section: Modules:Claude.
public sealed class ClaudeOptions {
    // The permission modes that the app accepts. bypassPermissions is not in the list.
    public static readonly IReadOnlySet<string> AllowedPermissionModes = new HashSet<string>(StringComparer.Ordinal) { "auto", "acceptEdits", "manual", "dontAsk", "plan" };

    // The claude executable. A bare name uses the PATH.
    public string Path { get; set; } = "claude";

    // The working folder of a run. It must be a folder that Claude Code trusts.
    public string? WorkingDirectory { get; set; }

    public string PermissionMode { get; set; } = "auto";
}
