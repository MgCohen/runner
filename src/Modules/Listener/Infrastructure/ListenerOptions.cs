using System.Text;
using System.Text.RegularExpressions;

namespace Listener.Infrastructure;

// Settings of the Listener. Section: Modules:Listener.
public sealed class ListenerOptions {
    public Uri BaseUrl { get; set; } = new("https://inbox.mgcohen.workers.dev");

    // The name of this PC at the inbox Worker. Empty: from the machine name.
    public string? Consumer { get; set; }

    public string? Token { get; set; }

    // Off: the app starts paused. --paused overrides it. On: the saved switch.
    public bool ListenerOnStart { get; set; } = true;

    public TimeSpan CatchUpInterval { get; set; } = TimeSpan.FromMinutes(30);

    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

// A consumer name that matches ^[a-z0-9][a-z0-9_-]{0,63}$ (contract A).
public static partial class ConsumerName {
    public static bool IsValid(string? name) => name is not null && Pattern().IsMatch(name);

    public static string Resolve(string? consumer) => string.IsNullOrWhiteSpace(consumer) ? FromMachineName(Environment.MachineName) : consumer;

    public static string FromMachineName(string machineName) {
        ArgumentNullException.ThrowIfNull(machineName);
        var name = new StringBuilder();
        foreach (var c in machineName.ToLowerInvariant()) {
            name.Append(c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-' ? c : '-');
        }
        var text = name.ToString().TrimStart('-', '_');
        if (text.Length > 64) {
            text = text[..64];
        }
        return text.Length == 0 ? "runner" : text;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$")]
    private static partial Regex Pattern();
}
