using Handling.Domain;
using Microsoft.Extensions.DependencyInjection;
using VSA.Handlers.Events;

namespace Handling.Slices.Handling;

// Reads the config of each registered InboxHandler, in a new scope.
public static class HandlerInfos {
    public static IReadOnlyList<HandlerInfo> Load(IServiceProvider services) {
        using var scope = services.CreateScope();
        return [.. scope.ServiceProvider.GetServices<IntegrationEventHandler<InboxEventDispatched>>().OfType<InboxHandler>().Select(h => h.Info)];
    }
}
