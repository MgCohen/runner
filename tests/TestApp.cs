using System.Diagnostics;
using App.Host;
using Handling.Domain;
using Handling.Infrastructure;
using Listener.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using MockHandlers.Infrastructure;
using VSA.Presenters;

namespace App.Tests;

// The real app in mock mode, with no window, in its own data folder. Kestrel listens on 127.0.0.1 port 0.
public sealed class TestApp : IAsyncDisposable {
    readonly bool ownsFolder;

    TestApp(WebApplication app, string folder, bool ownsFolder) {
        App = app;
        Folder = folder;
        this.ownsFolder = ownsFolder;
    }

    public WebApplication App { get; }

    public string Folder { get; }

    public IServiceProvider Services => App.Services;

    public IBus Bus => Services.GetRequiredService<IBus>();

    public HandlingStore Store => Services.GetRequiredService<HandlingStore>();

    public MockInbox Inbox => Services.GetRequiredService<MockInbox>();

    public MockControls Mocks => Services.GetRequiredService<MockControls>();

    public static async Task<TestApp> StartAsync(bool paused = false, string? folder = null) {
        var dir = folder ?? Path.Combine(Path.GetTempPath(), "runner-tests", Guid.NewGuid().ToString("N")[..8]);
        var app = RunnerApp.Build(new StartOptions(Mock: true, Paused: paused, SelfTest: false, DataDir: dir));
        await app.StartAsync();
        return new TestApp(app, dir, folder is null);
    }

    // Stops the app and starts a new one on the same data folder, as a restart of the PC app.
    public async Task<TestApp> RestartAsync(bool paused = false) {
        await App.StopAsync();
        await App.DisposeAsync();
        return await StartAsync(paused, Folder);
    }

    // Sends an event to the mock inbox. Source "mock" with a type selects a mock handler (MockHandlers).
    public long Send(string type, string source = "mock", string? action = null, string payload = "{}") => Inbox.Send(source, type, action, payload);

    public static async Task Until(Func<bool> condition, string message, double seconds = 15) {
        var watch = Stopwatch.StartNew();
        while (!condition()) {
            if (watch.Elapsed > TimeSpan.FromSeconds(seconds)) {
                Assert.Fail($"Timeout after {seconds} s: {message}");
            }
            await Task.Delay(50);
        }
    }

    public Task UntilFinished(long id, double seconds = 15) =>
        Until(() => Store.StatusOf(id) is EventStatus.Done or EventStatus.Failed, $"event {id} finished", seconds);

    public Task UntilAcked(long id, double seconds = 15) => Until(() => Store.Get(id)?.AckedAt is not null, $"event {id} acked", seconds);

    public async ValueTask DisposeAsync() {
        await App.StopAsync();
        await App.DisposeAsync();
        if (ownsFolder) {
            for (var i = 0; i < 5; i++) {
                try {
                    Directory.Delete(Folder, recursive: true);
                    break;
                } catch (IOException) {
                    await Task.Delay(200);
                }
            }
        }
    }
}
