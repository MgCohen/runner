using System.Text.Json.Nodes;

namespace Runner.Handlers.Notion;

/// <summary>Builds property values for <see cref="INotionClient.UpdatePageAsync"/>.</summary>
public static class NotionProperties
{
    /// <summary>Notion refuses a rich text item longer than this.</summary>
    public const int MaxTextLength = 2000;

    public static JsonNode Status(string name) => new JsonObject { ["status"] = new JsonObject { ["name"] = name } };

    public static JsonNode Date(DateTimeOffset value) =>
        new JsonObject { ["date"] = new JsonObject { ["start"] = value.ToUniversalTime().ToString("o") } };

    public static JsonNode Url(string? value) => new JsonObject { ["url"] = value };

    public static JsonNode Text(string value)
    {
        if (value.Length == 0)
        {
            return new JsonObject { ["rich_text"] = new JsonArray() };
        }
        if (value.Length > MaxTextLength)
        {
            value = value[..(MaxTextLength - 1)] + "…";
        }
        return new JsonObject
        {
            ["rich_text"] = new JsonArray(new JsonObject { ["text"] = new JsonObject { ["content"] = value } }),
        };
    }
}
