using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Runner.Abstractions;
using Runner.Handlers.Claude;
using Runner.Handlers.Notion;
using Runner.Handlers.Tasks;

namespace Runner.Handlers.Tests;

public sealed class TaskTests : IDisposable
{
    private const string TasksSource = "f0db2108-3a1e-4e80-8f74-f081643e0a29";
    private const string PageId = "11111111-2222-3333-4444-555555555555";

    private readonly string _statePath = Path.Combine(Path.GetTempPath(), $"tasks-{Guid.NewGuid():N}.json");
    private readonly FakeNotionClient _notion = new();
    private readonly FakeClaudeLauncher _claude = new();
    private readonly FakeClaudeAgents _agents = new();
    private readonly FakeTime _time = new();
    private readonly TaskOptions _options;
    private readonly TaskStore _store;

    public TaskTests()
    {
        _options = new TaskOptions { StatePath = _statePath };
        _store = new TaskStore(Options.Create(_options));
        _notion.Page = TaskPage("Todo");
    }

    public void Dispose()
    {
        if (File.Exists(_statePath)) File.Delete(_statePath);
    }

    private static NotionPage TaskPage(string status, string dataSource = TasksSource, string folder = "") =>
        NotionPage.FromJson(JsonDocument.Parse($$"""
        {
          "id": "{{PageId}}",
          "url": "https://www.notion.so/Fix-it-1111",
          "parent": { "type": "data_source_id", "data_source_id": "{{dataSource}}" },
          "properties": {
            "Name": { "type": "title", "title": [ { "plain_text": "Fix it" } ] },
            "Status": { "type": "status", "status": { "name": "{{status}}" } },
            "Folder": { "type": "rich_text", "rich_text": [ { "plain_text": "{{folder}}" } ] }
          }
        }
        """).RootElement);

    private TaskTodoStarted Handler() => new(_notion, _claude, _agents, _store, new PageCooldown(_time), _time,
        Options.Create(_options), Options.Create(new ClaudeOptions { WorkingDirectory = @"C:\Projects" }))
    {
        LinkWait = TimeSpan.Zero,
    };

    private TaskSessionWatcher Watcher() =>
        new(_store, _agents, _notion, _time, Options.Create(_options), NullLogger<TaskSessionWatcher>.Instance);

    private static string? Status(System.Text.Json.Nodes.JsonObject properties) =>
        properties["Status"]?["status"]?["name"]?.GetValue<string>();

    [Fact]
    public async Task Todo_task_moves_to_in_progress_and_starts_a_session()
    {
        await Handler().HandleAsync(Events.Notion("properties_updated", Events.PageBody()), Events.Context());

        Assert.Equal("In Progress", Status(_notion.Updates[0].Properties));
        var request = Assert.Single(_claude.Remote);
        Assert.Equal(@"C:\Projects", request.WorkingDirectory);
        Assert.Contains("Task: Fix it", request.Prompt);
        Assert.Equal("task Fix it", request.Name);
        var tracked = Assert.Single(_store.All());
        Assert.Equal("session-1", tracked.SessionId);
        Assert.Equal("https://claude.ai/code/session_abc", _notion.Updates[^1].Properties["Session"]?["url"]?.GetValue<string>());
    }

    [Fact]
    public async Task Folder_property_sets_the_working_folder()
    {
        _notion.Page = TaskPage("Todo", folder: "C:/Work/repo");
        await Handler().HandleAsync(Events.Notion("properties_updated", Events.PageBody()), Events.Context());
        Assert.Equal("C:/Work/repo", Assert.Single(_claude.Remote).WorkingDirectory);
    }

    [Theory]
    [InlineData("Backlog")]
    [InlineData("In Progress")]
    [InlineData("In Review")]
    public async Task Other_status_starts_nothing(string status)
    {
        _notion.Page = TaskPage(status);
        await Handler().HandleAsync(Events.Notion("properties_updated", Events.PageBody()), Events.Context());
        Assert.Empty(_claude.Remote);
        Assert.Empty(_notion.Updates);
    }

