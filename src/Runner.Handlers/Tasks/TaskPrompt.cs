using System.Text;
using Runner.Handlers.Notion;

namespace Runner.Handlers.Tasks;

/// <summary>Builds the Claude prompt for one task page.</summary>
public static class TaskPrompt
{
    public static string Build(TaskOptions options, NotionPage page, string markdown)
    {
        if (markdown.Length > options.MaxMarkdownChars)
        {
            markdown = markdown[..options.MaxMarkdownChars] + "\n\n[The page continues. Read it at the URL.]";
        }
        return new StringBuilder()
            .AppendLine(options.Instructions)
            .AppendLine()
            .AppendLine($"Task: {page.Title}")
            .AppendLine($"URL: {page.Url}")
            .AppendLine($"Page ID: {page.Id}")
            .AppendLine()
            .AppendLine("Task page (Markdown):")
            .AppendLine(markdown)
            .ToString();
    }

    /// <summary>The filter for tasks with one status.</summary>
    public static System.Text.Json.Nodes.JsonObject StatusFilter(TaskOptions options, string status) => new()
    {
        ["property"] = options.StatusProperty,
        ["status"] = new System.Text.Json.Nodes.JsonObject { ["equals"] = status },
    };
}
