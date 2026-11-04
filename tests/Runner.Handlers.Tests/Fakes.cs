using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Runner.Abstractions;
using Runner.Handlers.Claude;
using Runner.Handlers.Notion;

namespace Runner.Handlers.Tests;

internal static class Events
{
    public static InboxEvent Notion(string action, string json, string eventType = "page", long id = 1) => new()
    {
        Id = id,
        Source = "notion",
        DeliveryId = Guid.NewGuid().ToString(),
        EventType = eventType,
        Action = action,
        ReceivedAt = DateTimeOffset.UtcNow,
        Payload = JsonDocument.Parse(json).RootElement.Clone(),
    };

    public static InboxEvent Other(string source, string? eventType, string? action) => new()
    {
        Id = 1,
        Source = source,
        DeliveryId = "d",
        EventType = eventType,
        Action = action,
        ReceivedAt = DateTimeOffset.UtcNow,
        Payload = JsonDocument.Parse("{}").RootElement.Clone(),
    };

    public static string PageBody(string pageId = "11111111-2222-3333-4444-555555555555", string authorType = "person") =>
        $$"""
        {
          "type": "page.properties_updated",
          "entity": { "id": "{{pageId}}", "type": "page" },
          "authors": [ { "id": "u1", "type": "{{authorType}}" } ]
        }
        """;

    public static HandlerContext Context(int attempt = 1) =>
        new(attempt, NullLogger.Instance, new EmptyServices(), CancellationToken.None);

    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}

internal sealed class FakeNotionClient : INotionClient
{
    public NotionPage Page { get; set; } = new(
        "11111111-2222-3333-4444-555555555555", "Plan the trip", "https://www.notion.so/Plan-the-trip-1111",
        "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", "99999999-8888-7777-6666-555555555555",
        JsonDocument.Parse("{}").RootElement.Clone());

    public string Markdown { get; set; } = "# Plan the trip\n\nBook the hotel.";
    public Exception? MarkdownError { get; set; }
    public List<string> PageCalls { get; } = [];

    public Task<NotionPage> GetPageAsync(string pageId, CancellationToken cancellationToken)
    {
        PageCalls.Add(pageId);
        return Task.FromResult(Page);
    }

    public Task<string> GetPageMarkdownAsync(string pageId, CancellationToken cancellationToken) =>
        MarkdownError is null ? Task.FromResult(Markdown) : Task.FromException<string>(MarkdownError);
}

internal sealed class FakeClaudeLauncher : IClaudeLauncher
{
    public List<ClaudeRequest> Print { get; } = [];
    public List<ClaudeRequest> Remote { get; } = [];
    public ClaudeRunResult PrintResult { get; set; } = new(0, "done", "");

    public Task<ClaudeRunResult> RunPrintAsync(ClaudeRequest request, CancellationToken cancellationToken)
    {
        Print.Add(request);
        return Task.FromResult(PrintResult);
    }

    public Task<ClaudeSession> StartRemoteSessionAsync(ClaudeRequest request, CancellationToken cancellationToken)
    {
        Remote.Add(request);
        return Task.FromResult(new ClaudeSession("session-1", request.Name ?? "", ""));
    }
}

internal sealed class FakeTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class FakeHttpHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
