using Handling.Domain;
using Handling.Slices.Handling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notion.Domain;
using Notion.Infrastructure;
using Tasks.Domain;
using Tasks.Infrastructure;

namespace Tasks.Slices.StartTodoTask;

// Starts Claude when a task of the Tasks database gets the status Todo (TaskRun).
// A Notion event does not give the old status, so each change to Todo starts a run. The runner edits as a bot,
// and the handler skips bot edits, so its own changes do not start a loop.
sealed partial class TaskTodoStarted(HandlerContext context, INotionClient notion, TaskRun run, IOptions<TaskOptions> options) : InboxHandler(context) {
    readonly TaskOptions o = options.Value;

    public override HandlerConfig Config { get; } = new() {
        Sources = ["notion"],
        Matches = [new Match("page", "properties_updated"), new Match("page", "created")],
        Description = "Starts Claude when a Notion task goes to Todo",
        Timeout = TimeSpan.FromMinutes(30),
    };

    protected override async Task Run(InboxEvent e, CancellationToken ct) {
        if (NotionWebhook.HasBotAuthor(e.Payload)) {
            return;
        }
        var pageId = NotionWebhook.EntityId(e.Payload) ?? throw new PermanentFailureException($"Event {e.Id} has no entity.id.");
        var page = await notion.GetPageAsync(pageId, ct);
        if (!page.IsIn(o.DataSourceId) || page.StatusOf(o.StatusProperty) != o.TriggerStatus) {
            return;
        }
        if (!await run.RunAsync(page, answered: null, ct)) {
            AlreadyRunning(Log, pageId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Task {PageId} already has a run. No run starts.")]
    static partial void AlreadyRunning(ILogger logger, string pageId);
}
