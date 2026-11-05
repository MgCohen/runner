using Events.Slices.Common;
using Listener.Infrastructure;
using VSA.Handlers.Events;

namespace Listener.Slices.Listen;

// Step 7 of the event pipeline: an event is finished, so the ack service sends its ack.
sealed class AckOnFinish(AckSignal signal) : IntegrationEventHandler<EventFinished> {
    public override Task HandleAsync(EventFinished integrationEvent, CancellationToken ct) {
        signal.Set();
        return Task.CompletedTask;
    }
}
