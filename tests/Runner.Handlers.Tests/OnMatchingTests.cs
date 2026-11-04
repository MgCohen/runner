using System.Reflection;
using Runner.Abstractions;

namespace Runner.Handlers.Tests;

public class OnMatchingTests
{
    private static bool Matches<T>(Abstractions.InboxEvent e) =>
        typeof(T).GetCustomAttributes<OnAttribute>().Any(a => a.Matches(e));

    [Theory]
    [InlineData("notion", "page", "properties_updated", true)]
    [InlineData("notion", "page", "content_updated", true)]
    [InlineData("Notion", "PAGE", "Content_Updated", true)]
    [InlineData("notion", "page", "created", false)]
    [InlineData("notion", "database", "properties_updated", false)]
    [InlineData("github", "page", "properties_updated", false)]
    public void NotionPageUpdated_matches_page_updates_only(string source, string eventType, string action, bool expected) =>
        Assert.Equal(expected, Matches<NotionPageUpdated>(Events.Other(source, eventType, action)));

    [Theory]
    [InlineData("notion", "page", "created", true)]
    [InlineData("notion", "comment", "created", true)]
    [InlineData("notion", null, null, true)]
    [InlineData("linear", "Issue", "create", false)]
    public void LogEverything_matches_each_notion_event(string source, string? eventType, string? action, bool expected) =>
        Assert.Equal(expected, Matches<LogEverything>(Events.Other(source, eventType, action)));

    [Fact]
    public void HandlerTypes_finds_each_handler() =>
        Assert.Equal(
            new[] { nameof(LogEverything), nameof(NotionPageUpdated), nameof(Runner.Handlers.Tasks.TaskTodoStarted) },
            HandlersServiceCollectionExtensions.HandlerTypes().Select(t => t.Name).Order().ToArray());
}
