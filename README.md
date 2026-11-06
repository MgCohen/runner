# runner

The local runner of the [`inbox`](https://github.com/MgCohen/inbox) Worker: a Windows tray app that receives the inbox events and runs the handlers for them, in one visible process.

```text
inbox (Cloudflare) ──WebSocket push──► App.Host: Listener ─► Events (store, handler lanes) ─► business handlers
                   ◄──HTTP ack────────                                          tray icon and widget (Blazor)
```

The repository is one VSA app ([MgCohen/vsa-template](https://github.com/MgCohen/vsa-template)). The spec is the Notion page "Spec · Tray widget" (DOC-5). The contracts with the inbox and the handlers are in [docs/CONTRACTS.md](docs/CONTRACTS.md).

| Path | Content |
|---|---|
| `src/App/Host` | `App.Host`: the one process. The VSA modules, Kestrel on 127.0.0.1, the tray icon, the widget window, the self-test |
| `src/App/Views` | The widget: Razor pages and components, `wwwroot/themes/*.css` |
| `src/Modules/Listener` | The socket to the inbox, catch-up, reconnect, ack, the mock inbox |
| `src/Modules/Events` | Store an event, the handler lanes, retries, clean-up, the event queries |
| `src/Modules/Dispatch` | The sources, the switches, the handler queries |
| `src/Modules/Traffic`, `src/Modules/Settings` | The traffic log; the settings file |
| `src/Modules/Tasks`, `src/Modules/Diagnostics` | The business handlers |
| `src/Modules/MockHandlers` | Test-only handlers, only in mock mode |
| `src/Shared/Handling` | The event model, the SQLite store, the switches, `InboxHandler`, `HandlerConfig` |
| `src/Shared/Network`, `src/Shared/AppSettings` | The traffic sink port; the settings parts of the modules |
| `src/Shared/Notion`, `src/Shared/Claude` | The Notion client and the claude CLI, with their mocks |
| `src/tests` | NUnit: the VSA architecture rules, the event pipeline in mock mode, the helpers |
| `scripts` | `install.ps1` and `tray.ps1` |
| `docs` | `CONTRACTS.md`: the contracts with the inbox and the handlers |

`src/` is the VSA app root: it holds `global.json`, `Runner.slnx` and `.config/dotnet-tools.json`. `nuget.config` is at the repository root.

## Install and use

Install the runner once, and again after each code change:

```powershell
.\scripts\install.ps1
```

`install.ps1` publishes `App.Host` as one file to `%USERPROFILE%\Apps\Runner` and adds a "Runner" shortcut to the desktop and to the Start menu. Start the runner with the shortcut. It opens in the tray, with no terminal.

The app is not in `AppData`. A packaged app, for example the Claude desktop app, writes `AppData` to its own private folder, and Explorer does not see files there. If such an app runs `install.ps1`, the Start menu shortcut also goes to that private folder. The desktop shortcut works in all cases.

| Item | How it works |
|---|---|
| Tokens | Real mode: the app runs `op read` for each reference in `runner.secrets.env` (`src/App/Host`). It needs `OP_SERVICE_ACCOUNT_TOKEN` in the user environment. |
| `claude -p`, `op read` | The app opens a visible console window while they run, and closes it after (T3) |
| Log | `logs\runner-YYYY-MM-DD.log` in the data folder, one file for each day. Each start adds to the file of the day. The app deletes a file after 14 days. A start error goes to `%LOCALAPPDATA%\Runner\crash.log` and to a message box. |
| Mode | Real or mock. Change it in Settings → App → Mock mode, in the tray menu, or with the CLI. A change restarts the app. The app keeps the mode after a restart. |
| Data | `%LOCALAPPDATA%\Runner` (mock mode: `…\Runner\mock`): `inbox.db`, `switches.json`, `settings.json`, `logs\`, `WebView2\`. The mock self-test uses `%LOCALAPPDATA%\Runner\selftest-mock`. |

The installed app is one file: `install.ps1` publishes with `PublishSingleFile`. The virus scanner examines each new DLL when a process loads it, and the source has more than 100 project DLLs. The bundle keeps each project as its own assembly inside `App.Host.exe`, so the scanner examines one file. The source, the tests and `tray.ps1` keep the projects. A one-file app has no `deps.json` that VSA module discovery can read, so the build records the module roots in `App.Host` (`ModuleList`). Then `install.ps1` copies only the files that changed and runs `App.Host.exe warmup`, so the scan occurs during the install. While the app starts, the tray shows a yellow Runner icon with the tooltip "Runner: starting". A click on it, or a second click on the shortcut, shows the start message.

A shortcut start gets the PATH of Explorer, which can be older than the saved PATH. The app adds the saved machine and user PATH entries, so it finds `op` and `claude`.

## CLI

Commands go to the app that runs:

```powershell
$runner = "$env:USERPROFILE\Apps\Runner\App.Host.exe"
& $runner status | Out-String     # real|mock, the listener state, the process ID, the data folder
& $runner mode mock | Out-String  # saves the mode, restarts the app in it, and shows the new status
& $runner mode real | Out-String
& $runner show | Out-String       # opens the widget
& $runner quit | Out-String       # stops the app
```

`| Out-String` makes PowerShell wait for the command and show its output.

## Run from the source

The VSA packages (vsa-template `0.1.0-preview.5`) come from GitHub Packages. GitHub Packages needs a token, also for public packages: `nuget.config` reads it from `GITHUB_TOKEN`. If `GITHUB_TOKEN` is empty, `tray.ps1` and `install.ps1` use `gh auth token`. The `gh` token needs the `read:packages` scope (`gh auth refresh -s read:packages`). For `dotnet build` and `dotnet test`, set it first:

```powershell
$env:GITHUB_TOKEN = gh auth token
```

```powershell
.\scripts\tray.ps1                  # real mode; the listener starts as saved
.\scripts\tray.ps1 -Paused          # real mode; the listener starts off
.\scripts\tray.ps1 -Mock            # fake inbox, Notion and Claude; nothing leaves the PC
.\scripts\tray.ps1 -Mock -SelfTest  # the mock self-test: 29 checks, screenshots, exit code
.\scripts\tray.ps1 -SelfTest        # real mode, paused: screenshots of each page only
```

A start from `tray.ps1` shows the log and the output of `claude -p` in that terminal. It does not use or change the saved mode. One app runs at a time: if the installed app runs, `tray.ps1` only shows its window. The self-test runs apart from the app.

## Add a handler

Add a handler slice to a business module: a class that inherits `InboxHandler`, with its `HandlerConfig` and `Run`. The host finds it, Home shows it under its sources, and it gets its own lane. No other code changes. See contract B in [docs/CONTRACTS.md](docs/CONTRACTS.md).

## Build and test

Run the commands in `src/`. From another folder, `dotnet test` does not read the test runner of `global.json` and fails.

```powershell
cd src
dotnet build Runner.slnx
dotnet test --solution Runner.slnx
```
