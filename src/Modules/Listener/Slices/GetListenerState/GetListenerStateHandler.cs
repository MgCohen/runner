using Handling.Infrastructure;
using Listener.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using VSA.Domain;
using VSA.Handlers.Requests;

namespace Listener.Slices.GetListenerState;

sealed class GetListenerStateHandler(HandlerSwitches switches, ListenerStatus status, IOptions<ListenerOptions> options, IConfiguration configuration)
    : RoutedHandler<GetListenerStateRequest, GetListenerStateResponse> {
    protected override Task<Result<GetListenerStateResponse>> HandleRequestAsync(GetListenerStateRequest request, CancellationToken ct) =>
        Task.FromResult<Result<GetListenerStateResponse>>(new GetListenerStateResponse(switches.Listener, status.State.ToString(), status.Reason,
            ConsumerName.Resolve(options.Value.Consumer), StorePaths.IsMock(configuration)));
}
