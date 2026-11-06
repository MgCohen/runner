namespace Handling.Domain;

// One type and action that a handler listens to. A null value matches any value.
public sealed record EventMatch(string? Type, string? Action) {
    public bool Matches(string? type, string? action) =>
        (Type is null || string.Equals(Type, type, StringComparison.OrdinalIgnoreCase))
        && (Action is null || string.Equals(Action, action, StringComparison.OrdinalIgnoreCase));
}

// The catalog row of one handler. Key is the full type name of the handler class.
public sealed record HandlerInfo(string Key, string Name, IReadOnlyList<string> Sources, IReadOnlyList<EventMatch> Matches, string Description, bool DefaultOn, TimeSpan Timeout) {
    public bool Accepts(EventRecord e) =>
        Sources.Contains(e.Source, StringComparer.OrdinalIgnoreCase) && Matches.Any(m => m.Matches(e.Type, e.Action));

    public bool Listens(string source) => Sources.Contains(source, StringComparer.OrdinalIgnoreCase);
}
