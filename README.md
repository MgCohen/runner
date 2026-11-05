# runner

The local runner of the [`inbox`](https://github.com/MgCohen/inbox) Worker: a Windows tray app that receives the inbox events and runs the handlers for them, in one visible process.

```text
inbox (Cloudflare) ──WebSocket push──► App.Host: Listener ─► Events (store, handler lanes) ─► business handlers
                   ◄──HTTP ack────────                                          tray icon and widget (Blazor)
```

The repository is one VSA app ([MgCohen/vsa-template](https://github.com/MgCohen/vsa-template)). The spec is the Notion page "Spec · Tray widget" (DOC-5). The contracts with the inbox and the handlers are in [CONTRACTS.md](CONTRACTS.md).

| Path | Content |
|---|---|
| `App/Host` | `App.Host`: the one process. The VSA modules, Kestrel on 127.0.0.1, the tray icon, the widget window, the self-test |
| `App/Views` | The widget: Razor pages and components, `wwwroot/themes/*.css` |
| `Modules/Listener` | The socket to the inbox, catch-up, reconnect, ack, the mock inbox |
| `Modules/Events` | Store an event, the handler lanes, retries, clean-up, the event queries |
| `Modules/Dispatch` | The sources, the switches, the handler queries |
| `Modules/Traffic`, `Modules/Settings` | The traffic log; the settings file |
| `Modules/Tasks`, `Modules/Diagnostics` | The business handlers |
| `Modules/MockHandlers` | Test-only handlers, only in mock mode |
| `Shared/Handling` | The event model, the SQLite store, the switches, `InboxHandler`, `HandlerConfig` |
| `Shared/Network`, `Shared/AppSettings` | The traffic sink port; the settings parts of the modules |
| `Shared/Notion`, `Shared/Claude` | The Notion client and the claude CLI, with their mocks |
| `Tests` | NUnit: the VSA architecture rules, the event pipeline in mock mode, the helpers |
| `vendor/vsa` | The VSA packages of vsa-template main (06daea4), before its release |

## Run

```powershell
.\tray.ps1                  # real mode; the listener starts as saved
.\tray.ps1 -Paused          # real mode; the listener starts off
.\tray.ps1 -Mock            # fake inbox, Notion and Claude; nothing leaves the PC
.\tray.ps1 -Mock -SelfTest  # the mock self-test: 29 checks, screenshots, exit code
.\tray.ps1 -SelfTest        # real mode, paused: screenshots of each page only
```

Keep the terminal open: the logs and the output of `claude -p` show there. Stop the app with Exit in the tray menu. The tokens come from 1Password (`deploy/runner.secrets.env`) through `op run`.

The data is in `%LOCALAPPDATA%\Runner` (mock mode: `…\Runner\mock`): `inbox.db`, `switches.json`, `settings.json`, `selftest\`.

## Add a handler

Add a handler slice to a business module: a class that inherits `InboxHandler`, with its `HandlerConfig` and `Run`. The host finds it, Home shows it under its sources, and it gets its own lane. No other code changes. See contract B in [CONTRACTS.md](CONTRACTS.md).

## Build and test

```bash
dotnet build Runner.slnx
dotnet test --solution Runner.slnx
```
