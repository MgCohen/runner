using System.Text.Json;
using Claude.Infrastructure;
using Dispatch.Domain;
using Handling.Domain;
using Listener.Domain;
using Listener.Infrastructure;
using NotionApi.Domain;

namespace App.Tests;

// The pure helpers, ported from the old Runner.Handlers.Tests and Runner.Host.Tests.
public sealed class HelperTests {
    [Test]
    public void Background_id_comes_from_the_cli_output() =>
        Assert.That(ClaudeLauncher.ParseBackgroundId("backgrounded · 1a2b3c\n"), Is.EqualTo("1a2b3c"));

    [Test]
    public void Background_id_is_null_when_the_output_has_none() => Assert.That(ClaudeLauncher.ParseBackgroundId("error"), Is.Null);

    [Test]
    public void Agents_list_is_parsed() {
        var agents = ClaudeAgents.Parse("""[{"id":"a1","sessionId":"s1","state":"done"},{"x":1}]""");

        Assert.That(agents, Is.EqualTo(new[] { new ClaudeAgent("a1", "s1", "done") }));
    }

    [Test]
    public void Last_answer_is_the_last_assistant_text() {
        string[] lines = [
            """{"type":"assistant","message":{"content":[{"type":"text","text":"first"}]}}""",
            """{"type":"user","message":{"content":[{"type":"text","text":"assistant"}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"last"}]}}""",
            "{\"assistant\" not json",
        ];

        Assert.That(ClaudeAgents.LastAnswer(lines), Is.EqualTo("last"));
    }

    [TestCase("""{"authors":[{"type":"bot"}]}""", true)]
    [TestCase("""{"authors":[{"type":"person"}]}""", false)]
    [TestCase("""{}""", false)]
    public void Has_bot_author(string json, bool expected) {
        using var doc = JsonDocument.Parse(json);
        Assert.That(NotionWebhook.HasBotAuthor(doc.RootElement), Is.EqualTo(expected));
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
