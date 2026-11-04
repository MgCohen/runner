using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Runner.Host.Tests;

public sealed class SwitchFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "runner-switch-tests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_folder, "switches.json");

    [Fact]
    public void Missing_file_gives_the_defaults()
    {
        var switches = RunnerSwitches.Load(FilePath);

        Assert.True(switches.ListenerEnabled);
        Assert.All(RunnerSwitches.KnownSources, s => Assert.True(switches.IsSourceEnabled(s)));
        Assert.True(switches.IsHandlerEnabled("TaskTodoStarted"));
        Assert.True(switches.IsHandlerEnabled("LogEverything"));
        Assert.False(switches.IsHandlerEnabled("NotionPageUpdated"));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Changes_are_saved_and_load_again()
    {
        var switches = RunnerSwitches.Load(FilePath);
        var changes = 0;
        switches.Changed += () => changes++;

        switches.SetListener(false);
        switches.SetSource("github", false);
        switches.SetHandler("NotionPageUpdated", true);
        switches.SetHandler("TaskTodoStarted", false);
        switches.SetHandler("TaskTodoStarted", false);

        Assert.Equal(4, changes);
        var again = RunnerSwitches.Load(FilePath);
        Assert.False(again.ListenerEnabled);
        Assert.False(again.IsSourceEnabled("github"));
        Assert.True(again.IsSourceEnabled("notion"));
        Assert.True(again.IsHandlerEnabled("NotionPageUpdated"));
        Assert.False(again.IsHandlerEnabled("TaskTodoStarted"));

        using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
        Assert.False(doc.RootElement.GetProperty("listener").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("sources").GetProperty("github").GetBoolean());
    }

    [Fact]
    public void Bad_file_gives_the_defaults()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "{ not json");

        var switches = RunnerSwitches.Load(FilePath);

        Assert.True(switches.ListenerEnabled);
        Assert.False(switches.IsHandlerEnabled("NotionPageUpdated"));
    }

    [Fact]
    public void In_memory_switches_are_all_on_and_not_saved()
    {
        var switches = RunnerSwitches.InMemory();

        Assert.Null(switches.FilePath);
        Assert.True(switches.ListenerEnabled);
        Assert.True(switches.IsHandlerEnabled("NotionPageUpdated"));
        switches.SetSource("linear", false);
        Assert.False(switches.IsSourceEnabled("linear"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}

public sealed class TrafficTests
{
    private sealed class Stub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private sealed class Throws : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("no route");
    }

    [Fact]
    public async Task Handler_records_method_host_path_status_size_and_no_headers_or_query()
    {
        var log = new TrafficLog();
        using var client = new HttpClient(new TrafficHandler(log, TimeProvider.System) { InnerHandler = new Stub(HttpStatusCode.Accepted, "hello") });
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://user:pw-secret@api.example.com/v1/pages?token=query-secret#frag-secret")
        {
            Content = new StringContent("body-secret"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "header-secret");
        request.Headers.Add("X-Api-Key", "key-secret");

        using var response = await client.SendAsync(request);

        var entry = Assert.Single(log.Snapshot());
        Assert.Equal("out", entry.Direction);
        Assert.Equal("POST", entry.Kind);
        Assert.Equal("api.example.com/v1/pages", entry.Target);
        Assert.Equal("202", entry.Status);
        Assert.Equal(5, entry.Size);
        Assert.NotNull(entry.Duration);
        Assert.DoesNotContain("secret", entry.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handler_records_a_failed_call_and_throws()
    {
        var log = new TrafficLog();
        using var client = new HttpClient(new TrafficHandler(log, TimeProvider.System) { InnerHandler = new Throws() });

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://api.example.com/x?a=1"));

        var entry = Assert.Single(log.Snapshot());
        Assert.Equal("error", entry.Status);
        Assert.Equal("api.example.com/x", entry.Target);
    }

    [Fact]
    public void Log_keeps_the_last_500_entries_newest_first_and_raises_an_event()
    {
        var log = new TrafficLog();
        var added = 0;
        log.EntryAdded += _ => added++;

        for (var i = 0; i < 600; i++)
        {
            log.Add(new TrafficEntry(DateTimeOffset.UtcNow, "out", "GET", $"host/{i}", "200", null, null));
        }

        var entries = log.Snapshot();
        Assert.Equal(TrafficLog.Capacity, entries.Count);
        Assert.Equal("host/599", entries[0].Target);
        Assert.Equal("host/100", entries[^1].Target);
        Assert.Equal(600, added);
    }
}
