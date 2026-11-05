using VSA.Contracts.Requests;

namespace Settings.Slices.GetSettings;

public sealed record GetSettingsRequest : IRequest<GetSettingsResponse> {
    public static HttpMethod Method => HttpMethod.Get;
    public static string Route => "/api/settings";
}

// The groups in page order, with the rows of all modules.
public sealed record GetSettingsResponse(IReadOnlyList<SettingGroup> Groups) : IResponse;

public sealed record SettingGroup(string Name, IReadOnlyList<SettingItem> Rows);

// Kind: Switch, Choice or Info. Later: shown disabled with a "later" tag.
public sealed record SettingItem(string Key, string Label, string? Description, string Kind, IReadOnlyList<string> Options, string Value, bool Later);
