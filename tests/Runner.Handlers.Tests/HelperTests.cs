using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Runner.Abstractions;
using Runner.Handlers.Claude;
using Runner.Handlers.Notion;

namespace Runner.Handlers.Tests;

public class ClaudeLauncherTests
{
    [Fact]
    public void Print_arguments_use_the_permission_mode() =>
        Assert.Equal(["-p", "--permission-mode", "auto"], ClaudeLauncher.PrintArguments(new ClaudeOptions()));

    [Fact]
    public void Background_arguments_start_a_remote_control_session() =>
        Assert.Equal(
            ["--bg", "--remote-control", "notion Plan", "--permission-mode", "plan", "--session-id", "id-1", "--", "-do it"],
            ClaudeLauncher.BackgroundArguments(new ClaudeOptions { PermissionMode = "plan" }, "notion Plan", "id-1", "-do it"));

    [Theory]
    [InlineData("bypassPermissions")]
    [InlineData("AUTO")]
    [InlineData("")]
    public void Unknown_or_bypass_mode_is_refused(string mode) =>
        Assert.Throws<InvalidOperationException>(() =>
            ClaudeLauncher.PrintArguments(new ClaudeOptions { PermissionMode = mode }));
}

public class NotionClientTests
{
    private const string PageJson = """
        {
          "object": "page",
          "id": "p1",
          "url": "https://www.notion.so/p1",
          "parent": { "type": "data_source_id", "data_source_id": "ds1", "database_id": "db1" },
          "properties": {
            "Status": { "type": "status", "status": { "name": "Done" } },
            "Name": { "type": "title", "title": [ { "plain_text": "Plan " }, { "plain_text": "the trip" } ] }
          }
        }
        """;

    private static NotionClient Client(FakeHttpHandler handler, string? token = "secret_test") =>
        new(new HttpClient(handler), Options.Create(new NotionOptions { Token = token }));

    [Fact]
    public async Task GetPage_sends_the_headers_and_reads_the_page()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, PageJson);

        var page = await Client(handler).GetPageAsync("p1", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.notion.com/v1/pages/p1", request.RequestUri!.ToString());
        Assert.Equal("Bearer secret_test", request.Headers.Authorization!.ToString());
        Assert.Equal("2026-03-11", Assert.Single(request.Headers.GetValues("Notion-Version")));
        Assert.Equal("Plan the trip", page.Title);
        Assert.Equal("https://www.notion.so/p1", page.Url);
        Assert.Equal("ds1", page.ParentDataSourceId);
        Assert.Equal("db1", page.ParentDatabaseId);
    }

    [Fact]
    public async Task GetPageMarkdown_reads_the_markdown_field()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, """{"object":"page_markdown","markdown":"# Hi"}""");

        var markdown = await Client(handler).GetPageMarkdownAsync("p1", CancellationToken.None);

        Assert.Equal("# Hi", markdown);
        Assert.Equal("https://api.notion.com/v1/pages/p1/markdown", handler.Requests[0].RequestUri!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Client_errors_are_permanent(HttpStatusCode status) =>
        await Assert.ThrowsAsync<PermanentFailureException>(() =>
            Client(new FakeHttpHandler(status, "{}")).GetPageAsync("p1", CancellationToken.None));

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Server_errors_can_retry(HttpStatusCode status) =>
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Client(new FakeHttpHandler(status, "{}")).GetPageAsync("p1", CancellationToken.None));

    [Fact]
    public async Task Missing_token_is_permanent_and_sends_nothing()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, PageJson);

        await Assert.ThrowsAsync<PermanentFailureException>(() =>
            Client(handler, token: null).GetPageAsync("p1", CancellationToken.None));
        Assert.Empty(handler.Requests);
    }
}

public class AddHandlersTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddHandlers(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Resolves_each_handler()
    {
        using var provider = Build([]);

        foreach (var type in HandlersServiceCollectionExtensions.HandlerTypes())
        {
            Assert.IsAssignableFrom<IEventHandler>(provider.GetRequiredService(type));
        }
        Assert.False(provider.GetRequiredService<IOptions<NotionPageUpdatedOptions>>().Value.Enabled);
        Assert.Equal("auto", provider.GetRequiredService<IOptions<ClaudeOptions>>().Value.PermissionMode);
    }

    [Fact]
    public void Token_falls_back_to_NOTION_API_TOKEN()
    {
        using var provider = Build(new() { ["NOTION_API_TOKEN"] = "from-env" });

        Assert.Equal("from-env", provider.GetRequiredService<IOptions<NotionOptions>>().Value.Token);
    }

    [Fact]
    public void Notion_Token_wins_over_NOTION_API_TOKEN()
    {
        using var provider = Build(new() { ["Notion:Token"] = "from-secrets", ["NOTION_API_TOKEN"] = "from-env" });

        Assert.Equal("from-secrets", provider.GetRequiredService<IOptions<NotionOptions>>().Value.Token);
    }

    [Fact]
    public void Bypass_permission_mode_fails_validation()
    {
        using var provider = Build(new() { ["Claude:PermissionMode"] = "bypassPermissions" });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ClaudeOptions>>().Value);
    }

    [Fact]
    public void Binds_the_handler_filter()
    {
        using var provider = Build(new()
        {
            ["Handlers:NotionPageUpdated:Enabled"] = "true",
            ["Handlers:NotionPageUpdated:DataSourceIds:0"] = "ds1",
            ["Handlers:NotionPageUpdated:Mode"] = "Print",
        });

        var options = provider.GetRequiredService<IOptions<NotionPageUpdatedOptions>>().Value;
        Assert.True(options.Enabled);
        Assert.Equal(["ds1"], options.DataSourceIds);
        Assert.Equal(ClaudeMode.Print, options.Mode);
    }
}
