namespace AppSettings.Domain;

public enum SettingKind {
    // On or off: the value is "true" or "false".
    Switch,
    // One of Options.
    Choice,
    // A read-only fact. Value gives the text.
    Info,
}

// One row of the Settings page. Later: the row shows, disabled, with a "later" tag, and it cannot change.
// Order sorts the rows in a group.
public sealed record SettingRow(string Key, string Group, string Label, SettingKind Kind, string Default) {
    public string? Description { get; init; }
    public IReadOnlyList<string> Options { get; init; } = [];
    public bool Later { get; init; }
    public int Order { get; init; }
    public Func<string>? Value { get; init; }
}

// The rows of one module.
public sealed record SettingsPart(string Module, IReadOnlyList<SettingRow> Rows);

// The groups of the Settings page, in page order.
public static class SettingGroups {
    public const string AtStart = "At start";
    public const string Traffic = "Traffic";
    public const string Inbox = "Inbox";
    public const string Appearance = "Appearance";
    public const string About = "About";

    public static readonly IReadOnlyList<string> Order = [AtStart, Traffic, Inbox, Appearance, About];
}
