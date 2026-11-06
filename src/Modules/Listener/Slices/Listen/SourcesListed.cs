using VSA.Contracts.Events;

namespace Listener.Slices.Listen;

// The inbox gave its source list (GET /), at each connect.
public sealed record SourcesListed(IReadOnlyList<string> Sources) : IntegrationEvent;
