using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Runner.Abstractions;
using Runner.Handlers.Claude;
using Runner.Handlers.Notion;
using Runner.Handlers.Tasks;
using Runner.Host;

namespace Runner.Tray;

/// <summary>
/// Mock mode (<c>--mock</c>): fake inbox and Notion HTTP answers, and a fake Claude.
/// Nothing leaves this computer and no <c>claude</c> process starts. Use the tray menu to send mock events.
/// </summary>
internal static class MockMode
{
    /// <summary>The ID of the mock task page.</summary>
    public const string TaskPageId = "00000000-0000-4000-8000-0000000000aa";

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Runner", "mock");

    /// <summary>Settings for mock mode. Call before <c>AddRunnerHost</c>.</summary>
    public static void Configure(IConfigurationManager configuration)
    {
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Inbox:Token"] = "mock-token",
            ["Inbox:BaseUrl"] = "https://inbox.mock.invalid",
            ["Inbox:Consumer"] = "mock",
            ["Inbox:StorePath"] = Path.Combine(Folder, "runner-host.db"),
            ["Notion:Token"] = "mock-token",
            ["Notion:BaseUrl"] = "https://notion.mock.invalid/",
            ["Handlers:Tasks:Mode"] = "Print",
            ["Handlers:Tasks:StatePath"] = Path.Combine(Folder, "tasks.json"),
        });
    }

    /// <summary>Replaces the network and Claude. Call after <c>AddRunnerHost</c> and <c>AddHandlers</c>.</summary>
    public static void AddMocks(IServiceCollection services)
    {
        var notion = new MockNotion();
        services.AddSingleton(notion);
        services.AddHttpClient<InboxClient>().ConfigurePrimaryHttpMessageHandler(() => new MockInboxHandler());
        services.AddHttpClient<INotionClient, NotionClient>().ConfigurePrimaryHttpMessageHandler(() => new MockNotionHandler(notion));
        services.RemoveAll<IClaudePrint>();
        services.AddSingleton<IClaudePrint, MockClaudePrint>();
        services.AddSingleton<MockEvents>();
    }
}

/// <summary>Adds mock events to the local store and wakes the dispatcher.</summary>
internal sealed class MockEvents(EventStore store, DispatchSignal signal, MockNotion notion)
{
    private long _next = 9_000_000 + DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 100_000 * 10;

    /// <summary>A Notion task edit. The mock task is set to Todo first, so the Tasks handler runs.</summary>
    public long SendTaskEvent()
    {
        notion.Status = "Todo";
        return Send("notion", "page", "properties_updated",
            $$"""{"type":"page.properties_updated","entity":{"id":"{{MockMode.TaskPageId}}","type":"page"},"authors":[{"id":"mock-user","type":"person"}]}""");
    }

    /// <summary>A GitHub push. No handler matches it, so it is only acked.</summary>
    public long SendGitHubEvent() => Send("github", "push", null, """{"ref":"refs/heads/main","repository":{"full_name":"mock/repo"}}""");

    private long Send(string source, string type, string? action, string payload)
    {
        var id = Interlocked.Increment(ref _next);
        store.TryAdd(new InboxEvent
        {
            Id = id,
            Source = source,
            DeliveryId = Guid.NewGuid().ToString(),
            EventType = type,
            Action = action,
            ReceivedAt = DateTimeOffset.UtcNow,
            Payload = JsonDocument.Parse(payload).RootElement.Clone(),
        });
        signal.Set();
        return id;
    }
}

/// <summary>The state of the one mock task page.</summary>
internal sealed class MockNotion
{
    public volatile string Status = "Backlog";
    public volatile string Result = "";
}

/// <summary>Answers the inbox API: no pending events, and each ack succeeds.</summary>
internal sealed class MockInboxHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.RequestUri!.AbsolutePath.StartsWith("/api/events", StringComparison.Ordinal)
            ? """{"events":[],"next_after":null}"""
            : """{"consumed":[],"already":[],"unknown":[]}""";
        return Task.FromResult(MockHttp.Json(body));
    }
}

/// <summary>Answers the Notion API for the one mock task page, and keeps its status.</summary>
internal sealed class MockNotionHandler(MockNotion state) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Patch && request.Content is not null)
        {
            var body = JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            if (body?["properties"]?["Status"]?["status"]?["name"]?.GetValue<string>() is { } status) state.Status = status;
            if (body?["properties"]?["Result"]?["rich_text"] is JsonArray text)
                state.Result = string.Concat(text.Select(t => t?["text"]?["content"]?.GetValue<string>()));
            return MockHttp.Json("{}");
        }
        if (path.EndsWith("/markdown", StringComparison.Ordinal))
        {
            return MockHttp.Json("""{"object":"page_markdown","markdown":"Mock task. Reply with OK."}""");
        }
        if (path.Contains("/query", StringComparison.Ordinal))
        {
            return MockHttp.Json("""{"results":[],"has_more":false}""");
        }
        var page = $$"""
            {"object":"page","id":"{{MockMode.TaskPageId}}","url":"https://www.notion.so/mock-task",
             "parent":{"type":"data_source_id","data_source_id":"{{new TaskOptions().DataSourceId}}"},
             "properties":{
               "Name":{"type":"title","title":[{"plain_text":"Mock task"}]},
               "Status":{"type":"status","status":{"name":"{{state.Status}}"} },
               "Folder":{"type":"rich_text","rich_text":[]} } }
            """;
        return MockHttp.Json(page);
    }
}

/// <summary>Writes what <c>claude -p</c> would run, and returns a fixed answer. No process starts.</summary>
internal sealed class MockClaudePrint(IActivitySink sink) : IClaudePrint
{
    public async Task<ClaudeOutput> RunAsync(string prompt, string? workingDirectory, CancellationToken cancellationToken)
    {
        sink.Write($"> [mock] claude -p   (folder: {workingDirectory ?? Environment.CurrentDirectory}, prompt: {prompt.Length} chars)");
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        const string answer = "OK, the mock task flow works.";
        sink.Write(answer);
        return new ClaudeOutput(0, answer);
    }
}

internal static class MockHttp
{
    public static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
