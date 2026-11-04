using Microsoft.Extensions.DependencyInjection;
using Runner.Abstractions;

namespace Runner.Host.Tests;

public sealed class UnitTests
{
    [Theory]
    [InlineData("DESKTOP-AB12", "desktop-ab12")]
    [InlineData("My PC.local", "my-pc-local")]
    [InlineData("-_Box", "box")]
    [InlineData("!!!", "runner")]
    public void Consumer_name_from_machine_name_is_valid(string machine, string expected)
    {
        var name = ConsumerName.FromMachineName(machine);
        Assert.Equal(expected, name);
        Assert.True(ConsumerName.IsValid(name));
    }

    [Fact]
    public void Consumer_name_is_at_most_64_characters()
    {
        var name = ConsumerName.FromMachineName(new string('a', 100));
        Assert.Equal(64, name.Length);
        Assert.True(ConsumerName.IsValid(name));
    }

    [Theory]
    [InlineData("https://inbox.mgcohen.workers.dev", "wss://inbox.mgcohen.workers.dev/listen?consumer=pc-1")]
    [InlineData("http://127.0.0.1:8787/", "ws://127.0.0.1:8787/listen?consumer=pc-1")]
    public void Listen_uri_uses_the_websocket_scheme(string baseUrl, string expected)
    {
        var uri = SocketListener.ListenUri(new InboxOptions { BaseUrl = new Uri(baseUrl), Consumer = "pc-1" });
        Assert.Equal(expected, uri.ToString());
    }

    [Fact]
    public void Catalog_finds_handlers_with_on_and_matches_more_than_one()
    {
        var catalog = new HandlerCatalog([new HandlerAssembly(typeof(UnitTests).Assembly)]);

        var matched = catalog.Match(TestHost.Event(1, "ok")).Select(h => h.Type).ToList();

        Assert.Equal([typeof(OkHandler), typeof(SecondHandler)], matched.OrderBy(t => t.Name));
        Assert.Empty(catalog.Match(TestHost.Event(1, "page", source: "notion")));
    }

    [Fact]
    public void Backoff_doubles_and_stops_at_the_maximum()
    {
        var b = TimeSpan.FromSeconds(1);
        var max = TimeSpan.FromSeconds(10);
        Assert.Equal(TimeSpan.FromSeconds(1), Backoff.Delay(b, max, 0, jitter: false));
        Assert.Equal(TimeSpan.FromSeconds(4), Backoff.Delay(b, max, 2, jitter: false));
        Assert.Equal(max, Backoff.Delay(b, max, 20, jitter: false));
        var withJitter = Backoff.Delay(b, max, 2);
        Assert.InRange(withJitter, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
    }
}
