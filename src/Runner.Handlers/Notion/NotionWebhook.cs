using System.Text.Json;

namespace Runner.Handlers.Notion;

/// <summary>Reads fields from a Notion webhook body. A Notion event has IDs only, not the page content.</summary>
public static class NotionWebhook
{
    /// <summary>Returns <c>entity.id</c>, or null.</summary>
    public static string? EntityId(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty("entity", out var entity)
        && entity.ValueKind == JsonValueKind.Object
        && entity.TryGetProperty("id", out var id)
            ? id.GetString()
            : null;

    /// <summary>
    /// Returns true if one or more entries in <c>authors</c> have the type <c>bot</c>.
    /// The "Claude workflow" connection is a bot, so its own edits do not start a loop.
    /// </summary>
    public static bool HasBotAuthor(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty("authors", out var authors)
        && authors.ValueKind == JsonValueKind.Array
        && authors.EnumerateArray().Any(a =>
            a.ValueKind == JsonValueKind.Object
            && a.TryGetProperty("type", out var type)
            && string.Equals(type.GetString(), "bot", StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns the ID in lower case without dashes, so two forms of one ID compare equal.</summary>
    public static string NormalizeId(string id) => id.Replace("-", "").Trim().ToLowerInvariant();
}
