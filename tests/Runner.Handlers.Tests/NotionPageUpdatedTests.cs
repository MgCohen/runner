using Microsoft.Extensions.Options;
using Runner.Abstractions;
using Runner.Handlers.Claude;

namespace Runner.Handlers.Tests;

public class NotionPageUpdatedTests
{
    private readonly FakeNotionClient _notion = new();
    private readonly FakeClaudeLauncher _claude = new();
    private readonly FakeTime _time = new();
    private readonly PageCooldown _cooldown;
    private readonly NotionPageUpdatedOptions _options = new() { Enabled = true };

    public NotionPageUpdatedTests() => _cooldown = new PageCooldown(_time);

    private NotionPageUpdated Handler() => new(_notion, _claude, _cooldown, Options.Create(_options));

    [Fact]
    public async Task Off_by_default_does_nothing()
    {
        var handler = new NotionPageUpdated(_notion, _claude, _cooldown, Options.Create(new NotionPageUpdatedOptions()));

        await handler.HandleAsync(Events.Notion("properties_updated", Events.PageBody()), Events.Context());

        Assert.Empty(_notion.PageCalls);
        Assert.Empty(_claude.Remote);
    }

    [Fact]
    public async Task Bot_author_does_not_start_a_session()
    {
        await Handler().HandleAsync(Events.Notion("content_updated", Events.PageBody(authorType: "bot")), Events.Context());

        Assert.Empty(_notion.PageCalls);
        Assert.Empty(_claude.Remote);
    }

    [Fact]
    public async Task Person_edit_starts_a_background_session_with_the_page_context()
    {
        _options.WorkingDirectory = @"C:\work";

        await Handler().HandleAsync(Events.Notion("properties_updated", Events.PageBody()), Events.Context());

        Assert.Equal(["11111111-2222-3333-4444-555555555555"], _notion.PageCalls);
        var request = Assert.Single(_claude.Remote);
        Assert.Empty(_claude.Print);
        Assert.Contains("Title: Plan the trip", request.Prompt);
        Assert.Contains("URL: https://www.notion.so/Plan-the-trip-1111", request.Prompt);
        Assert.Contains("Book the hotel.", request.Prompt);
        Assert.StartsWith(_options.Instructions, request.Prompt);
        Assert.Equal(@"C:\work", request.WorkingDirectory);
        Assert.Equal("notion Plan the trip", request.Name);
    }

    [Fact]
    public async Task Print_mode_runs_claude_p()
    {
        _options.Mode = ClaudeMode.Print;

        await Handler().HandleAsync(Events.Notion("properties_updated", Events.PageBody()), Events.Context());

        Assert.Single(_claude.Print);
        Assert.Empty(_claude.Remote);
    }

    [Fact]
    public async Task Print_mode_failure_throws_for_a_retry()
    {
        _options.Mode = ClaudeMode.Print;
        _claude.PrintResult = new ClaudeRunResult(1, "", "boom");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler().HandleAsync(Events.Notion("properties_updated", Events.PageBody()), Events.Context()));
    }

    [Fact]
    public async Task Page_outside_the_data_sources_does_not_start_a_session()
    {
        _options.DataSourceIds = ["00000000-0000-0000-0000-000000000000"];

        await Handler().HandleAsync(Events.Notion("properties_updated", Events.PageBody()), Events.Context());

        Assert.Empty(_claude.Remote);
    }

    [Theory]
    [InlineData("AAAAAAAABBBBCCCCDDDDEEEEEEEEEEEE")]
    [InlineData("99999999-8888-7777-6666-555555555555")]
    public async Task Page_in_a_data_source_or_database_starts_a_session(string id)
    {
        _options.DataSourceIds = [id];

        await Handler().HandleAsync(Events.Notion("properties_updated", Events.PageBody()), Events.Context());

        Assert.Single(_claude.Remote);
    }

    [Fact]
    public async Task Two_events_for_one_page_start_one_session_in_the_cooldown()
    {
        await Handler().HandleAsync(Events.Notion("properties_updated", Events.PageBody(), id: 1), Events.Context());
        await Handler().HandleAsync(Events.Notion("content_updated", Events.PageBody(), id: 2), Events.Context());
        Assert.Single(_claude.Remote);

        _time.Now += _options.Cooldown;
        await Handler().HandleAsync(Events.Notion("content_updated", Events.PageBody(), id: 3), Events.Context());
        Assert.Equal(2, _claude.Remote.Count);
    }

    [Fact]
    public async Task Failure_releases_the_cooldown_so_a_retry_can_start_a_session()
    {
        _notion.MarkdownError = new HttpRequestException("down");
        var e = Events.Notion("properties_updated", Events.PageBody());

        await Assert.ThrowsAsync<HttpRequestException>(() => Handler().HandleAsync(e, Events.Context(1)));

        _notion.MarkdownError = null;
        await Handler().HandleAsync(e, Events.Context(2));
        Assert.Single(_claude.Remote);
    }

    [Fact]
    public async Task Event_without_entity_id_is_a_permanent_failure() =>
        await Assert.ThrowsAsync<PermanentFailureException>(() =>
            Handler().HandleAsync(Events.Notion("properties_updated", """{"authors":[]}"""), Events.Context()));

    [Fact]
    public async Task Long_markdown_is_cut()
    {
        _options.MaxMarkdownChars = 10;
        _notion.Markdown = new string('x', 100);

        await Handler().HandleAsync(Events.Notion("properties_updated", Events.PageBody()), Events.Context());

        var prompt = Assert.Single(_claude.Remote).Prompt;
        Assert.DoesNotContain(new string('x', 11), prompt);
        Assert.Contains("[The page continues. Read it at the URL.]", prompt);
    }
}
