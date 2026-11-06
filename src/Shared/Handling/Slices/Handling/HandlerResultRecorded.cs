using VSA.Contracts.Events;

namespace Handling.Slices.Handling;

// An InboxHandler wrote its result row. Result: done, skipped, failed or cancelled. Permanent: a failure with no retry.
public sealed record HandlerResultRecorded(long InboxId, string HandlerKey, string Result, bool Permanent) : IntegrationEvent;
