using Listener.Presenters;
using Microsoft.Extensions.DependencyInjection;
using MockHandlers.Presenters;
using VSA.Presenters;

namespace App.Views;

// The mock controls of the self-test (C8), through the presenters of the mock modules: send mock events, cut and
// restore the mock network, set the delay of a mock handler. It also keeps each subtitle of the header, so the
// self-test sees a short state such as "Connecting…".
public sealed class SelfTestProbe : IDisposable {
    readonly IServiceScope scope;
    readonly ListenerPresenter listener;
    readonly MockPresenter mocks;
    readonly IDisposable live;
    readonly List<string> subtitles = [];

    public SelfTestProbe(IServiceScopeFactory scopes, LivePresenters presenters) {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(presenters);
        scope = scopes.CreateScope();
        listener = scope.ServiceProvider.GetRequiredService<ListenerPresenter>();
        mocks = scope.ServiceProvider.GetRequiredService<MockPresenter>();
        listener.Changed += OnChanged;
        live = presenters.Add(listener);
        // The consumer name is in the subtitle.
        _ = listener.LoadAsync();
    }

    // The subtitles of the header, in order, with no repeats in a row.
    public IReadOnlyList<string> Subtitles {
        get {
            lock (subtitles) {
                return [.. subtitles];
            }
        }
    }

    public Task<long> SendAsync(string source, string type, string? action = null, double ageDays = 0) =>
        listener.SendMockEventAsync("custom", source, type, action, ageDays);

    // A Notion task edit: the mock task goes to Todo, so the Tasks handler runs.
    public Task<long> SendTaskAsync() => listener.SendMockEventAsync("task");

    public Task SendAgainAsync(long id) => listener.SendAgainAsync(id);

    public Task SetNetworkAsync(bool up) => listener.SetMockNetworkAsync(up);

    public Task<IReadOnlyList<string>> SetDelayAsync(string handler, int milliseconds) => mocks.SetDelayAsync(handler, milliseconds);

    public Task<IReadOnlyList<string>> RunsAsync() => mocks.RunsAsync();

    public void Dispose() {
        live.Dispose();
        listener.Changed -= OnChanged;
        scope.Dispose();
    }

    void OnChanged() {
        lock (subtitles) {
            var text = listener.Subtitle;
            if (subtitles.Count == 0 || subtitles[^1] != text) {
                subtitles.Add(text);
            }
        }
    }
}
