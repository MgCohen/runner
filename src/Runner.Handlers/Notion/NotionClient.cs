using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
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

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            throw new PermanentFailureException(
                "The Notion token is missing. Set Notion:Token in user secrets or the NOTION_API_TOKEN environment variable.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(_options.BaseUrl), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
        request.Headers.Add("Notion-Version", _options.Version);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var message = $"Notion GET {path} returned {(int)response.StatusCode}: {body}";
            throw response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden or HttpStatusCode.NotFound
                ? new PermanentFailureException(message)
                : new HttpRequestException(message, null, response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}
