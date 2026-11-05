using System.Globalization;
using System.Text;
using Notion.Domain;

namespace Tasks.Domain;

// The Claude prompt for one task page.
public static class TaskPrompt {
    public static string Build(TaskOptions options, NotionPage page, string markdown, NotionPage? answered = null) {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(markdown);
        if (markdown.Length > options.MaxMarkdownChars) {
            markdown = markdown[..options.MaxMarkdownChars] + "\n\n[The page continues. Read it at the URL.]";
        }
        var text = new StringBuilder()
            .AppendLine(options.Instructions)
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Task: {page.Title}")
            .AppendLine(CultureInfo.InvariantCulture, $"URL: {page.Url}")
            .AppendLine(CultureInfo.InvariantCulture, $"Page ID: {page.Id}")
            .AppendLine(CultureInfo.InvariantCulture, $"Questions database: collection://{options.QuestionsDataSourceId}")
            .AppendLine()
            .AppendLine("Task page (Markdown):")
            .AppendLine(markdown);
        if (answered is not null) {
            text.AppendLine()
                .AppendLine("An earlier run of this task stopped for a question. The question now has an answer. Continue the task with it.")
                .AppendLine(CultureInfo.InvariantCulture, $"Question: {answered.Title}")
                .AppendLine(CultureInfo.InvariantCulture, $"Context: {answered.TextOf(options.ContextProperty)}")
                .AppendLine(CultureInfo.InvariantCulture, $"Answer: {answered.TextOf(options.AnswerProperty)}");
        }
        return text.ToString();
    }

}
