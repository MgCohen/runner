using VSA.Contracts.Events;

namespace Traffic.Slices.RecordTraffic;

// A new traffic entry. A presenter event for the Traffic page.
public sealed record TrafficRecorded(long Seq) : IntegrationEvent;
