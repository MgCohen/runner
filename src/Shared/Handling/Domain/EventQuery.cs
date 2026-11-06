namespace Handling.Domain;

// A page of the stored events, newest first (received time, then ID). Before: an event ID; the page starts after it.
// Values in one group combine with OR, and groups combine with AND. Text matches "#id source type", case-insensitive.
public sealed record EventQuery(IReadOnlyList<string> States, IReadOnlyList<string> Sources, IReadOnlyList<string> Handlers, string? Text, long? Before, int Limit);

public sealed record EventRow(long Id, string Source, string TypeText, DateTimeOffset ReceivedAt, string State, IReadOnlyList<string> Handlers);

// Facets: the count of each chip value, with the other active filters. The keys are the group names: state, source, handler.
public sealed record EventPage(IReadOnlyList<EventRow> Events, int Matching, int Total, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Facets, bool HasMore);

// The counts of one handler in the store.
public sealed record HandlerStats(string HandlerKey, int Events, int Failed, EventRecord? Last);
