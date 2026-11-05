using Dispatch.Domain;
using Handling.Infrastructure;
using VSA.Domain;
using VSA.Handlers.Requests;

namespace Dispatch.Slices.GetHandler;

sealed class GetHandlerHandler(HandlingStore store, HandlerCatalog catalog, HandlerSwitches switches) : RoutedHandler<GetHandlerRequest, GetHandlerResponse> {
    protected override Task<Result<GetHandlerResponse>> HandleRequestAsync(GetHandlerRequest request, CancellationToken ct) {
        if (catalog.Find(request.Key) is not { } handler) {
            return Task.FromResult<Result<GetHandlerResponse>>(new Error("dispatch.not_found", $"No handler has the key '{request.Key}'."));
        }
        var stats = store.StatsOf(handler.Key);
        return Task.FromResult<Result<GetHandlerResponse>>(new GetHandlerResponse(handler.Key, handler.Name, switches.IsHandlerOn(handler.Key), handler.Description,
            handler.Sources, LastText.ListensTo(handler), LastText.Of(stats.Last), stats.Events, stats.Failed));
    }
}
