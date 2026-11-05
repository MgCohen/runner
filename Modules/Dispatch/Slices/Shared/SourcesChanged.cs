using VSA.Contracts.Events;

namespace Dispatch.Slices.Common;

// The data of Home changed: a new event, a finished event or a new source list. A presenter gets only the events of
// its own module, so Dispatch sends this event for the events of the Events and Listener modules.
public sealed record SourcesChanged : IntegrationEvent;
