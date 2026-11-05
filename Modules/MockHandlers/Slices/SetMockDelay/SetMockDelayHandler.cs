using Microsoft.Extensions.DependencyInjection;
using MockHandlers.Infrastructure;
using VSA.Domain;
using VSA.Handlers.Requests;

namespace MockHandlers.Slices.SetMockDelay;

sealed class SetMockDelayHandler(IServiceProvider services) : RoutedHandler<SetMockDelayRequest, MockRunsResponse> {
    protected override Task<Result<MockRunsResponse>> HandleRequestAsync(SetMockDelayRequest request, CancellationToken ct) {
        if (services.GetService<MockControls>() is not { } controls) {
            return Task.FromResult<Result<MockRunsResponse>>(new Error("mock.off", "The app does not run in mock mode."));
        }
        if (request.Handler.Length > 0) {
            controls.SetDelay(request.Handler, TimeSpan.FromMilliseconds(request.Milliseconds));
        }
        return Task.FromResult<Result<MockRunsResponse>>(new MockRunsResponse([.. controls.Order.Select(r => $"{r.Handler}:{r.Event}")]));
    }
}
