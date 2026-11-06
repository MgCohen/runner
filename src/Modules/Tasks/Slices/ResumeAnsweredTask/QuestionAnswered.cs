using System.Text.Json.Nodes;
using Handling.Domain;
using Handling.Slices.Handling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notion.Domain;
using Notion.Infrastructure;
using Tasks.Domain;
using Tasks.Infrastructure;

namespace Tasks.Slices.ResumeAnsweredTask;

// Runs a waiting task again when its blocking question gets the status Answered. The new run gets the question and
// the answer in its prompt (TaskRun). When a run finishes, the question goes to Closed.
// A Notion event does not give the old status, so the handler checks the pages: only a task in Waiting with no other
// open question runs. Bot edits count too, because a tool can write the answer through the connection. The handler
// changes only a question in Answered and a task in Waiting, so its own edits start nothing.
sealed partial class QuestionAnswered(HandlerContext context, INotionClient notion, TaskRun run, IOptions<TaskOptions> options) : InboxHandler(context) {
    readonly TaskOptions o = options.Value;

    public override HandlerConfig Config { get; } = new() {
        Sources = ["notion"],
        Matches = [new Match("page", "properties_updated")],
        Description = "Runs a waiting task again when its question is answered",
        Timeout = TimeSpan.FromMinutes(30),
    };

    protected override async Task Run(InboxEvent e, CancellationToken ct) {
        var pageId = NotionWebhook.EntityId(e.Payload) ?? throw new PermanentFailureException($"Event {e.Id} has no entity.id.");
        var question = await notion.GetPageAsync(pageId, ct);
        if (!question.IsIn(o.QuestionsDataSourceId) || question.StatusOf(o.StatusProperty) != o.AnsweredQuestionStatus) {
            return;
        }
        var ran = false;
        foreach (var taskId in question.RelationOf(o.BlocksProperty)) {
            var task = await notion.GetPageAsync(taskId, ct);
            if (!task.IsIn(o.DataSourceId) || task.StatusOf(o.StatusProperty) != o.WaitingStatus) {
                continue;
            }
            if (await run.HasOpenQuestionAsync(task, ct)) {
                StillWaiting(Log, taskId);
                continue;
            }
            if (await run.RunAsync(task, question, ct)) {
                ran = true;
            } else {
                AlreadyRunning(Log, taskId);
            }
        }
        if (ran) {
            await notion.UpdatePageAsync(question.Id, new JsonObject { [o.StatusProperty] = NotionProperties.Status(o.ClosedQuestionStatus) }, ct);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Task {PageId} has another open question. It stays in Waiting.")]
    static partial void StillWaiting(ILogger logger, string pageId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Task {PageId} already has a run. No run starts.")]
    static partial void AlreadyRunning(ILogger logger, string pageId);
}
