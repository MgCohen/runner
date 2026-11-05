using App.Views;

namespace App.Host;

// The IAppMode of the views and of the CLI command "mode". Program sets Restart: it saves the mode, stops the app
// and starts it again in the other mode.
public sealed class AppMode(bool mock) : IAppMode {
    public bool Mock => mock;

    public Action<bool>? Restart { get; set; }

    public void Switch(bool mock) {
        if (mock != Mock) {
            Restart?.Invoke(mock);
        }
    }
}
