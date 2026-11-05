namespace Claude.Infrastructure;

// Mock mode: a fake Claude. No process starts. The self-test can make a run slow, to test the exit (A28).
public sealed class MockClaude : IClaudePrint, IClaudeLauncher, IClaudeAgents {
    public const string Answer = "OK, the mock task flow works.";

    // The time of one mock run.
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(1);

    public int Runs { get; private set; }

    public async Task<ClaudeOutput> RunAsync(string prompt, string? workingDirectory, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(prompt);
        Runs++;
        await Console.Out.WriteLineAsync($"> [mock] claude -p   (prompt: {prompt.Length} chars)");
        await Task.Delay(Delay, ct);
        await Console.Out.WriteLineAsync(Answer);
        return new ClaudeOutput(0, Answer);
    }

    public Task<ClaudeSession> StartRemoteSessionAsync(string prompt, string? workingDirectory, string name, CancellationToken ct) =>
        Task.FromResult(new ClaudeSession("mock" + Guid.NewGuid().ToString("N")[..8], name));

    public Task<IReadOnlyList<ClaudeAgent>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ClaudeAgent>>([]);

    public Task<string?> GetRemoteLinkAsync(string id, CancellationToken ct) => Task.FromResult<string?>(null);

    public string? ReadLastAnswer(string sessionId) => Answer;
}
