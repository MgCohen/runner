using System.Text.Json;

namespace Notion.Domain;

// The parts of a Notion page that handlers use. Raw holds the full API response.
public sealed record NotionPage(string Id, string Title, string? Url, string? ParentDataSourceId, string? ParentDatabaseId, JsonElement Raw) {
    public static NotionPage FromJson(JsonElement page) {
        var id = page.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? "" : "";
        var url = page.TryGetProperty("url", out var urlValue) ? urlValue.GetString() : null;
        string? dataSourceId = null;
        string? databaseId = null;
        if (page.TryGetProperty("parent", out var parent) && parent.ValueKind == JsonValueKind.Object) {
            dataSourceId = parent.TryGetProperty("data_source_id", out var ds) ? ds.GetString() : null;
            databaseId = parent.TryGetProperty("database_id", out var db) ? db.GetString() : null;
        }
        return new NotionPage(id, ReadTitle(page), url, dataSourceId, databaseId, page.Clone());
    }

    // The option name of a status or select property, or null.
    public string? StatusOf(string property) {
        if (!TryGetProperty(property, out var value)) {
            return null;
        }
        foreach (var kind in new[] { "status", "select" }) {
            if (value.TryGetProperty(kind, out var option) && option.ValueKind == JsonValueKind.Object && option.TryGetProperty("name", out var name)) {
                return name.GetString();
            }
        }
        return null;
    }

    // The plain text of a rich text property, or "".
    public string TextOf(string property) =>
        TryGetProperty(property, out var value) && value.TryGetProperty("rich_text", out var parts) && parts.ValueKind == JsonValueKind.Array
            ? string.Concat(parts.EnumerateArray().Select(p => p.TryGetProperty("plain_text", out var text) ? text.GetString() : null)).Trim()
            : "";

    // The page IDs of a relation property, or an empty list. The page API gives at most 25 IDs for one relation.
    public IReadOnlyList<string> RelationOf(string property) =>
        TryGetProperty(property, out var value) && value.TryGetProperty("relation", out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Select(i => i.TryGetProperty("id", out var id) ? id.GetString() : null).OfType<string>().ToList()
            : [];

    // True if the page is in the data source (or database) with this ID.
    public bool IsIn(string dataSourceId) {
        var target = NotionWebhook.NormalizeId(dataSourceId);
        return (ParentDataSourceId is { } ds && NotionWebhook.NormalizeId(ds) == target)
            || (ParentDatabaseId is { } db && NotionWebhook.NormalizeId(db) == target);
    }

    bool TryGetProperty(string property, out JsonElement value) {
        value = default;
        return Raw.ValueKind == JsonValueKind.Object && Raw.TryGetProperty("properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(property, out value)
            && value.ValueKind == JsonValueKind.Object;
    }

    static string ReadTitle(JsonElement page) {
        if (!page.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object) {
            return "";
        }
        foreach (var property in properties.EnumerateObject()) {
            var value = property.Value;
            if (value.TryGetProperty("type", out var type) && type.GetString() == "title"
                && value.TryGetProperty("title", out var parts) && parts.ValueKind == JsonValueKind.Array) {
                return string.Concat(parts.EnumerateArray().Select(p => p.TryGetProperty("plain_text", out var text) ? text.GetString() : null));
            }
        }
        return "";
    }
}
