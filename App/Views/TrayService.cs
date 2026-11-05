using Listener.Presenters;
using Microsoft.Extensions.DependencyInjection;

namespace App.Views;

// The tray icon state and the tray menu actions (C6). It has its own Listener presenter in its own scope. The
// presenter gets its events while the scope exists, so the icon follows the listener also when the widget is hidden.
// Changed can occur on any thread: the host marshals it to the UI thread.
public sealed class TrayService : IDisposable {
    readonly IServiceScope scope;
    readonly ListenerPresenter listener;

    public TrayService(IServiceScopeFactory scopes) {
        ArgumentNullException.ThrowIfNull(scopes);
        scope = scopes.CreateScope();
        listener = scope.ServiceProvider.GetRequiredService<ListenerPresenter>();
        listener.Changed += OnChanged;
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
        listener.Changed -= OnChanged;
        scope.Dispose();
    }

    void OnChanged() => Changed?.Invoke();
}
