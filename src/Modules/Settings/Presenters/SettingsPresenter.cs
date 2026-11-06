using Settings.Slices.GetSettings;
using Settings.Slices.SetSetting;
using VSA.Presenters;

namespace Settings.Presenters;

// The Settings page (C5) and the theme of the window (C7).
public sealed class SettingsPresenter(IBus bus) : Presenter(bus), IPresenterEvent<SettingChanged> {
    public OpState Load { get; } = new();

    public IReadOnlyList<SettingsGroupView> Groups { get; private set; } = [];

    public string Theme => Value("theme") ?? "System";

    public string? Value(string key) => Groups.SelectMany(g => g.Rows).FirstOrDefault(r => r.Key == key)?.Value;

    public Task LoadAsync() => Run(Load, async () => Groups = Map(await Bus.Send(new GetSettingsRequest())));

    public Task SetAsync(string key, string value) => Run(Load, async () => {
        await Bus.Send(new SetSettingRequest(key, value));
        Groups = Map(await Bus.Send(new GetSettingsRequest()));
    });

    public Task OnEventAsync(SettingChanged integrationEvent, CancellationToken ct) => LoadAsync();

    static List<SettingsGroupView> Map(GetSettingsResponse response) =>
        [.. response.Groups.Select(g => new SettingsGroupView(g.Name, [.. g.Rows.Select(r => new SettingView(r.Key, r.Label, r.Description, r.Kind, r.Options, r.Value, r.Later))]))];
}
