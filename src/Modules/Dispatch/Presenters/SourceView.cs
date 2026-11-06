namespace Dispatch.Presenters;

public sealed record SourceView(string Name, bool On, int Events, IReadOnlyList<HandlerRowView> Handlers);

// Last: "#id type · HH:mm" or "No events yet".
public sealed record HandlerRowView(string Key, string Name, bool On, string Last);

public sealed record HandlerView(string Key, string Name, bool On, string Description, string Source, string ListensTo, string Last, int Events, int Failed);
