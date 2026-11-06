using System.Text.Json;
using Dispatch.Domain;
using Handling.Domain;
using Listener.Domain;
using Listener.Infrastructure;
using Notion.Domain;

namespace App.Tests;

// The pure helpers, ported from the old Runner.Handlers.Tests and Runner.Host.Tests.
public sealed class HelperTests {
    [TestCase("""{"authors":[{"type":"bot"}]}""", true)]
    [TestCase("""{"authors":[{"type":"person"}]}""", false)]
    [TestCase("""{}""", false)]
    public void Has_bot_author(string json, bool expected) {
        using var doc = JsonDocument.Parse(json);
        Assert.That(NotionWebhook.HasBotAuthor(doc.RootElement), Is.EqualTo(expected));
    }

    [Test]
    public void RelationOf_reads_the_page_ids_of_a_relation() {
        using var doc = JsonDocument.Parse("""
            {"id":"p","properties":{"Blocks":{"type":"relation","relation":[{"id":"a"},{"id":"b"}]},"Name":{"type":"title","title":[]}}}
            """);
        var page = NotionPage.FromJson(doc.RootElement);
        Assert.That(page.RelationOf("Blocks"), Is.EqualTo(new[] { "a", "b" }));
        Assert.That(page.RelationOf("Missing"), Is.Empty);
    }

    [Test]
    public void Entity_id_and_normalize_id() {
        using var doc = JsonDocument.Parse("""{"entity":{"id":"40E9-1E24"}}""");

        Assert.That(NotionWebhook.EntityId(doc.RootElement), Is.EqualTo("40E9-1E24"));
        Assert.That(NotionWebhook.NormalizeId("40E9-1E24"), Is.EqualTo("40e91e24"));
    }

    [Test]
    public void Long_text_is_cut_to_the_notion_limit() {
        var text = NotionProperties.Text(new string('a', 2500))["rich_text"]![0]!["text"]!["content"]!.GetValue<string>();

        Assert.That(text, Has.Length.EqualTo(NotionProperties.MaxTextLength).And.EndsWith("…"));
    }

    [Test]
    public void Retry_delay_doubles_up_to_the_maximum() {
        var policy = new RetryPolicy(5, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2));

        Assert.That(Enumerable.Range(1, 4).Select(n => policy.Delay(n).TotalSeconds), Is.EqualTo(new double[] { 30, 60, 120, 120 }));
        Assert.That((policy.IsLast(4), policy.IsLast(5)), Is.EqualTo((false, true)));
    }

    [Test]
    public void Reconnect_delays_are_1_2_5_10_then_30_seconds() =>
        Assert.That(Enumerable.Range(0, 7).Select(n => Reconnect.Delay(n).TotalSeconds), Is.EqualTo(new double[] { 1, 2, 5, 10, 30, 30, 30 }));

    [TestCase("LAPTOP-URA30POK", "laptop-ura30pok")]
    [TestCase("__Mine", "mine")]
    [TestCase("***", "runner")]
    public void Consumer_name_from_the_machine_name(string machine, string expected) {
        Assert.That(ConsumerName.FromMachineName(machine), Is.EqualTo(expected));
        Assert.That(ConsumerName.IsValid(expected), Is.True);
    }

    [Test]
    public void Listens_to_text_groups_the_actions_by_type() {
        var handler = new HandlerInfo("k", "TaskTodoStarted", ["notion"], [new EventMatch("page", "properties_updated"), new EventMatch("page", "created")], "", true, TimeSpan.Zero);
        var all = handler with { Matches = [new EventMatch(null, null)] };

        Assert.That(LastText.ListensTo(handler), Is.EqualTo("notion · page · properties_updated, created"));
        Assert.That(LastText.ListensTo(all), Is.EqualTo("notion · all"));
    }

    [Test]
    public void Event_state_of_the_store_status() =>
        Assert.That(new[] { EventState.Of(EventStatus.Pending, 0), EventState.Of(EventStatus.Pending, 2), EventState.Of(EventStatus.Done, 1), EventState.Of(EventStatus.Failed, 5) },
            Is.EqualTo(new[] { "new", "pending", "done", "failed" }));
}
