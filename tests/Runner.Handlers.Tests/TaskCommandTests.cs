using System.Text.Json;
using System.Text.Json.Nodes;
using Runner.Handlers.Notion;
using Runner.Handlers.Tasks;

namespace Runner.Handlers.Tests;

public sealed class TaskCommandTests
{
    private readonly FakeNotionClient _notion = new();
    private readonly StringWriter _output = new();
    private readonly List<(string Prompt, string? Folder)> _runs = [];
    private ClaudeOutput _claudeResult = new(0, "OK, the task flow works.");

    private static NotionPage Task(string title, string folder = "") => NotionPage.FromJson(JsonDocument.Parse($$"""
        {
          "id": "{{Guid.NewGuid()}}",
          "url": "https://www.notion.so/x",
          "properties": {
            "Name": { "type": "title", "title": [ { "plain_text": "{{title}}" } ] },
            "Status": { "type": "status", "status": { "name": "Todo" } },
            "Folder": { "type": "rich_text", "rich_text": [ { "plain_text": "{{folder}}" } ] }
          }
        }
        """).RootElement);

    private TaskCommand Command(Func<string, bool>? confirm = null) => new(
        _notion,
        (prompt, folder, _) => { _runs.Add((prompt, folder)); return System.Threading.Tasks.Task.FromResult(_claudeResult); },
        confirm ?? (_ => true), _output, new TaskOptions(), new FakeTime(), "C:/Projects");

    private static string? Status(JsonObject properties) => properties["Status"]?["status"]?["name"]?.GetValue<string>();

    [Fact]
    public async Task Confirmed_task_goes_through_in_progress_to_in_review()
    {
        _notion.QueryResult.Add(Task("Test: reply OK"));

        var done = await Command().RunAsync(yes: false, listOnly: false, CancellationToken.None);

        Assert.Equal(1, done);
        Assert.Equal("In Progress", Status(_notion.Updates[0].Properties));
        Assert.Equal("In Review", Status(_notion.Updates[1].Properties));
        Assert.Contains("the task flow works", _notion.Updates[1].Properties["Result"]!.ToJsonString());
        Assert.Equal("C:/Projects", Assert.Single(_runs).Folder);
        Assert.Contains("Task: Test: reply OK", _runs[0].Prompt);
    }

    [Fact]
    public async Task Declined_task_stays_in_todo()
    {
        _notion.QueryResult.Add(Task("A"));
        var done = await Command(_ => false).RunAsync(yes: false, listOnly: false, CancellationToken.None);
        Assert.Equal(0, done);
        Assert.Empty(_notion.Updates);
        Assert.Empty(_runs);
    }

    [Fact]
    public async Task List_only_changes_nothing()
    {
        _notion.QueryResult.Add(Task("A"));
        await Command().RunAsync(yes: false, listOnly: true, CancellationToken.None);
        Assert.Empty(_notion.Updates);
        Assert.Contains("1 task(s) in Todo", _output.ToString());
    }

    [Fact]
    public async Task Failed_claude_run_moves_the_task_back()
    {
        _notion.QueryResult.Add(Task("A"));
        _claudeResult = new ClaudeOutput(1, "error text");
        var done = await Command().RunAsync(yes: true, listOnly: false, CancellationToken.None);
        Assert.Equal(0, done);
        Assert.Equal("Backlog", Status(_notion.Updates[^1].Properties));
        Assert.Contains("exit 1", _notion.Updates[^1].Properties["Result"]!.ToJsonString());
    }

    [Fact]
    public async Task Folder_property_overrides_the_default_folder()
    {
        _notion.QueryResult.Add(Task("A", folder: "C:/Work/repo"));
        await Command().RunAsync(yes: true, listOnly: false, CancellationToken.None);
        Assert.Equal("C:/Work/repo", Assert.Single(_runs).Folder);
    }
}
