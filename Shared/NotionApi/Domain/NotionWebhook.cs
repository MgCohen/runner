using System.Text.Json;

namespace NotionApi.Domain;

// Reads fields from a Notion webhook body. A Notion event has IDs only, not the page content.
public static class NotionWebhook {
    // entity.id, or null.
    public static string? EntityId(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("entity", out var entity)
        && entity.ValueKind == JsonValueKind.Object && entity.TryGetProperty("id", out var id)
            ? id.GetString() : null;

    // True if one or more authors have the type bot. The "Claude workflow" connection is a bot, so its own edits do not start a loop.
    public static bool HasBotAuthor(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("authors", out var authors)
        && authors.ValueKind == JsonValueKind.Array
        && authors.EnumerateArray().Any(a => a.ValueKind == JsonValueKind.Object && a.TryGetProperty("type", out var type)
            && string.Equals(type.GetString(), "bot", StringComparison.OrdinalIgnoreCase));

    // The ID in lower case with no dashes, so two forms of one ID are equal.
    public static string NormalizeId(string id) {
        ArgumentNullException.ThrowIfNull(id);
        return id.Replace("-", "", StringComparison.Ordinal).Trim().ToLowerInvariant();
    }
}
