namespace Events.Presenters;

// Handlers: the matching handler names, comma-separated, or "no handler".
public sealed record EventRowView(long Id, string Source, string Type, string State, DateTimeOffset ReceivedAt, string Handlers);

public sealed record HandlerOptionView(string Key, string Name, IReadOnlyList<string> Sources);

public sealed record EventView(long Id, string Source, string Type, string State, DateTimeOffset ReceivedAt, int Attempts, DateTimeOffset? AckedAt,
    IReadOnlyList<(string Handler, string Result)> Results, string Payload);
