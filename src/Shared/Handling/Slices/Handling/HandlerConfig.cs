namespace Handling.Slices.Handling;

// The config of one InboxHandler. Description is one line for the handler page.
public sealed record HandlerConfig {
    public required IReadOnlyList<string> Sources { get; init; }

    // The types and actions of the sources. The default matches all events of the sources.
    public IReadOnlyList<Match> Matches { get; init; } = [new()];

    public required string Description { get; init; }

    public bool DefaultOn { get; init; } = true;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
}

// One type and action of an event. A null value matches any value.
public sealed record Match(string? Type = null, string? Action = null);
