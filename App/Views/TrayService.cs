using Listener.Presenters;
using Microsoft.Extensions.DependencyInjection;
using VSA.Presenters;

namespace App.Views;

// The tray icon state and the tray menu actions (C6). It has its own Listener presenter in its own scope, and it adds
// it to the live presenters, so the icon follows the listener also when the widget is hidden.
// Changed can occur on any thread: the host marshals it to the UI thread.
public sealed class TrayService : IDisposable {
    readonly IServiceScope scope;
    readonly ListenerPresenter listener;
    readonly IDisposable live;

    public TrayService(IServiceScopeFactory scopes, LivePresenters presenters) {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(presenters);
        scope = scopes.CreateScope();
        listener = scope.ServiceProvider.GetRequiredService<ListenerPresenter>();
        listener.Changed += OnChanged;
        live = presenters.Add(listener);
    }

    public event Action? Changed;

    // Paused, Connecting, Connected or Error.
    public string State => listener.State;

    public bool ListenerOn => listener.On;

    public bool Mock => listener.Mock;

    public string Tooltip => $"Runner · {listener.Subtitle}";

    public Task StartAsync() => listener.LoadAsync();

    public Task ToggleListenerAsync() => listener.ToggleAsync();

    public Task SendMockAsync(string kind) => listener.SendMockAsync(kind);

    public void Dispose() {
        live.Dispose();
        listener.Changed -= OnChanged;
        scope.Dispose();
    }

    void OnChanged() => Changed?.Invoke();
}
