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
| `src/Runner.Cli` | `runner tasks`: runs the Notion Todo tasks in your terminal. Start it with `tasks.ps1`. |

The contracts between the parts are in [CONTRACTS.md](CONTRACTS.md).

## Run the Notion tasks yourself

```powershell
.\tasks.ps1          # asks before each Todo task, then runs claude -p in this window
.\tasks.ps1 --list   # lists the Todo tasks
.\tasks.ps1 --yes    # runs each Todo task without a question
```

For each task it sets In Progress, runs `claude -p` with the task page as the prompt, and sets In Review with the answer in `Result`. If Claude fails, the task goes back to Backlog. The Notion token comes from 1Password through `op run`.

## Build

```bash
dotnet build
dotnet test
```
