namespace Traffic.Presenters;

public sealed record TrafficRowView(long Seq, string Direction, string Kind, string Label, string Status, string Size, string Ms, bool IsError);

// The summary line: "{Matching} of {Kept} kept · {Errors} errors · {KbIn} KB in · {KbOut} KB out".
public sealed record TrafficPageView(IReadOnlyList<TrafficRowView> Entries, int Matching, int Kept, int Errors, double KbIn, double KbOut, bool HasMore,
    IReadOnlyList<string> Kinds);

public sealed record TrafficEntryView(long Seq, string Direction, string Kind, string Host, string Path, string Status, string Size, string Ms, bool IsError,
    long? EventId, string? Body, bool CaptureBodies);
