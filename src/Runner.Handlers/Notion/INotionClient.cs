namespace Runner.Handlers.Notion;

/// <summary>Reads pages from the Notion API.</summary>
public interface INotionClient
{
    /// <summary>Returns the page and its properties (<c>GET /v1/pages/{id}</c>).</summary>
    Task<NotionPage> GetPageAsync(string pageId, CancellationToken cancellationToken);

    /// <summary>Returns the page content as Markdown (<c>GET /v1/pages/{id}/markdown</c>).</summary>
    Task<string> GetPageMarkdownAsync(string pageId, CancellationToken cancellationToken);
}
