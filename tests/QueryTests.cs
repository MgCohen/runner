using Events.Slices.ListEvents;

namespace App.Tests;

public sealed class QueryTests {
    static ListEventsRequest Query(IReadOnlyList<string>? states = null, IReadOnlyList<string>? sources = null, string? text = null, long? before = null) =>
        new(states ?? [], sources ?? [], [], text, before);

    [Test]
    public async Task A25_pages_of_50_newest_first_until_has_more_is_false() {
        await using var app = await TestApp.StartAsync(paused: true);
        var ids = Enumerable.Range(0, 120).Select(_ => app.Send("push", source: "bulk")).ToList();
        await app.Bus.Send(new Listener.Slices.SetListener.SetListenerRequest(true));
        await app.UntilAcked(ids[^1], seconds: 30);

        var all = new List<EventItem>();
        long? before = null;
        ListEventsResponse page;
        do {
            page = await app.Bus.Send(Query(sources: ["bulk"], before: before));
            all.AddRange(page.Events);
            before = page.Events.LastOrDefault()?.Id;
        } while (page.HasMore);

        Assert.That(all.Select(e => e.Id), Is.EqualTo(Enumerable.Reverse(ids)));
        Assert.That(page.Matching, Is.EqualTo(120));
    }

    [Test]
    public async Task A26_text_matches_the_id_the_source_and_the_type() {
        await using var app = await TestApp.StartAsync();
        var task = app.Send("task", source: "github");
        var other = app.Send("push", source: "github");
        await app.UntilAcked(other);
        await app.UntilAcked(task);

        var page = await app.Bus.Send(Query(text: "TASK"));

        Assert.That(page.Events.Select(e => e.Id), Is.EqualTo(new[] { task }));
    }

    [Test]
    public async Task A9_filters_combine_and_facets_count_with_the_other_filters() {
        await using var app = await TestApp.StartAsync();
        var failed = app.Send("permanent");
        var done = app.Send("echo");
        var github = app.Send("push", source: "github");
        foreach (var id in new[] { failed, done, github }) {
            await app.UntilFinished(id);
        }

        var page = await app.Bus.Send(Query(states: ["failed"], sources: ["mock"]));

        Assert.That(page.Events.Select(e => e.Id), Is.EqualTo(new[] { failed }));
        Assert.That(page.Facets["source"].GetValueOrDefault("github"), Is.Zero, "State failed: no github event.");
        Assert.That(page.Facets["state"]["done"], Is.EqualTo(1), "With Source = mock: one done event.");
        Assert.That(page.Total, Is.EqualTo(3));
    }
}
