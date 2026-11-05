using Listener.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using VSA.Domain;
using VSA.Handlers.Requests;

namespace Listener.Slices.SetMockNetwork;

sealed class SetMockNetworkHandler(IServiceProvider services) : RoutedHandler<SetMockNetworkRequest, SetMockNetworkResponse> {
    protected override Task<Result<SetMockNetworkResponse>> HandleRequestAsync(SetMockNetworkRequest request, CancellationToken ct) {
        if (services.GetService<MockInbox>() is not { } inbox) {
            return Task.FromResult<Result<SetMockNetworkResponse>>(new Error("listener.not_mock", "The app does not run in mock mode."));
        }
        inbox.SetNetwork(request.Up);
        return Task.FromResult<Result<SetMockNetworkResponse>>(new SetMockNetworkResponse(inbox.IsUp));
    }
}
