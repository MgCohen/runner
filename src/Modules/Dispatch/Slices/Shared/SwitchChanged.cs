using VSA.Contracts.Events;

namespace Dispatch.Slices.Common;

// A source or a handler switch changed. Kind: source or handler. Name: the source name or the handler key.
public sealed record SwitchChanged(string Kind, string Name, bool On) : IntegrationEvent;
