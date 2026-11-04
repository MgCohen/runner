using System.Text.Json;

namespace Runner.Handlers.Notion;

/// <summary>The parts of a Notion page that handlers use. <see cref="Raw"/> holds the full API response.</summary>
public sealed record NotionPage(
    string Id,
    string Title,
    string? Url,
    string? ParentDataSourceId,
    string? ParentDatabaseId,
    JsonElement Raw)
{
    /// <summary>Reads a page object from the Notion API.</summary>
    public static NotionPage FromJson(JsonElement page)
    {
        var id = page.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? "" : "";
        var url = page.TryGetProperty("url", out var urlValue) ? urlValue.GetString() : null;

        string? dataSourceId = null;
        string? databaseId = null;
        if (page.TryGetProperty("parent", out var parent) && parent.ValueKind == JsonValueKind.Object)
        {
            dataSourceId = parent.TryGetProperty("data_source_id", out var ds) ? ds.GetString() : null;
            databaseId = parent.TryGetProperty("database_id", out var db) ? db.GetString() : null;
        }

        return new NotionPage(id, ReadTitle(page), url, dataSourceId, databaseId, page.Clone());
    }

    /// <summary>Returns the option name of a status or select property, or null.</summary>
    public string? StatusOf(string property)
    {
        if (!TryGetProperty(property, out var value)) return null;
        foreach (var kind in new[] { "status", "select" })
        {
            if (value.TryGetProperty(kind, out var option) && option.ValueKind == JsonValueKind.Object
                && option.TryGetProperty("name", out var name))
            {
                return name.GetString();
            }
        }
        return null;
    }

    /// <summary>Returns the plain text of a rich text property, or an empty string.</summary>
    public string TextOf(string property) =>
        TryGetProperty(property, out var value)
        && value.TryGetProperty("rich_text", out var parts) && parts.ValueKind == JsonValueKind.Array
            ? string.Concat(parts.EnumerateArray()
                .Select(p => p.TryGetProperty("plain_text", out var text) ? text.GetString() : null)).Trim()
            : "";

    private bool TryGetProperty(string property, out JsonElement value)
    {
        value = default;
        return Raw.ValueKind == JsonValueKind.Object
            && Raw.TryGetProperty("properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object
            && properties.TryGetProperty(property, out value)
            && value.ValueKind == JsonValueKind.Object;
    }

    private static string ReadTitle(JsonElement page)
    {
        if (!page.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
        {
            return "";
        }

        foreach (var property in properties.EnumerateObject())
        {
            var value = property.Value;
            if (value.TryGetProperty("type", out var type) && type.GetString() == "title"
                && value.TryGetProperty("title", out var parts) && parts.ValueKind == JsonValueKind.Array)
            {
                return string.Concat(parts.EnumerateArray()
                    .Select(p => p.TryGetProperty("plain_text", out var text) ? text.GetString() : null));
            }
        }

        return "";
    }
}
