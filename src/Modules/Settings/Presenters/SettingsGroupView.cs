namespace Settings.Presenters;

public sealed record SettingsGroupView(string Name, IReadOnlyList<SettingView> Rows);

// Kind: Switch, Choice or Info.
public sealed record SettingView(string Key, string Label, string? Description, string Kind, IReadOnlyList<string> Options, string Value, bool Later);
