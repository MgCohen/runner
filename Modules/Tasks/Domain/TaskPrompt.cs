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

    // "task <title>", at most 40 characters of the title.
    public static string SessionName(NotionPage page) {
        ArgumentNullException.ThrowIfNull(page);
        var title = new string([.. page.Title.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-')]).Trim();
        if (title.Length > 40) {
            title = title[..40].Trim();
        }
        return string.IsNullOrEmpty(title) ? "task" : $"task {title}";
    }
}
