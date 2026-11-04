# Contracts

Three parts use these contracts. If you change a contract, change all parts in the same PR.

```
Cloudflare: inbox Worker ──contract A──► Runner.Host (connector) ──contract B──► Runner.Handlers
```

| Part | Repository | Owns |
|---|---|---|
| Inbox | `MgCohen/inbox` | Webhooks, D1, hub, `/listen`, settled flag, cleanup |
| Connector | `MgCohen/runner`, `src/Runner.Host` | Socket, catch-up, local store, dispatch, ack |
| Handlers | `MgCohen/runner`, `src/Runner.Handlers` | Business logic and its helpers |

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

`id` increases. It is the dedup key and the order key. C# type: `Runner.Abstractions.InboxEvent`.

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

Types are in `src/Runner.Abstractions`.

```csharp
[On("notion", "page", "properties_updated")]
public sealed class PageUpdated : IEventHandler
{
    public Task HandleAsync(InboxEvent e, HandlerContext context) { ... }
}
```

| Rule | Value |
|---|---|
| Discovery | The connector finds each `IEventHandler` class with `[On]` in `Runner.Handlers` and creates it with dependency injection. |
| Services | `Runner.Handlers` gives an extension `services.AddHandlers(configuration)`. The host calls it. |
| Match | `[On(source, eventType, action)]`. A null value matches any value. One event can match more than one handler. |
| Return | Done. The connector acks the event when all matching handlers are done. |
| Exception | Retry with backoff, maximum 5 attempts. Then the event is failed and acked. |
| `PermanentFailureException` | Failed without a retry, then acked. |
| Duplicates | A handler can get the same event two times. It must accept that. |
| No match | The connector acks the event immediately. |
