using App.Views;

namespace App.Host;

// The IWidgetWindow of the views. The host sets the form after it creates it; before that, the calls do nothing.
public sealed class WidgetWindow : IWidgetWindow {
    public WidgetForm? Form { get; set; }

    public bool Pinned => Form?.TopMost ?? false;

    public void SetPinned(bool pinned) {
        if (Form is { } form) {
            form.TopMost = pinned;
        }
    }

    public void Hide() => Form?.Hide();

    public void BeginDrag() => Form?.BeginDrag();
}
