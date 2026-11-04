namespace Runner.Handlers.Claude;

/// <summary>Settings for <see cref="ClaudeLauncher"/>. Configuration section: <c>Claude</c>.</summary>
public sealed class ClaudeOptions
{
    public const string Section = "Claude";

    /// <summary>The permission modes that the launcher accepts. <c>bypassPermissions</c> is not in the list.</summary>
    public static readonly IReadOnlySet<string> AllowedPermissionModes =
        new HashSet<string>(StringComparer.Ordinal) { "auto", "acceptEdits", "manual", "dontAsk", "plan" };

    /// <summary>The <c>claude</c> executable. A bare name uses the PATH.</summary>
    public string Path { get; set; } = "claude";

    /// <summary>The working folder of the session. If empty, the launcher uses the current folder.</summary>
    public string? WorkingDirectory { get; set; }

    public string PermissionMode { get; set; } = "auto";

    /// <summary>The maximum time for one <c>claude -p</c> run.</summary>
    public TimeSpan PrintTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>The maximum time for <c>claude --bg</c> to print the session ID and exit.</summary>
    public TimeSpan BackgroundStartTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>The start of each Remote Control session name.</summary>
    public string SessionNamePrefix { get; set; } = "runner";
}
