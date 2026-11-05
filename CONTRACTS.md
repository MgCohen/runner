# Contracts

Three parts use these contracts. If you change a contract, change all parts in the same PR.

```
Cloudflare: inbox Worker ──contract A──► Listener and Events modules ──contract B──► business modules (InboxHandler)
```

| Part | Repository | Owns |
|---|---|---|
| Inbox | `MgCohen/inbox` | Webhooks, D1, hub, `/listen`, settled flag, cleanup |
| Connector | `MgCohen/runner`, `Modules/Listener`, `Modules/Events`, `Shared/Handling` | Socket, catch-up, local store, handler lanes, ack |
| Handlers | `MgCohen/runner`, business modules (`Modules/Tasks` …) | Business logic and its helpers |

## Contract A: inbox to connector

### Event shape

The live push and `GET /api/events?payload=1` give each event in this shape:

```json
{
  "id": 41,
  "source": "notion",
  "delivery_id": "367cba44-b6f3-4c92-81e7-6a2e9659efd4",
  "event_type": "page",
  "action": "properties_updated",
  "received_at": "2026-11-04T11:26:07.755Z",
  "payload": { "raw": "source body" }
}
```

`id` increases. It is the dedup key and the order key. C# type: `Handling.Slices.Handling.InboxEvent` (`Shared/Handling`).

### Live push

| Item | Value |
|---|---|
| URL | `wss://inbox.mgcohen.workers.dev/listen?consumer=<name>` |
| Auth | `Authorization: Bearer <INBOX_TOKEN>` on the upgrade request |
| Consumer name | Same rule as the API: `^[a-z0-9][a-z0-9_-]{0,63}$` |
| Server to client | `{"kind":"hello","consumer":"<name>"}` when connected. `{"kind":"event","event":<event shape>}` for each new event. |
| Client to server | Nothing. The ack uses HTTP. |
| Delivery | At most once, to each socket that is open. A push is a signal, not a guarantee. |

### Catch-up and ack

The API in the inbox README stays the same, with one addition:

| Call | Use |
|---|---|
| `GET /api/events?consumer=<name>&pending=1&payload=1&after=<id>` | Catch-up at start, after a reconnect, and every 30 minutes. It does not return settled events. |
| `GET /api/events?...&settled=1` | Also returns settled events. |
| `POST /api/consume` `{"consumer":"<name>","ids":[...]}` | Ack after the handler succeeds. |

### Lifecycle

| State | Rule | In `pending=1`? |
|---|---|---|
| Received | No consumer | Yes |
| Consumed | First consume less than 1 hour ago | Yes, for other consumers |
| Settled | First consume 1 hour ago or more | No |
| Deleted | `received_at` 7 days ago or more, consumed or not | No. The row is gone. |

## Contract B: connector to handlers

A handler is a VSA event handler slice in a business module. It inherits `InboxHandler` (`Shared/Handling`).

```csharp
// Modules/Tasks/Slices/StartTodoTask/TaskTodoStarted.cs
sealed class TaskTodoStarted(HandlerContext context, INotionClient notion /* , … */) : InboxHandler(context) {
    public override HandlerConfig Config { get; } = new() {
        Sources = ["notion"],
        Matches = [new Match("page", "properties_updated"), new Match("page", "created")],
        Description = "Starts Claude when a Notion task goes to Todo",
    };

    protected override async Task Run(InboxEvent e, CancellationToken ct) { /* … */ }
}
```

| Rule | Value |
|---|---|
| Discovery | The VSA host registers each `InboxHandler` subclass. The handler catalog reads the `HandlerConfig` of each one. The key of a handler is its full type name. |
| Match | `Sources` and `Matches` (type and action). A null type or action matches any value. One event can match more than one handler. |
| Lanes | Each handler has one lane. A lane runs one item at a time, oldest first (received time, then ID). A slow handler delays only its own lane. |
| Return | Done. The event is acked when each matching handler has a final result. |
| Exception | Retry with backoff (30 s, doubled, up to 30 min), maximum 5 attempts. Then the failure is final, the event is Failed and acked. |
| Timeout | `HandlerConfig.Timeout` (default 10 min). A timeout is a failed attempt with the reason "timeout". |
| `PermanentFailureException` | Failed at once, with no retry, then acked. |
| Exit | The running handler is cancelled. The result is "cancelled", the item stays pending and does not count as an attempt, and the event runs at the next start. |
| Duplicates | The store drops a known event ID. A duplicate of a finished event is acked again. A handler can still get the same event two times; it must accept that. |
| No match | The event is done and acked at once. |
| Handler off | The handler skips the event ("skipped (off)"). The event is done and acked without it. Turning the handler on again does not run it for earlier events. |
| Source off | Each handler skips the events of the source ("skipped (source off)"). Only the listener switch makes events wait. |
| Output | A handler writes to its logger. `claude -p` writes its output to the window that started the app. |
