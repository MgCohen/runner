using Dispatch.Slices.Common;
using Handling.Infrastructure;
using VSA.Contracts.Events;
using VSA.Domain;
using VSA.Handlers.Requests;

namespace Dispatch.Slices.SetSource;

// Source off: the events of the source are skipped and acked. They do not run when the source turns on again.
sealed class SetSourceHandler(HandlerSwitches switches, IIntegrationEventPublisher events) : RoutedHandler<SetSourceRequest, SetSourceResponse> {
    protected override async Task<Result<SetSourceResponse>> HandleRequestAsync(SetSourceRequest request, CancellationToken ct) {
        switches.SetSource(request.Name, request.On);
        await events.PublishAsync(new SwitchChanged("source", request.Name, request.On), ct);
        return new SetSourceResponse(request.Name, switches.IsSourceOn(request.Name));
    }
}
