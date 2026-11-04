using System.Text.Json.Nodes;

namespace Runner.Handlers.Notion;

/// <summary>Reads and changes pages with the Notion API.</summary>
public interface INotionClient
{
    /// <summary>Returns the page and its properties (<c>GET /v1/pages/{id}</c>).</summary>
    Task<NotionPage> GetPageAsync(string pageId, CancellationToken cancellationToken);

    /// <summary>Returns the page content as Markdown (<c>GET /v1/pages/{id}/markdown</c>).</summary>
    Task<string> GetPageMarkdownAsync(string pageId, CancellationToken cancellationToken);

    /// <summary>Sets page properties (<c>PATCH /v1/pages/{id}</c>). Build the values with <see cref="NotionProperties"/>.</summary>
    Task UpdatePageAsync(string pageId, JsonObject properties, CancellationToken cancellationToken);

    /// <summary>Returns the pages of a data source that match a filter (<c>POST /v1/data_sources/{id}/query</c>).</summary>
    Task<IReadOnlyList<NotionPage>> QueryDataSourceAsync(string dataSourceId, JsonObject filter, CancellationToken cancellationToken);
}
