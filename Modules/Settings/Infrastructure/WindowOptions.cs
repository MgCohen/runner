namespace Settings.Infrastructure;

// The settings of the window and the appearance. Section: Modules:Settings. The host reads them at start.
public sealed class WindowOptions {
    // The themes of App.Views/wwwroot/themes. A new theme file adds its name here.
    public static readonly IReadOnlyList<string> Themes = ["System", "Light", "Dark", "Contrast"];

    public bool OpenOnStart { get; set; } = true;

    public bool PinOnStart { get; set; }

    public string Theme { get; set; } = "System";
}
