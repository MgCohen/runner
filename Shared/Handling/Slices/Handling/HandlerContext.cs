using Handling.Infrastructure;
using Microsoft.Extensions.Logging;
using VSA.Contracts.Events;

namespace Handling.Slices.Handling;

// What the InboxHandler base needs. One for each scope: each lane item has its own scope.
public sealed class HandlerContext(HandlingStore store, HandlerSwitches switches, IIntegrationEventPublisher events, TimeProvider time, ILoggerFactory loggers) {
    internal HandlingStore Store => store;
    internal HandlerSwitches Switches => switches;
    internal IIntegrationEventPublisher Events => events;
    internal TimeProvider Time => time;
    internal ILoggerFactory Loggers => loggers;
}
