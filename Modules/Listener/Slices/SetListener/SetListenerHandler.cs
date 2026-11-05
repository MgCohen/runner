using Handling.Infrastructure;
using VSA.Domain;
using VSA.Handlers.Requests;

namespace Listener.Slices.SetListener;

// Saves the listener switch. The socket listener connects or closes, and publishes the new state.
sealed class SetListenerHandler(HandlerSwitches switches) : RoutedHandler<SetListenerRequest, SetListenerResponse> {
    protected override Task<Result<SetListenerResponse>> HandleRequestAsync(SetListenerRequest request, CancellationToken ct) {
        switches.SetListener(request.On);
        return Task.FromResult<Result<SetListenerResponse>>(new SetListenerResponse(switches.Listener));
    }
}
