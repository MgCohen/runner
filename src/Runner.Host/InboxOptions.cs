using System.Text;
using System.Text.RegularExpressions;

namespace Runner.Host;

/// <summary>Settings of the connector. Configuration section: <c>Inbox</c>.</summary>
public sealed class InboxOptions
{
    public const string Section = "Inbox";

    /// <summary>Base URL of the inbox Worker, for example https://inbox.mgcohen.workers.dev.</summary>
    public Uri BaseUrl { get; set; } = new("https://inbox.mgcohen.workers.dev");

    /// <summary>Consumer name. If empty, the connector uses the machine name.</summary>
    public string? Consumer { get; set; }

    /// <summary>Bearer token. Comes from user secrets or the INBOX_TOKEN environment variable.</summary>
    public string? Token { get; set; }

    /// <summary>Path of the SQLite file. A relative path starts at the application folder.</summary>
    public string StorePath { get; set; } = "runner-host.db";

    public TimeSpan CatchUpInterval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Maximum handler attempts for one event (contract B).</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Delay before the second attempt. Each next attempt doubles it.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromMinutes(30);

    public TimeSpan ReconnectBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan ReconnectMaxDelay { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Interval of the WebSocket ping frames.</summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>If no pong comes in this time, the connector closes the socket and reconnects.</summary>
    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan AckBaseDelay { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan AckMaxDelay { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>The connector deletes acked rows older than this. The inbox keeps events for 7 days.</summary>
    public TimeSpan StoreRetention { get; set; } = TimeSpan.FromDays(8);

    public string ResolvedConsumer => string.IsNullOrWhiteSpace(Consumer)
        ? ConsumerName.FromMachineName(Environment.MachineName)
        : Consumer;
}

/// <summary>Makes a consumer name that matches <c>^[a-z0-9][a-z0-9_-]{0,63}$</c> (contract A).</summary>
public static partial class ConsumerName
{
    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$")]
    private static partial Regex Pattern();

    public static bool IsValid(string? name) => name is not null && Pattern().IsMatch(name);

    public static string FromMachineName(string machineName)
    {
        var sb = new StringBuilder();
        foreach (var c in machineName.ToLowerInvariant())
        {
            sb.Append(c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-' ? c : '-');
        }

        var name = sb.ToString().TrimStart('-', '_');
        if (name.Length > 64) name = name[..64];
        return name.Length == 0 ? "runner" : name;
    }
}
