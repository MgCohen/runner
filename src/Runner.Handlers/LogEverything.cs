using Microsoft.Extensions.Logging;
using Runner.Abstractions;

namespace Runner.Handlers;

/// <summary>Writes one log line for each Notion event. Use it as an example for a handler.</summary>
[On("notion")]
public sealed class LogEverything : IEventHandler
{
    public Task HandleAsync(InboxEvent e, HandlerContext context)
    {
        context.Logger.LogInformation(
            "Notion event {Id}: {EventType}.{Action}, entity {EntityId}, attempt {Attempt}",
            e.Id, e.EventType, e.Action, Notion.NotionWebhook.EntityId(e.Payload), context.Attempt);
        return Task.CompletedTask;
    }
}
