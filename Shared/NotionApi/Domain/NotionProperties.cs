using System.Globalization;
using System.Text.Json.Nodes;

namespace NotionApi.Domain;

// Builds property values for INotionClient.UpdatePageAsync.
public static class NotionProperties {
    // Notion refuses a rich text item longer than this.
    public const int MaxTextLength = 2000;

    public static JsonNode Status(string name) => new JsonObject { ["status"] = new JsonObject { ["name"] = name } };

    public static JsonNode Date(DateTimeOffset value) =>
        new JsonObject { ["date"] = new JsonObject { ["start"] = value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) } };

    public static JsonNode NoDate() => new JsonObject { ["date"] = null };

    public static JsonNode Url(string? value) => new JsonObject { ["url"] = value };

    public static JsonNode Text(string value) {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0) {
            return new JsonObject { ["rich_text"] = new JsonArray() };
        }
        if (value.Length > MaxTextLength) {
            value = value[..(MaxTextLength - 1)] + "…";
        }
        return new JsonObject { ["rich_text"] = new JsonArray(new JsonObject { ["text"] = new JsonObject { ["content"] = value } }) };
    }
}
