using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Runner.Handlers.Claude;
using Runner.Handlers.Notion;

namespace Runner.Handlers.Tasks;

/// <summary>
/// Checks the sessions of running tasks with the local <c>claude agents --json</c>.
/// When a session finishes, stops or runs too long, it moves the task to In Review with the last answer as the result.
/// </summary>
public sealed class TaskSessionWatcher(
    TaskStore store,
    IClaudeAgents agents,
    INotionClient notion,
    TimeProvider time,
    IOptions<TaskOptions> options,
    ILogger<TaskSessionWatcher> log) : BackgroundService
{
    private readonly TaskOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Task check failed. The next check tries again.");
            }
            await Task.Delay(_options.PollInterval, time, stoppingToken);
        }
    }

    /// <summary>Checks each running task one time.</summary>
    internal async Task CheckOnceAsync(CancellationToken ct)
    {
        var tasks = store.All();
        if (tasks.Count == 0) return;

        var list = await agents.ListAsync(ct);
        foreach (var task in tasks)
        {
            var agent = list.FirstOrDefault(a => a.Id == task.SessionId);

            if (task.Link is null && agent is not null
                && await agents.GetRemoteLinkAsync(task.SessionId, ct) is { } link)
            {
                await notion.UpdatePageAsync(task.PageId, new JsonObject { ["Session"] = NotionProperties.Url(link) }, ct);
                store.Upsert(task with { Link = link });
            }

            var reason = EndReason(task, agent);
            if (reason is null) continue;

            var answer = agent?.SessionId is { } full ? agents.ReadLastAnswer(full) : null;
            var result = reason == "done" ? answer ?? "The session finished." : $"Check the session ({reason}).\n{answer}".Trim();
            await notion.UpdatePageAsync(task.PageId, new JsonObject
            {
                [_options.StatusProperty] = NotionProperties.Status(_options.ReviewStatus),
                ["Finished"] = NotionProperties.Date(time.GetUtcNow()),
                ["Result"] = NotionProperties.Text(result),
            }, ct);
            store.Remove(task.PageId);
            log.LogInformation("Task {PageId}: session {SessionId} ended ({Reason}). Status: {Status}.",
                task.PageId, task.SessionId, reason, _options.ReviewStatus);
        }
    }

    /// <summary>Returns why the task must go to review, or null while the session still works.</summary>
    internal string? EndReason(TrackedTask task, ClaudeAgent? agent)
    {
        if (agent is null) return "session not found";
        if (agent.State is { } state && ClaudeAgents.EndStates.Contains(state)) return state.ToLowerInvariant();
        if (time.GetUtcNow() - task.StartedAt > _options.MaxRunTime) return $"still running after {_options.MaxRunTime}";
        return null;
    }
}
