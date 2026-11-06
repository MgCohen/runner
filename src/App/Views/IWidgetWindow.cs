namespace App.Views;

// The window of the widget. App.Host implements it: pin, hide and drag call the host, they are not slices.
public interface IWidgetWindow {
    bool Pinned { get; }

    void SetPinned(bool pinned);

    void Hide();

    // Starts a drag of the window with the mouse button that is down.
    void BeginDrag();
}

// A window for a test host with no window.
public sealed class NoWindow : IWidgetWindow {
    public bool Pinned { get; private set; }

    public void SetPinned(bool pinned) => Pinned = pinned;

    public void Hide() {
    }

    public void BeginDrag() {
    }
}
