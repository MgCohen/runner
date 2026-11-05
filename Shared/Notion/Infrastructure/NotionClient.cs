using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Handling.Domain;
using Notion.Domain;

namespace Notion.Infrastructure;

// Reads and changes pages with the Notion API.
public interface INotionClient {
    Task<NotionPage> GetPageAsync(string pageId, CancellationToken ct);

    Task<string> GetPageMarkdownAsync(string pageId, CancellationToken ct);

    // Sets page properties. Build the values with NotionProperties.
    Task UpdatePageAsync(string pageId, JsonObject properties, CancellationToken ct);
}

public sealed record NotionToken(string? Value);

// Calls the Notion API. A 400, 401, 403 or 404 response throws PermanentFailureException: a retry cannot fix it.
// Other errors throw HttpRequestException, and the handler lane tries again.
public sealed class NotionClient(HttpClient http, NotionToken token, Microsoft.Extensions.Options.IOptions<NotionOptions> options) : INotionClient {
    public async Task<NotionPage> GetPageAsync(string pageId, CancellationToken ct) {
        using var document = await SendAsync(HttpMethod.Get, $"v1/pages/{Uri.EscapeDataString(pageId)}", null, ct);
        return NotionPage.FromJson(document.RootElement);
    }

    public async Task<string> GetPageMarkdownAsync(string pageId, CancellationToken ct) {
        using var document = await SendAsync(HttpMethod.Get, $"v1/pages/{Uri.EscapeDataString(pageId)}/markdown?markdown_version=v2", null, ct);
        return document.RootElement.TryGetProperty("markdown", out var markdown) ? markdown.GetString() ?? "" : "";
    }

    public async Task UpdatePageAsync(string pageId, JsonObject properties, CancellationToken ct) {
        var body = new JsonObject { ["properties"] = properties };
        using var _ = await SendAsync(HttpMethod.Patch, $"v1/pages/{Uri.EscapeDataString(pageId)}", body.ToJsonString(), ct);
    }

    async Task<JsonDocument> SendAsync(HttpMethod method, string path, string? body, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(token.Value)) {
            throw new PermanentFailureException("The Notion token is missing. Set NOTION_API_TOKEN (op run).");
        }
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null) {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        request.Headers.Add("Notion-Version", options.Value.Version);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) {
            var error = await response.Content.ReadAsStringAsync(ct);
            var message = $"Notion {method} {path} returned {(int)response.StatusCode}: {error}";
            throw response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound
                ? new PermanentFailureException(message)
                : new HttpRequestException(message, null, response.StatusCode);
        }
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }
}
