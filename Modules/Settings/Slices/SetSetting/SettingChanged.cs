using VSA.Contracts.Events;

namespace Settings.Slices.SetSetting;

// A setting changed. The new value is in the configuration before this event.
public sealed record SettingChanged(string Module, string Key, string Value) : IntegrationEvent;
