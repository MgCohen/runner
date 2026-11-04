namespace Runner.Handlers.Claude;

/// <summary>Starts Claude Code sessions with the <c>claude</c> CLI.</summary>
public interface IClaudeLauncher
{
    /// <summary>Runs <c>claude -p</c> and waits for the result.</summary>
    Task<ClaudeRunResult> RunPrintAsync(ClaudeRequest request, CancellationToken cancellationToken);

    /// <summary>Starts a background Remote Control session and returns its ID. The session keeps running.</summary>
    Task<ClaudeSession> StartRemoteSessionAsync(ClaudeRequest request, CancellationToken cancellationToken);
}

/// <summary>One prompt for Claude. Null values use <see cref="ClaudeOptions"/>.</summary>
public sealed record ClaudeRequest(string Prompt, string? WorkingDirectory = null, string? Name = null);

public sealed record ClaudeRunResult(int ExitCode, string Output, string Error)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>A background session. Open it on claude.ai, or use <c>claude attach &lt;id&gt;</c>.</summary>
public sealed record ClaudeSession(string SessionId, string Name, string Output);
