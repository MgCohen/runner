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
