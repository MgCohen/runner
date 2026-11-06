using System.Text.Json;
using Handling.Slices.Handling;
using Microsoft.Extensions.Logging;

namespace Diagnostics.Slices.LogEverything;

// Writes one log line for each Notion event. Use it as an example of a handler.
sealed partial class LogEverything(HandlerContext context) : InboxHandler(context) {
    public override HandlerConfig Config { get; } = new() {
        Sources = ["notion"],
        Description = "One log line for each event",
    };

    protected override Task Run(InboxEvent e, CancellationToken ct) {
        var entity = e.Payload.ValueKind == JsonValueKind.Object && e.Payload.TryGetProperty("entity", out var x) && x.TryGetProperty("id", out var id)
            ? id.GetString() : null;
        Logged(Log, e.Id, e.EventType, e.Action, entity);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Notion event {Id}: {Type}.{Action}, entity {Entity}")]
    static partial void Logged(ILogger logger, long id, string? type, string? action, string? entity);
}
