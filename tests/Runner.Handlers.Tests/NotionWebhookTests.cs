using System.Text.Json;
using Runner.Handlers.Notion;

namespace Runner.Handlers.Tests;

public class NotionWebhookTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Theory]
    [InlineData("""{"authors":[{"id":"b","type":"bot"}]}""", true)]
    [InlineData("""{"authors":[{"id":"u","type":"person"},{"id":"b","type":"bot"}]}""", true)]
    [InlineData("""{"authors":[{"id":"u","type":"person"}]}""", false)]
    [InlineData("""{"authors":[]}""", false)]
    [InlineData("""{}""", false)]
    [InlineData("""{"authors":"bot"}""", false)]
    public void HasBotAuthor(string json, bool expected) =>
        Assert.Equal(expected, NotionWebhook.HasBotAuthor(Json(json)));

    [Fact]
    public void EntityId_reads_entity_id()
    {
        Assert.Equal("abc", NotionWebhook.EntityId(Json("""{"entity":{"id":"abc","type":"page"}}""")));
        Assert.Null(NotionWebhook.EntityId(Json("{}")));
        Assert.Null(NotionWebhook.EntityId(Json("[]")));
    }

    [Fact]
    public void NormalizeId_ignores_dashes_and_case() =>
        Assert.Equal(
            NotionWebhook.NormalizeId("AAAAAAAA-bbbb-cccc-dddd-eeeeeeeeeeee"),
            NotionWebhook.NormalizeId("aaaaaaaabbbbccccddddeeeeeeeeeeee"));
}
