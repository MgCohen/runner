# Runner.Tray

A Windows tray app that runs the runner in its own process. The user starts it with `tray.ps1`. A small dark widget shows the status, the switches, the activity and the network traffic.

```
tray.ps1 ──op run──► dotnet run ──► Runner.Tray process
                                     ├─ WinForms message loop: tray icon, widget
                                     └─ generic host: AddRunnerHost + AddHandlers (same services as Runner.Host)
                                          └─ claude -p (child, only from ClaudePrint)
```

## Start and stop

| Action | How |
|---|---|
| Start | `.\tray.ps1` in a PowerShell window. Keep the window open. |
| Open the widget | Click the tray icon, or tray menu **Show** |
| Hide the widget | **✕** in the header, or Esc |
| Stop | Tray menu **Exit**, or Ctrl+C in the window of `tray.ps1` |

The app starts no other runner process. It does not start at log-on and does not restart itself. The console window of `tray.ps1` stays open: it shows the logs, and `claude -p` uses it.

Do not run `tray.ps1` and `host.ps1` at the same time. They use the same consumer and the same local store.

## Widget

| Part | Contents |
|---|---|
| Header | Status dot, consumer name, **Listener** switch. Drag the header to move the widget. |
| Sources | A switch for `notion`, `github` and `linear` |
| Handlers | A switch for each handler class in the handler catalog |
| Activity | Log lines and `claude -p` output, newest at the bottom, last 1,000 lines |
| Traffic | Time, direction, kind, target, status, size and duration, newest first, last 500 items |
| Footer | **Always on top** switch |

| Status dot and tray icon | Meaning |
|---|---|
| Green | Connected |
| Amber | Connecting |
| Gray | Paused: the listener is off |
| Red | Error: the socket failed or the runner did not start |

## Switches

The switches are in `%LOCALAPPDATA%\Runner\switches.json`. "Off" pauses the work. An event is never lost.

| Switch off | Effect | Switch on |
|---|---|---|
| Listener | The socket closes and does not connect again. The catch-up does not run. The dispatcher runs no handlers. Events wait in the inbox. | The socket connects. The catch-up runs. |
| Source | Its events stay pending in the local store. No run, no ack. | The pending events run. |
| Handler | It is skipped. The other handlers of the event run, and the event is acked. | The handler runs for new events. It does not run for the events that it skipped. |

Defaults: listener on, all sources on, all handlers on, except `NotionPageUpdated` off.

- The settings of the environment also apply. For example, `NotionPageUpdated` starts Claude only if `Handlers:NotionPageUpdated:Enabled` is true.
- If the listener is off, the ack service still acks the events that are finished.
- The inbox deletes events after 7 days. An event that waits for a source or for the listener stays in the local store.

## Traffic

The traffic log records each HTTP call of the host and the handlers, and the socket connect, close and messages. It records no headers, tokens, query strings or bodies.

## Build

```bash
dotnet build src/Runner.Tray
```
