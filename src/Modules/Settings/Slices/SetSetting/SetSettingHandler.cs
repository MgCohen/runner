using System.Globalization;
using System.Text.Json.Nodes;
using AppSettings.Domain;
using Settings.Infrastructure;
using VSA.Contracts.Events;
using VSA.Domain;
using VSA.Handlers.Requests;

namespace Settings.Slices.SetSetting;

// Saves one setting and applies it at once. A setting with the "later" tag, an Info row or a value that is not
// an option of the row is refused, and the file does not change (A22).
sealed class SetSettingHandler(IEnumerable<SettingsPart> parts, SettingsFile file, IIntegrationEventPublisher events)
    : RoutedHandler<SetSettingRequest, SetSettingResponse> {
    protected override async Task<Result<SetSettingResponse>> HandleRequestAsync(SetSettingRequest request, CancellationToken ct) {
        var found = parts.SelectMany(p => p.Rows.Select(r => (p.Module, Row: r))).FirstOrDefault(r => r.Row.Key == request.Key);
        if (found.Row is not { } row) {
            return new Error("settings.not_found", $"No setting has the key '{request.Key}'.");
        }
        if (row.Later || row.Kind == SettingKind.Info) {
            return new Error("settings.read_only", $"'{row.Label}' cannot change now.");
        }
        JsonNode value;
        if (row.Kind == SettingKind.Switch) {
            if (!bool.TryParse(request.Value, out var on)) {
                return new Error("settings.value", $"'{row.Label}' takes true or false.");
            }
            value = on;
        } else {
            var option = row.Options.FirstOrDefault(o => string.Equals(o, request.Value, StringComparison.OrdinalIgnoreCase));
            if (option is null) {
                return new Error("settings.value", $"'{row.Label}' takes one of: {string.Join(", ", row.Options)}.");
            }
            value = int.TryParse(option, CultureInfo.InvariantCulture, out var number) ? number : option;
        }
        file.Set(found.Module, row.Key, value);
        var text = value.ToString();
        await events.PublishAsync(new SettingChanged(found.Module, row.Key, text), ct);
        return new SetSettingResponse(row.Key, text);
    }
}
