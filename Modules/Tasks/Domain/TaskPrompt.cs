using System.Globalization;
using System.Text;
using NotionApi.Domain;

namespace Tasks.Domain;

// The Claude prompt for one task page.
public static class TaskPrompt {
    public static string Build(TaskOptions options, NotionPage page, string markdown) {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(markdown);
        if (markdown.Length > options.MaxMarkdownChars) {
            markdown = markdown[..options.MaxMarkdownChars] + "\n\n[The page continues. Read it at the URL.]";
        }
        return new StringBuilder()
            .AppendLine(options.Instructions)
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Task: {page.Title}")
            .AppendLine(CultureInfo.InvariantCulture, $"URL: {page.Url}")
            .AppendLine(CultureInfo.InvariantCulture, $"Page ID: {page.Id}")
            .AppendLine()
            .AppendLine("Task page (Markdown):")
            .AppendLine(markdown)
            .ToString();
    }

}
