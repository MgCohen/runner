using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Runner.Abstractions;

namespace Runner.Host.Tests;

/// <summary>A connector host that uses a <see cref="FakeInbox"/>, a temporary SQLite file and short delays.</summary>
public sealed class TestHost : IAsyncDisposable
{
    public const string Consumer = "test-runner";

    private readonly string _folder;
    private readonly bool _ownsFolder;

    private TestHost(IHost host, string folder, bool ownsFolder)
    {
        Host = host;
        _folder = folder;
        _ownsFolder = ownsFolder;
    }

    public IHost Host { get; }
    public Recorder Recorder => Host.Services.GetRequiredService<Recorder>();
    public EventStore Store => Host.Services.GetRequiredService<EventStore>();
    public SocketListener Socket => Host.Services.GetRequiredService<SocketListener>();
    public RunnerSwitches Switches => Host.Services.GetRequiredService<RunnerSwitches>();
    public TrafficLog Traffic => Host.Services.GetRequiredService<TrafficLog>();
    public Dispatcher Dispatcher => Host.Services.GetServices<IHostedService>().OfType<Dispatcher>().Single();

    public static async Task<TestHost> StartAsync(FakeInbox inbox, string? folder = null, RunnerSwitches? switches = null)
    {
        var ownsFolder = folder is null;
        folder ??= Path.Combine(Path.GetTempPath(), "runner-host-tests", Guid.NewGuid().ToString("N"));
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Inbox:BaseUrl"] = inbox.BaseUrl.ToString(),
            ["Inbox:Token"] = FakeInbox.Token,
            ["Inbox:Consumer"] = Consumer,
            ["Inbox:StorePath"] = Path.Combine(folder, "store.db"),
            ["Inbox:CatchUpInterval"] = "01:00:00",
            ["Inbox:RetryBaseDelay"] = "00:00:00.050",
            ["Inbox:RetryMaxDelay"] = "00:00:00.200",
            ["Inbox:ReconnectBaseDelay"] = "00:00:00.050",
            ["Inbox:ReconnectMaxDelay"] = "00:00:00.200",
            ["Inbox:AckBaseDelay"] = "00:00:00.050",
            ["Inbox:AckMaxDelay"] = "00:00:00.200",
        });
        if (switches is not null) builder.Services.AddSingleton(switches);
        builder.Services.AddSingleton<IActivitySink, NullActivitySink>();
        builder.Services.AddRunnerHost(builder.Configuration);
        builder.Services.AddHandlerAssembly(typeof(TestHost).Assembly);
        builder.Services.AddSingleton<Recorder>();

        var host = builder.Build();
        await host.StartAsync();
        return new TestHost(host, folder, ownsFolder);
    }

    public static InboxEvent Event(long id, string eventType, string source = "test") => new()
    {
        Id = id,
        Source = source,
        DeliveryId = $"delivery-{id}",
        EventType = eventType,
        Action = null,
        ReceivedAt = DateTimeOffset.UtcNow,
        Payload = JsonDocument.Parse("""{"raw":"body"}""").RootElement.Clone(),
    };

    /// <summary>Waits until the condition is true. Fails the test after the timeout.</summary>
    public static async Task WaitUntil(Func<bool> condition, string what, int timeoutMs = 10_000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > end) Assert.Fail($"Timeout: {what}");
            await Task.Delay(20);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Host.StopAsync();
        Host.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (!_ownsFolder) return;
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Keeps the activity lines out of the test output.</summary>
public sealed class NullActivitySink : IActivitySink
{
    public void Write(string line)
    {
    }
}
