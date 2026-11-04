# runner

A local .NET service that receives events from the [`inbox`](https://github.com/MgCohen/inbox) Worker and runs your handlers for them.

```
inbox (Cloudflare) ──WebSocket push──► Runner.Host ──► your IEventHandler classes
                   ◄──HTTP ack────────
```

| Project | Purpose |
|---|---|
| `src/Runner.Abstractions` | Shared types: `InboxEvent`, `IEventHandler`, `[On]`, `HandlerContext` |
| `src/Runner.Host` | The connector. One instance on each machine. |
| `src/Runner.Handlers` | Your business logic |

The contracts between the parts are in [CONTRACTS.md](CONTRACTS.md).

## Build

```bash
dotnet build
dotnet test
```
