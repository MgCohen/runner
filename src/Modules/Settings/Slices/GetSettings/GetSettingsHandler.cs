using AppSettings.Domain;
using Microsoft.Extensions.Configuration;
using VSA.Domain;
using VSA.Handlers.Requests;

namespace Settings.Slices.GetSettings;

// Renders the schema of each module with the current values. A value that is not valid shows the default.
sealed class GetSettingsHandler(IEnumerable<SettingsPart> parts, IConfiguration configuration) : RoutedHandler<GetSettingsRequest, GetSettingsResponse> {
    protected override Task<Result<GetSettingsResponse>> HandleRequestAsync(GetSettingsRequest request, CancellationToken ct) {
        var rows = parts.SelectMany(p => p.Rows.Select(r => (p.Module, Row: r))).ToList();
        var groups = rows.GroupBy(r => r.Row.Group)
            .OrderBy(g => SettingGroups.Order.Contains(g.Key) ? SettingGroups.Order.ToList().IndexOf(g.Key) : int.MaxValue)
            .Select(g => new SettingGroup(g.Key, [.. g.OrderBy(r => r.Row.Order).Select(r => Item(r.Module, r.Row))]))
            .ToList();
        return Task.FromResult<Result<GetSettingsResponse>>(new GetSettingsResponse(groups));
    }

    SettingItem Item(string module, SettingRow row) =>
        new(row.Key, row.Label, row.Description, row.Kind.ToString(), row.Options, Value(module, row), row.Later);

    string Value(string module, SettingRow row) {
        if (row.Value is { } read) {
            return read();
        }
        if (row.Later) {
            return row.Default;
        }
        var value = configuration[$"Modules:{module}:{row.Key}"];
        return row.Kind switch {
            SettingKind.Switch when bool.TryParse(value, out var on) => on ? "true" : "false",
            SettingKind.Choice when value is not null && row.Options.Contains(value, StringComparer.OrdinalIgnoreCase) =>
                row.Options.First(o => string.Equals(o, value, StringComparison.OrdinalIgnoreCase)),
            _ => row.Default,
        };
    }
}
