using Listener.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Notion.Infrastructure;
using VSA.Domain;
using VSA.Handlers.Requests;

namespace Listener.Slices.SendMockEvent;

sealed class SendMockEventHandler(IServiceProvider services) : RoutedHandler<SendMockEventRequest, SendMockEventResponse> {
    protected override Task<Result<SendMockEventResponse>> HandleRequestAsync(SendMockEventRequest request, CancellationToken ct) {
        if (services.GetService<MockInbox>() is not { } inbox) {
            return Task.FromResult<Result<SendMockEventResponse>>(new Error("listener.not_mock", "The app does not run in mock mode."));
        }
        if (request.Again is { } again) {
            inbox.SendAgain(again);
            return Task.FromResult<Result<SendMockEventResponse>>(new SendMockEventResponse(again));
        }
        long id;
        switch (request.Kind) {
            case "task":
                if (services.GetService<MockNotion>() is { } notion) {
                    notion.Status = "Todo";
                }
                id = inbox.Send("notion", "page", "properties_updated",
                    $$"""{"type":"page.properties_updated","entity":{"id":"{{MockNotion.TaskPageId}}","type":"page"},"authors":[{"id":"mock-user","type":"person"}]}""");
                break;
            case "github":
                id = inbox.Send("github", "push", null, """{"ref":"refs/heads/main","repository":{"full_name":"mock/repo"}}""");
                break;
            case "custom" when request.Source is { Length: > 0 } source:
                id = inbox.Send(source, request.Type, request.Action, request.Payload ?? "{}", TimeSpan.FromDays(request.AgeDays));
                break;
            default:
                return Task.FromResult<Result<SendMockEventResponse>>(new Error("listener.mock_kind", $"Unknown mock event kind '{request.Kind}'."));
        }
        return Task.FromResult<Result<SendMockEventResponse>>(new SendMockEventResponse(id));
    }
}
