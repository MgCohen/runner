using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Runner.Abstractions;

namespace Runner.Handlers.Notion;

/// <summary>
/// Calls the Notion API with the token from <see cref="NotionOptions"/>.
/// A 400, 401, 403 or 404 response throws <see cref="PermanentFailureException"/>, because a retry cannot fix it.
/// Other errors throw <see cref="HttpRequestException"/>, and the connector tries again.
/// </summary>
public sealed class NotionClient(HttpClient http, IOptions<NotionOptions> options) : INotionClient
{
    private readonly NotionOptions _options = options.Value;

    public async Task<NotionPage> GetPageAsync(string pageId, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync($"v1/pages/{Uri.EscapeDataString(pageId)}", cancellationToken);
        return NotionPage.FromJson(document.RootElement);
    }

    public async Task<string> GetPageMarkdownAsync(string pageId, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync($"v1/pages/{Uri.EscapeDataString(pageId)}/markdown?markdown_version=v2", cancellationToken);
        return document.RootElement.TryGetProperty("markdown", out var markdown) ? markdown.GetString() ?? "" : "";
    }

    public async Task UpdatePageAsync(string pageId, JsonObject properties, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["properties"] = properties };
        using var document = await SendJsonAsync(HttpMethod.Patch, $"v1/pages/{Uri.EscapeDataString(pageId)}",
            body.ToJsonString(), cancellationToken);
    }

    public async Task<IReadOnlyList<NotionPage>> QueryDataSourceAsync(string dataSourceId, JsonObject filter,
        CancellationToken cancellationToken)
    {
        var pages = new List<NotionPage>();
        string? cursor = null;
        do
        {
            var body = new JsonObject { ["filter"] = filter.DeepClone(), ["page_size"] = 100 };
            if (cursor is not null) body["start_cursor"] = cursor;
            using var document = await SendJsonAsync(HttpMethod.Post,
                $"v1/data_sources/{Uri.EscapeDataString(dataSourceId)}/query", body.ToJsonString(), cancellationToken);
            var root = document.RootElement;
            if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                pages.AddRange(results.EnumerateArray().Select(NotionPage.FromJson));
            }
            cursor = root.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.True
                && root.TryGetProperty("next_cursor", out var next) ? next.GetString() : null;
        } while (cursor is not null);
        return pages;
    }

    private Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken) =>
        SendJsonAsync(HttpMethod.Get, path, null, cancellationToken);

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, string? body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            throw new PermanentFailureException(
                "The Notion token is missing. Set Notion:Token in user secrets or the NOTION_API_TOKEN environment variable.");
        }

        using var request = new HttpRequestMessage(method, new Uri(new Uri(_options.BaseUrl), path));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
        request.Headers.Add("Notion-Version", _options.Version);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            var message = $"Notion {method} {path} returned {(int)response.StatusCode}: {error}";
            throw response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden or HttpStatusCode.NotFound
                ? new PermanentFailureException(message)
                : new HttpRequestException(message, null, response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}
