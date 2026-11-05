using System.Text.Json.Nodes;
using Claude.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotionApi.Domain;
using NotionApi.Infrastructure;
using Tasks.Domain;
using Tasks.Infrastructure;

namespace Tasks.Slices.StartTodoTask;

// Background mode only: checks the sessions of the running tasks. When a session ends or runs too long,
// the task goes to In Review with the last answer as the result.
public sealed partial class TaskSessionWatcher(TaskStore store, IClaudeAgents agents, INotionClient notion, TimeProvider time, IOptions<TaskOptions> options,
    ILogger<TaskSessionWatcher> log) : BackgroundService {
    readonly TaskOptions o = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (o.Mode != TaskRunMode.Background) {
            return;
        }
        while (!stoppingToken.IsCancellationRequested) {
            try {
                await CheckOnceAsync(stoppingToken);
            } catch (Exception e) when (e is not OperationCanceledException) {
                CheckFailed(log, e);
            }
            await Task.Delay(o.PollInterval, time, stoppingToken);
        }
    }

    public async Task CheckOnceAsync(CancellationToken ct) {
        var tasks = store.All();
        if (tasks.Count == 0) {
            return;
        }
        var list = await agents.ListAsync(ct);
        foreach (var task in tasks) {
            var agent = list.FirstOrDefault(a => a.Id == task.SessionId);
            var reason = agent is null ? "session not found"
                : agent.State is { } state && ClaudeAgents.EndStates.Contains(state) ? state.ToLowerInvariant()
                : time.GetUtcNow() - task.StartedAt > o.MaxRunTime ? $"still running after {o.MaxRunTime}" : null;
            if (reason is null) {
                continue;
            }
            var answer = agent?.SessionId is { } full ? agents.ReadLastAnswer(full) : null;
            var result = reason == "done" ? answer ?? "The session finished." : $"Check the session ({reason}).\n{answer}".Trim();
            await notion.UpdatePageAsync(task.PageId, new JsonObject {
                [o.StatusProperty] = NotionProperties.Status(o.ReviewStatus),
                ["Finished"] = NotionProperties.Date(time.GetUtcNow()),
                ["Result"] = NotionProperties.Text(result),
            }, ct);
            store.Remove(task.PageId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The task check failed. The next check tries again.")]
    static partial void CheckFailed(ILogger logger, Exception e);
}
