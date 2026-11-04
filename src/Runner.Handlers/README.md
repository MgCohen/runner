# Runner.Handlers

The business logic of the runner. The connector (`Runner.Host`) calls `services.AddHandlers(configuration)`. Then it sends each inbox event to the handlers that match it. The rules are in [CONTRACTS.md](../../CONTRACTS.md), contract B.

## Write a handler

1. Add a class to this project that implements `IEventHandler`.
2. Add one `[On(source, eventType, action)]` attribute for each kind of event. A null value matches any value.
3. Get the helpers from the constructor.
4. Return to mark the event as done. Throw to retry. Throw `PermanentFailureException` to stop without a retry.
5. Accept the same event two times. The connector can send it again.

`AddHandlers` finds the class and registers it. You do not register it by hand.

```csharp
[On("github", "pull_request", "opened")]
public sealed class PullRequestOpened(IClaudeLauncher claude) : IEventHandler
{
    public async Task HandleAsync(InboxEvent e, HandlerContext context)
    {
        var title = e.Payload.GetProperty("pull_request").GetProperty("title").GetString();
        await claude.RunPrintAsync(new ClaudeRequest($"Review the PR '{title}'."), context.CancellationToken);
    }
}
```

`LogEverything.cs` is the smallest example.

## Handlers

| Handler | Events | Does |
|---|---|---|
| `LogEverything` | `notion`, all | Writes one log line |
| `NotionPageUpdated` | `notion` `page` `properties_updated`, `content_updated` | Reads the page, starts a Claude session |
| `TaskTodoStarted` | `notion` `page` `properties_updated`, `created` | Starts a Claude session for a task in Todo (see Tasks) |

`NotionPageUpdated` does not start a session in these cases:

- `Enabled` is false. This is the default.
- An author of the edit has the type `bot`. The "Claude workflow" connection is a bot.
- The page parent is not in `DataSourceIds`. An empty list accepts all parents.
- The same page started a session less than `Cooldown` ago.

## Tasks

A task in the Notion database Workflow / Tasks starts a Claude session when its status becomes **Todo**.

```
Todo ──TaskTodoStarted──► In Progress ──TaskSessionWatcher──► In Review ──you──► Done
          │                                   │
          └─ claude --bg --remote-control     └─ session done, blocked or gone, or after MaxRunTime
```

| Step | What the runner does |
|---|---|
| Status becomes Todo | Sets In Progress and `Started`, starts the session with the task page as the prompt, sets `Session` to the claude.ai link |
| Session start fails | Sets Backlog and writes the error in `Result` |
| Every 30 seconds | Reads `claude agents --json` for each running task |
| Session ends | Sets In Review and `Finished`. `Result` gets the last answer of the session. |

- The working folder is the `Folder` property of the task. If it is empty, the runner uses `Handlers:Tasks:WorkingDirectory`, then `Claude:WorkingDirectory`. The folder must be trusted by Claude Code.
- A Notion event does not give the previous status. Each change to Todo starts a session, also from In Review.
- The runner makes its changes as a bot. Bot changes start nothing.
- `tasks.json` (`Handlers:Tasks:StatePath`, default `%LOCALAPPDATA%\Runner\tasks.json`) keeps the running tasks over a restart.
- Settings are in `Handlers:Tasks` (`TaskOptions`): `Enabled` (default true), `DataSourceId`, status names, `PollInterval`, `MaxRunTime` (default 8 hours), `Instructions`.

## Helpers

| Helper | Use |
|---|---|
| `INotionClient` | `GetPageAsync` (properties), `GetPageMarkdownAsync` (content), `UpdatePageAsync` (set properties) |
| `NotionProperties` | Builds property values: `Status`, `Date`, `Url`, `Text` |
| `IClaudeLauncher` | `RunPrintAsync` (`claude -p`), `StartRemoteSessionAsync` (background Remote Control session) |
| `IClaudeAgents` | `ListAsync` (`claude agents --json`), `GetRemoteLinkAsync` (claude.ai link), `ReadLastAnswer` (from the transcript) |

The launcher runs these command lines:

```text
claude -p --permission-mode <mode>                     (prompt on stdin)
claude --bg --remote-control <name> -n <name> --permission-mode <mode> -- <prompt>
```

The launcher refuses `bypassPermissions`.

## Configuration

| Key | Default |
|---|---|
| `Notion:Token` | Value of `NOTION_API_TOKEN` |
| `Notion:Version` | `2026-03-11` |
| `Claude:Path` | `claude` (from the PATH) |
| `Claude:WorkingDirectory` | Current folder. It must be a folder that Claude Code trusts, or `claude --bg` refuses to start. |
| `Claude:PermissionMode` | `auto` |
| `Handlers:NotionPageUpdated:Enabled` | `false` |
| `Handlers:NotionPageUpdated:DataSourceIds` | Empty (all parents) |
| `Handlers:NotionPageUpdated:Mode` | `Background` (or `Print`) |
| `Handlers:NotionPageUpdated:WorkingDirectory` | `Claude:WorkingDirectory` |
| `Handlers:NotionPageUpdated:Cooldown` | `00:05:00` |

Keep the Notion token out of git. Put it in the user secrets of the host project:

```bash
dotnet user-secrets set "Notion:Token" "<token>" --project src/Runner.Host
```
