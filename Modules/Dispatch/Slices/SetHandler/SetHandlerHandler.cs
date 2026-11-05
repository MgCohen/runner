using Dispatch.Slices.Common;
using Handling.Infrastructure;
using VSA.Contracts.Events;
using VSA.Domain;
using VSA.Handlers.Requests;

namespace Dispatch.Slices.SetHandler;

// Handler off: its events are skipped and acked, so it does not block other handlers.
sealed class SetHandlerHandler(HandlerSwitches switches, HandlerCatalog catalog, IIntegrationEventPublisher events) : RoutedHandler<SetHandlerRequest, SetHandlerResponse> {
    protected override async Task<Result<SetHandlerResponse>> HandleRequestAsync(SetHandlerRequest request, CancellationToken ct) {
        if (catalog.Find(request.Key) is null) {
            return new Error("dispatch.not_found", $"No handler has the key '{request.Key}'.");
        }
        switches.SetHandler(request.Key, request.On);
        await events.PublishAsync(new SwitchChanged("handler", request.Key, request.On), ct);
        return new SetHandlerResponse(request.Key, switches.IsHandlerOn(request.Key));
    }
}