    [Fact]
    public async Task Page_in_another_database_starts_nothing()
    {
        _notion.Page = TaskPage("Todo", dataSource: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        await Handler().HandleAsync(Events.Notion("properties_updated", Events.PageBody()), Events.Context());
        Assert.Empty(_claude.Remote);
    }

    [Fact]
    public async Task Bot_change_starts_nothing()
    {
        await Handler().HandleAsync(Events.Notion("properties_updated", Events.PageBody(authorType: "bot")), Events.Context());
        Assert.Empty(_notion.PageCalls);
        Assert.Empty(_claude.Remote);
    }

    [Fact]
    public async Task Second_event_for_a_running_task_starts_nothing()
    {
        var handler = Handler();
        await handler.HandleAsync(Events.Notion("properties_updated", Events.PageBody()), Events.Context());
        await handler.HandleAsync(Events.Notion("properties_updated", Events.PageBody(), id: 2), Events.Context());
        Assert.Single(_claude.Remote);
    }

    [Fact]
    public async Task Failed_start_moves_the_task_back_with_the_error()
    {
        _claude.StartError = new InvalidOperationException("Workspace not trusted");
        await Assert.ThrowsAsync<PermanentFailureException>(() =>
            Handler().HandleAsync(Events.Notion("properties_updated", Events.PageBody()), Events.Context()));

        var last = _notion.Updates[^1].Properties;
        Assert.Equal("Backlog", Status(last));
        Assert.Contains("Workspace not trusted", last["Result"]!.ToJsonString());
        Assert.Empty(_store.All());
    }

    [Fact]
    public async Task Finished_session_moves_the_task_to_in_review_with_the_answer()
    {
        _store.Upsert(new TrackedTask(PageId, "abc123", _time.Now, "https://claude.ai/code/session_abc"));
        _agents.Agents.Add(new ClaudeAgent("abc123", "abc123-full", "done", "idle", null));

        await Watcher().CheckOnceAsync(CancellationToken.None);

        var update = Assert.Single(_notion.Updates).Properties;
        Assert.Equal("In Review", Status(update));
        Assert.Contains("opened PR #1", update["Result"]!.ToJsonString());
        Assert.Empty(_store.All());
    }

    [Fact]
    public async Task Working_session_stays_in_progress()
    {
        _store.Upsert(new TrackedTask(PageId, "abc123", _time.Now, "https://claude.ai/code/session_abc"));
        _agents.Agents.Add(new ClaudeAgent("abc123", "abc123-full", "working", "busy", null));

        await Watcher().CheckOnceAsync(CancellationToken.None);

        Assert.Empty(_notion.Updates);
        Assert.Single(_store.All());
    }

    [Theory]
    [InlineData(null, "session not found")]
    [InlineData("blocked", "blocked")]
    public async Task Stopped_or_missing_session_goes_to_review_with_the_reason(string? state, string reason)
    {
        _store.Upsert(new TrackedTask(PageId, "abc123", _time.Now, "https://claude.ai/code/session_abc"));
        if (state is not null) _agents.Agents.Add(new ClaudeAgent("abc123", "abc123-full", state, null, null));

        await Watcher().CheckOnceAsync(CancellationToken.None);

        var update = Assert.Single(_notion.Updates).Properties;
        Assert.Equal("In Review", Status(update));
        Assert.Contains(reason, update["Result"]!.ToJsonString());
    }

    [Fact]
    public async Task Session_that_runs_too_long_goes_to_review()
    {
        _store.Upsert(new TrackedTask(PageId, "abc123", _time.Now, "x"));
        _agents.Agents.Add(new ClaudeAgent("abc123", "abc123-full", "working", "busy", null));
        _time.Now += _options.MaxRunTime + TimeSpan.FromMinutes(1);

        await Watcher().CheckOnceAsync(CancellationToken.None);

        Assert.Equal("In Review", Status(Assert.Single(_notion.Updates).Properties));
    }

    [Fact]
    public void Agents_list_is_parsed()
    {
        var list = ClaudeAgents.Parse("""
            [ { "id": "407bdf87", "kind": "background", "sessionId": "407bdf87-d61f", "state": "blocked" },
              { "pid": 1, "id": "a87362a1", "kind": "interactive", "status": "busy" } ]
            """);
        Assert.Equal(2, list.Count);
        Assert.Equal("blocked", list[0].State);
        Assert.Equal("407bdf87-d61f", list[0].SessionId);
        Assert.Null(list[1].State);
    }

    [Fact]
    public void Last_answer_is_the_last_assistant_text()
    {
        string[] lines =
        [
            """{"type":"user","message":{"content":"hi"}}""",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"First"}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash"}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"Summary: PR #2"}]}}""",
            """{"type":"assistant","message":""",
        ];
        Assert.Equal("Summary: PR #2", ClaudeAgents.LastAnswer(lines));
    }

    [Fact]
    public void Long_text_is_cut_to_the_notion_limit()
    {
        var node = NotionProperties.Text(new string('a', 3000));
        var content = node["rich_text"]![0]!["text"]!["content"]!.GetValue<string>();
        Assert.Equal(NotionProperties.MaxTextLength, content.Length);
    }
}
