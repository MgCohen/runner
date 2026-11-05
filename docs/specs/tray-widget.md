# Tray widget

The tray widget is the visible control panel of the local runner. It shows the listener, the sources, the handlers, the stored events and the network traffic, and it lets the user turn each one on or off. It is an HTML page in WebView2 inside the `Runner.Tray` process, and the runner runs in the same process.

<p>
<img src="images/home-light.png" width="170" alt="Home">
<img src="images/inbox-light.png" width="170" alt="Inbox">
<img src="images/traffic-light.png" width="170" alt="Traffic">
<img src="images/settings-light.png" width="170" alt="Settings">
</p>

## 1. Properties

| Property | Value |
|---|---|
| ID | SPEC-TRAY-1 |
| Name | Runner Tray (working name, open question 8) |
| Project | Runner |
| Kind | Desktop tray app with a widget window |
| Status | Draft, ready for review |
| Version | 0.3 (design version 9) |
| Owner | MgCohen |
| Usage scope | Personal. One user, one PC, one consumer of the inbox. |
| Platforms | Windows 11 (x64) |
| Stack | .NET 10, WinForms shell, WebView2, HTML + CSS + plain JavaScript, SQLite |
| Repository | `MgCohen/runner`, project `src/Runner.Tray` (private) |
| Install location | `%LOCALAPPDATA%\Runner` (app, store, settings, logs) |
| Start | `tray.ps1` in a visible terminal, by the user |
| Mockup | [tray-widget-mockup.html](tray-widget-mockup.html) (open it in a browser) |
| Related | [CONTRACTS.md](../../CONTRACTS.md), [src/Runner.Tray/README.md](../../src/Runner.Tray/README.md), `MgCohen/inbox` |

## 2. Capabilities

| ID | Capability | What the user can do | Now or later |
|---|---|---|---|
| [C1](#c1-listener) | Listener | Turn the inbox connection on or off, and see its state | Now |
| [C2](#c2-sources-and-handlers) | Sources and handlers | See each source with its handlers, switch each one, open a handler | Now |
| [C3](#c3-inbox) | Inbox | Search and filter the stored events, and open one event | Now |
| [C4](#c4-traffic) | Traffic | Search and filter the calls and socket messages, and open one entry | Now |
| [C5](#c5-settings) | Settings | Change the start, traffic, inbox and appearance settings | Now |
| [C6](#c6-window-and-tray) | Window and tray | Open from the tray, move, pin on top, hide, exit | Now |
| [C7](#c7-theme) | Theme | Use the system, light or dark theme, or a new theme file | Now |
| [C8](#c8-mock-mode-and-self-test) | Mock mode and self-test | Run with no network and no Claude, and save screenshots of each page | Now |
| C9 | Start with Windows | Start the app at log-on | Later |
| C10 | Failure notification | Get a Windows notification when a handler fails | Later |

## 3. Requirements for all capabilities

### 3.1 Usage

| ID | Requirement |
|---|---|
| U1 | One user on one PC. There is no sign-in and no multi-user state. |
| U2 | The user starts the app in a visible terminal. The app runs while the terminal is open. |
| U3 | The widget is a small window near the tray. It stays open until the user hides it. |
| U4 | The widget works with no network. It then shows the listener as "Error" or "Paused". |
| U5 | Opening the widget does not start work by itself. |

### 3.2 Technical

The PC has a CrowdStrike sensor that the PC owner manages. The owner approved a visible app that the user starts. Rules T1 to T3 keep the app inside that approval.

| ID | Requirement | Reason |
|---|---|---|
| T1 | The runner runs in the `Runner.Tray` process. The app starts no other runner process. | A hidden runner process was the chain that the sensor stopped |
| T2 | No hidden windows, no `-ExecutionPolicy Bypass`, no restart loop, no start at log-on | Same reason |
| T3 | `claude -p` is the only child process. Do not set `CreateNoWindow` on it. | Same reason |
| T4 | The UI files are local files of the app. The page loads nothing from the network. | The widget must work offline |
| T5 | Secrets come from 1Password through `op run`. No secret is in git, in settings or in the UI. | Repository rule |
| T6 | Lists load in pages of 50 items. Logs have a fixed capacity. | The UI stays fast when the store has many events |
| T7 | `dotnet build` and `dotnet test` pass, and the self-test passes in mock mode. | Repository rule |

## 4. Architecture

### 4.1 Principles

1. **Modular.** Each module has one job and a public contract. A module does not read the internal state of another module.
2. **Open for extension.** A new source, handler, page, setting or theme is a new unit. It does not change the code of other units.
3. **The host owns the state.** The page shows the state and sends commands. The page saves nothing.
4. **The theme is decoupled.** Components use only design tokens. A theme is one CSS file of tokens (C7).
5. **Contract first.** The UI and the runner meet only through the bridge. The runner and the handlers meet only through `Runner.Abstractions`.
6. **Mock first.** Each external dependency has a replacement for mock mode (C8).

### 4.2 Modules

```
Runner.Tray (process)
├── Shell            window, tray icon, tray menu, WebView2, bridge
├── UI (web files)
│   ├── app          router, state store, command sender
│   ├── pages/       one file for each page
│   ├── components/  switch, chip, filter panel, list row, detail row, badge …
│   └── themes/      default.css, more theme files
├── Runner.Host      listener, store, dispatcher, traffic log, switches
├── Runner.Handlers  business logic, found through [On] attributes
└── Runner.Abstractions  contracts between host and handlers
```

| Module | Owns | Depends on |
|---|---|---|
| Shell | Window, tray icon, bridge | Runner.Host, UI files |
| UI app | Routes, the last `state` message, commands | Bridge |
| UI page | One page: render and actions | UI app, components |
| UI component | One control and its states | Tokens |
| Theme file | Token values | Nothing |
| Runner.Host | Listener, store, dispatch, traffic, switches | Runner.Abstractions |
| Runner.Handlers | Handler logic | Runner.Abstractions |

### 4.3 Bridge

The page and the C# host exchange JSON messages through WebView2 (`postMessage` and `WebMessageReceived`).

| Rule | Detail |
|---|---|
| State snapshot | The host sends `state` at start and after each change: listener, consumer, switches, sources, handlers, settings |
| Commands | The page sends `{ "cmd": name, …fields }`. Each capability lists its commands. |
| Queries | `load…` and `get…` commands get an answer message with the same name root |
| No page state | The page keeps only view state (open page, filters, fold state). It saves nothing. |

| Decision | Reason |
|---|---|
| The runner runs in the tray process | The approved setup is one visible process that the user starts |
| The UI is HTML in WebView2, not WinForms controls | The mockup is the UI, and HTML makes the theme and the layout easy to change |
| Plain JavaScript with no framework and no build step | The UI is small, and the files load offline with no tool chain |

## 5. Capabilities in detail

Each capability has the same parts: **Definition · Functions · UI · Data and commands · Modules · Patterns and decisions · Extension · Acceptance**. The definition says what the capability is. Functions say what it does. UI says where the user sees it.

<details>
<summary><h3 id="c1-listener">C1 · Listener</h3></summary>

> The listener is the live connection from the runner to the inbox Worker. When it is on, the inbox pushes each new event to the runner. When it is off, the events wait in the inbox.

**Functions.**

- Connect to the inbox through the `/listen` WebSocket.
- Receive pushed events, and give them to the dispatcher.
- At each connect, get the events that came while the listener was off (catch-up).
- Pause and resume when the user says so. Show the connection state.
- At start, use the saved state. With `--paused` or `listenerOnStart = false`, start off.

**UI.** The listener is the main control, so its switch and state are in the header of every page.

- **Header, left to right:** listener switch, title "Runner", subtitle, pin, settings, hide (✕).
- **Subtitle:** "Connected · {consumer}", "Connecting…", "Paused" or "Error: {short reason}".

**Data and commands.**

| Item | Source or fields |
|---|---|
| Listener state | `SocketListener.State`: Paused, Connecting, Connected, Error |
| Saved switch | `RunnerSwitches.Listener` (`switches.json`) |
| `setListener` | `on` |

**Modules.** Shell (header actions), UI app (header), Runner.Host (`SocketListener`, `RunnerSwitches`).

**Patterns and decisions.**

| Decision | Reason |
|---|---|
| Listener off = events wait in the inbox, and catch-up runs at the next start | No event is lost while the user pauses |

**Acceptance.**

| # | Given | When | Then | Mode |
|---|---|---|---|---|
| A1 | The app starts with `--paused` | The widget opens | The switch is off, the subtitle says "Paused", and no event runs | Mock |
| A2 | The listener is off, and a task event is in the store | The listener turns on | The task runs to In Review, and the Inbox shows it as done | Mock |
| A3 | The real inbox | The listener turns on | The subtitle shows "Connected", and Traffic shows `connect` and `hello` | Real |
</details>

<details>
<summary><h3 id="c2-sources-and-handlers">C2 · Sources and handlers</h3></summary>

<p>
<img src="images/home-light.png" width="200" alt="Home">
<img src="images/handler-detail-light.png" width="200" alt="Handler detail">
</p>

> A source is an external system that sends webhooks to the inbox, for example Notion or GitHub. A handler is a C# class that runs on some events of a source, for example "start a task when a Notion task goes to Todo".

**Functions.**

- List each source and its handlers.
- Turn each source and each handler on or off.
- Show the last event and the event count of each source and handler.
- Show what one handler listens to, and open its events in the Inbox.

**UI: Home.**

- **Entry:** bottom bar "Home". It is the start page.
- **Layout:** search field "Search sources and handlers", then one foldable card for each source, in the order of the inbox source list.
- **Source card:** chevron, icon, name, "{n} handlers · {n} events", the source switch. The body has one row for each handler: name, "Last: #{id} {type} · {time}", the handler switch, a chevron.
- **Fold state:** sources that have handlers are unfolded at the first open. The app keeps the fold state until it exits.
- **A handler with two or more sources** shows under each of them, with the same switch.
- **Search:** shows only the matching sources and handlers, unfolded.
- **Empty states:** "No handlers yet" for a source with no handlers. "No sources or handlers match" for a search with no match.

**UI: handler detail.**

- **Entry:** a handler row on Home.
- **Layout:** "‹ Home", the name and the switch, a one-line description, a **Details** group (Source · Listens to · Last · Events · Failed), the button "Open in inbox".
- **Description:** handlers have no description in the code yet (open question 2). Until then, the line shows the `[On]` values in words, for example "Runs on notion page events".
- **"Open in inbox":** opens the Inbox with the filter "Handler = this handler".

**Data and commands.**

| Item | Source or fields |
|---|---|
| Source list | `GET /` of the inbox (`{"sources": […]}`), plus `RunnerSwitches.KnownSources` |
| Handler list | `HandlerCatalog`, the `[On]` attributes. Key = class name. |
| Counts and last event | The local store |
| `setSource` | `name`, `on` |
| `setHandler` | `name`, `on` |

**Modules.** UI pages `home` and `handler`, Runner.Host (`HandlerCatalog`, `RunnerSwitches`, store), Runner.Handlers.

**Patterns and decisions.**

| Item | Detail |
|---|---|
| Attribute discovery | `HandlerCatalog` finds each handler through `[On(source, type, action)]` |
| Handler off = the event is skipped and acked | A handler that is off must not block other handlers |
| Source off = the events of that source wait | The user pauses a source, and its events run later |

**Extension.**

| To add | Do this | No change to |
|---|---|---|
| A source | Add it in the inbox Worker | UI, Shell |
| A handler | Add a class with `[On]` in `Runner.Handlers` | UI, Shell, Runner.Host |
| A source icon | Add a name-to-icon entry. Unknown sources get a default icon. | Pages |

**Acceptance.**

| # | Given | When | Then | Mode |
|---|---|---|---|---|
| A4 | A handler is off | An event for it arrives | The event is done, and its detail shows "skipped (off)" for that handler | Mock |
| A5 | A source is off | An event of that source arrives | The event stays pending. When the source turns on, it runs. | Mock |
| A6 | Home has 6 sources | The user searches "pr" | Only the matching handlers and sources show, unfolded | Mock |
| A7 | A handler detail page | The user clicks "Open in inbox" | The Inbox opens with the filter "Handler = that handler" | Mock |
| A8 | A new handler class with `[On]` | The app starts | Home shows it under its source, with no UI change | Mock |
</details>

<details>
<summary><h3 id="c3-inbox">C3 · Inbox</h3></summary>

<p>
<img src="images/inbox-light.png" width="200" alt="Inbox">
<img src="images/inbox-filter-light.png" width="200" alt="Inbox filter panel">
<img src="images/event-detail-light.png" width="200" alt="Event detail">
</p>

> The Inbox shows the events in the local store: each event that the runner received, its state, and the result of each handler on it.

**Functions.**

- List the events, newest first, 50 at a time.
- Find events by text, state, source or handler.
- Show one event: its details, the result of each handler, and its payload.

**UI: Inbox.**

- **Entry:** bottom bar "Inbox", or "Open in inbox" on a handler page.
- **Layout:**
  1. Search field "Search events" and the **Filter** button.
  2. Active filters as chips, each with ×.
  3. Summary line "{shown} of {loaded} loaded · {total} in store".
  4. Event rows, newest first. A row: state dot, "#{id} {source} {type}", "{state} · {handlers}", the time, a chevron.
  5. "Load older events" at the end. It loads the next 50 events.
- **Filter panel:**
  - **State:** new · pending · done · failed, each with its colored dot.
  - **Source:** each source.
  - **Handler:** grouped under the source name. If sources are selected, it shows only their handlers.
  - Each group allows two or more values. Each chip shows its count with the other active filters.
  - **Footer:** "Clear all" · "Done". The footer stays visible when the panel scrolls.
- **Empty states:** "No events yet" for an empty store. "No events match these filters" for no match.

**UI: event detail.**

- **Entry:** an Inbox row, or "Open event #… in inbox" on a traffic detail page.
- **Layout:** "‹ Inbox", "#{id} {source}" and the state badge, the event type, a **Details** group (Received · Attempts · Acked), a **Handlers** group (one row for each handler, with its result), a **Payload** group (the JSON, formatted).
- **Handler results:** "done ({duration})", "failed: {error}", "skipped (off)" or "waiting".
- **Long values** are cut with "…", and the full value shows on hover.

**Data and commands.**

| Item | Source or fields |
|---|---|
| Event | `StoredEvent`: `Event`, `Status`, `Attempts`, `LastError`, `NextRetryAt`, `DoneHandlers`, `AckedAt` |
| State in the UI | new = Pending, 0 attempts · pending = Pending, 1 or more attempts, or waiting for a source · done = Done · failed = Failed |
| Handler result | done = in `DoneHandlers` · skipped = handler off when it ran · failed = `LastError` · waiting = not run yet |
| `loadEvents` → `events` | `filter` {`states`, `sources`, `handlers`, `text`}, `before`, `limit` → events, `total`, `hasMore` |
| `getEvent` → `event` | `id` → one full event |

**Modules.** UI pages `inbox` and `event`, components filter panel and chip, Runner.Host (`EventStore`).

**Patterns and decisions.**

| Item | Detail |
|---|---|
| Paged queries | The host filters in SQLite, and the page asks for 50 events at a time |
| One filter panel component | The Inbox and Traffic use the same component with other groups |

**Acceptance.**

| # | Given | When | Then | Mode |
|---|---|---|---|---|
| A9 | The Inbox has events | The user selects State = failed and Source = notion | Only failed Notion events show, and two chips show | Mock |
| A10 | The filter panel is open with all groups | Its content is taller than 420 | It scrolls, and "Clear all" and "Done" stay visible | Mock |
</details>

<details>
<summary><h3 id="c4-traffic">C4 · Traffic</h3></summary>

<p>
<img src="images/traffic-light.png" width="200" alt="Traffic">
<img src="images/traffic-detail-light.png" width="200" alt="Traffic detail">
</p>

> Traffic is the log of each network call and socket message that the runner sends or receives. It shows what the runner does on the network, with no secrets.

**Functions.**

- Record each HTTP call and each socket message, with its size, duration and result.
- Find entries by text, direction, kind or result.
- Show one entry. For a socket event, open that event in the Inbox.
- Keep the bodies in memory only when the user turns on "Capture bodies".

**UI: Traffic.**

- **Entry:** bottom bar "Traffic".
- **Layout:**
  1. Search field "Search traffic" and the **Filter** button.
  2. Active filter chips.
  3. Summary line "{shown} of {kept} kept · {n} errors · {n} KB in · {n} KB out". The error part is red when it is more than 0.
  4. Entry rows, newest first. A row: direction arrow, "{kind} {target}", "{size} · {ms} ms", or "{status} · {ms} ms" in red for an error.
- **Filter groups:** Direction (in, out) · Kind (event, hello, connect, GET, POST, PATCH and others that occur) · Result (ok, error).
- **Empty state:** "No traffic matches these filters".

**UI: traffic detail.**

- **Layout:** "‹ Traffic", "{kind} {target}" and the status badge, a **Details** group (Direction · Host · Path · Size · Duration).
- **Socket event:** the button "Open event #{id} in inbox".
- **HTTP call, capture bodies on:** a **Response body** group with the kept body.
- **HTTP call, capture bodies off:** "Body not kept. Turn on capture bodies in settings", with a link to Settings.

**Data and commands.**

| Item | Source or fields |
|---|---|
| Entry | `TrafficEntry`: `Time`, `Direction`, `Kind`, `Target`, `Status`, `Size`, `Duration`. Socket entries link to an event ID. |
| `loadTraffic` → `traffic` | `filter` {`directions`, `kinds`, `results`, `text`} → entries, `kept`, totals. The host also sends each new entry. |
| `getTraffic` → `trafficEntry` | `index` → one entry, with the body if one was kept |

**Modules.** UI pages `traffic` and `entry`, Runner.Host (`TrafficLog`, an HTTP handler that records each call).

**Patterns and decisions.**

| Item | Detail |
|---|---|
| Ring buffer | Fixed capacity from `trafficKeep`. The oldest entry goes first. |
| No headers, tokens or query strings | Headers hold tokens |
| Bodies only in memory, only with `captureBodies`, secrets removed | Bodies can hold page content |
| Secret removal | Replace the value of each JSON key that contains `token`, `secret`, `password`, `authorization` or `key` (any case) with `"•••"` |

**Acceptance.**

| # | Given | When | Then | Mode |
|---|---|---|---|---|
| A11 | The Traffic page | The user selects Result = error | Only errors show, and the summary line counts them in red | Mock |
| A12 | "Capture bodies" is off | The user opens an HTTP traffic entry | It says "Body not kept" with a link to Settings | Mock |
</details>

<details>
<summary><h3 id="c5-settings">C5 · Settings</h3></summary>

<img src="images/settings-light.png" width="200" alt="Settings">

> Settings are the options of the app that the user can change. The app keeps them in one file.

**Functions.**

- Show the settings in groups.
- Apply a change at once, and save it.
- Show read-only facts about the app: consumer, inbox URL, store folder, version.

**UI.**

- **Entry:** the settings button in the header. A second click returns to the page before.
- **Layout:** "‹ Back", then the groups At start · Traffic · Inbox · Appearance · About.
- **Controls:** switches for on/off settings, segmented controls for choices, plain text for read-only values, disabled switches with a "later" tag for future settings.
- **A change applies at once,** and the app saves it in the settings file.

**Data and commands.** File: `%LOCALAPPDATA%\Runner\settings.json` (mock mode: `…\Runner\mock\settings.json`).

| Key | Group | Type | Default | Effect | Now or later |
|---|---|---|---|---|---|
| `listenerOnStart` | At start | bool | true | Off: the app starts paused. `--paused` overrides it. | Now |
| `openOnStart` | At start | bool | true | Opens the widget at start | Now |
| `pinOnStart` | At start | bool | false | Starts pinned | Now |
| `startWithWindows` | At start | bool | false | Disabled. Needs the PC owner's OK first (C9). | Later |
| `captureBodies` | Traffic | bool | false | Keeps bodies in memory, max 16 KB each, secrets removed | Now |
| `trafficKeep` | Traffic | 200 · 500 · 1000 | 500 | Capacity of the traffic log | Now |
| `storeDays` | Inbox | 1 · 7 · 30 | 7 | The local store deletes older events | Now |
| `theme` | Appearance | System · Light · Dark · {theme files} | System | Color theme (C7) | Now |
| `notifyOnFailure` | Appearance | bool | false | Disabled (C10) | Later |

- **About group (read-only):** consumer, inbox URL, store folder, version.
- **`setSetting`:** `key`, `value`.

**Modules.** UI page `settings`, Shell (settings file), Runner.Host (reads the settings that it uses).

**Patterns and decisions.**

| Item | Detail |
|---|---|
| Schema-driven form | The host sends the settings schema in `state`. The page renders each row from it. |

**Extension.**

| To add | Do this | No change to |
|---|---|---|
| A setting | Add a schema entry, and read the key where it applies | The Settings page, other settings |
</details>

<details>
<summary><h3 id="c6-window-and-tray">C6 · Window and tray</h3></summary>

> The window is the widget: a small panel that holds all pages. The tray icon in the Windows taskbar opens the window and shows the listener state when the window is hidden.

**Functions.**

- Open and hide the window from the tray icon.
- Move the window, and keep it on top (pin).
- Go between the pages.
- Give a tray menu with the main actions, and exit the app.

**UI.**

| Item | Behavior |
|---|---|
| Size | 350 × 560 logical pixels, fixed. It scales with the display (DPI). |
| Shape | No title bar, rounded corners, a thin border |
| Open | A left click on the tray icon opens the widget, or hides it if it is open |
| Hide | The ✕ button hides it. A click outside the widget does **not** hide it. |
| Move | Drag the header. The switch and the buttons in the header do not start a drag. |
| Pin | The pin button sets "always on top". When on, the button has the accent color. |
| Position | Bottom right of the work area at the first open. After a drag, the app keeps the position until it exits. |
| Navigation | A bottom bar with Home, Inbox and Traffic. Each detail page has a link back. |
| Tray icon | Shows the listener state: green (connected), amber (connecting), gray (paused), red (error) |
| Tray menu | Right click: Show · Listener on/off · (mock mode: Send mock task event, Send mock GitHub event) · Exit |
| Exit | Stops the runner, then closes the app |

**Data and commands.** `setPin` (`on`), `hide`, `dragStart` (the host starts a window drag).

**Modules.** Shell (window, tray icon, tray menu), UI app (router, bottom bar).

**Patterns and decisions.**

| Item | Detail |
|---|---|
| Page registry | The router maps a route name to a page module |
| No hide on a click outside | The user reads the widget while working in other windows |

**Extension.**

| To add | Do this | No change to |
|---|---|---|
| A page | Add a file in `pages/` and register its route | Other pages |

**Acceptance.**

| # | Given | When | Then | Mode |
|---|---|---|---|---|
| A13 | The widget is open | The user clicks outside it | It stays open | Mock |
| A14 | Pin is on | Another window gets the focus | The widget stays on top | Mock |
| A15 | Any page with a scroll bar | The content grows from short to long | No element moves sideways | Mock |
</details>

<details>
<summary><h3 id="c7-theme">C7 · Theme</h3></summary>

<p>
<img src="images/home-light.png" width="200" alt="Home, light">
<img src="images/home-dark.png" width="200" alt="Home, dark">
</p>

> The theme is the set of colors, sizes and fonts of the UI. It is separate from the components, so a new look is one new file.

**Functions.**

- Follow the Windows light or dark mode, or use the theme that the user selects.
- Load a theme file, and use the default value for each token that it does not set.

**UI.** The `theme` setting in Settings: System, Light, Dark, or a theme file.

**Theme system.**

| Rule | Detail |
|---|---|
| Tokens only | Components and pages use only `var(--token)`. No color, radius, space or font size is written in a component. |
| One file for each theme | `themes/{name}.css` sets all tokens. `default.css` has the light and dark values. |
| Theme change | The app sets `data-theme` on the page root, or loads another theme file. Nothing else changes. |
| Fallback | A theme file that does not set a token gets the value from `default.css`. |

**Tokens.**

| Token | Light | Dark | Use |
|---|---|---|---|
| `surface-0` | #f5f4ef | #1f1e1d | Behind the widget |
| `surface-1` | #faf9f5 | #262624 | Cards, groups, hover |
| `surface-2` | #ffffff | #30302e | Widget background |
| `text-primary / secondary / muted` | #141413 / #5e5d59 / #8f8d86 | #f0eee6 / #b8b5ab / #8a877e | Text |
| `accent / success / warning / danger` | #185fa5 / #2f7d32 / #8a5a0b / #b3261e | #85b7eb / #7fc47a / #ef9f27 / #f09595 | States |
| `border / border-strong` | black 10% / 18% | white 10% / 18% | Hairlines, 0.5 px |
| `radius-window / card / control` | 16 / 12 / 8 | same | Corners. Chips and badges are pill-shaped. |
| `font-sans / font-mono` | system UI / Cascadia Mono | same | Text and code |
| `font-body / small / title` | 13 / 11 / 16 px | same | Weights 400 and 500 only |

**Components.**

| Component | States | Notes |
|---|---|---|
| Switch | off, on, disabled | 34 × 20, round knob |
| Icon button | normal, hover, on (accent) | 28 × 28 |
| Search field | empty, typing | Height 32, the same height as the Filter button |
| Filter button | normal, active (accent), with count | Opens the filter panel |
| Filter panel | closed, open, scrolling | Max height 420. The footer stays visible. |
| Chip | off, on (accent) | Label and count |
| Active-filter chip | normal | Label and × |
| Card / group | normal | Surface 1, thin border |
| List row | normal, hover | Fixed height 46 |
| Detail row | normal, hover | Min height 38, label left, value right |
| Badge | done, pending, new, failed, ok, error | Tint background, text in the same hue |
| Segmented control | one option on | For choices such as Theme |

**Layout rules.**

- **Scroll bar:** thin. The page always keeps its gutter (`scrollbar-gutter: stable`), so the content never moves.
- **Icons:** Tabler outline icons, bundled with the app as local files (open question 6).

**Modules.** UI themes and components.

**Patterns and decisions.**

| Item | Detail |
|---|---|
| Design tokens | Each color, radius, space and font size is a CSS variable |
| HTML UI | Makes the theme and the layout easy to change (section 4.3) |

**Extension.**

| To add | Do this | No change to |
|---|---|---|
| A theme | Add a token file in `themes/` | Components, pages |
| A component | Add a file in `components/` that uses only tokens | Themes |

**Acceptance.**

| # | Given | When | Then | Mode |
|---|---|---|---|---|
| A16 | A second theme file | The user selects it | Each page uses its tokens, and no component keeps a color of the old theme | Mock |
</details>

<details>
<summary><h3 id="c8-mock-mode-and-self-test">C8 · Mock mode and self-test</h3></summary>

> Mock mode runs the real app with fake external services, so nothing leaves the PC. The self-test drives the app through fixed steps, checks each result, and saves a screenshot of each step.

**Functions.**

- `--mock`: replace the inbox, Notion and Claude with fakes. The tray menu can send mock events.
- `--selftest`: run the acceptance checks, save a screenshot of each page, and exit.

**UI.** Mock mode shows the mock items in the tray menu. The self-test needs no user action.

- **Folders:** mock data in `%LOCALAPPDATA%\Runner\mock`. Real self-test images in `%LOCALAPPDATA%\Runner\selftest-real`.
- **Commands:** `tray.ps1 -Mock -SelfTest` for the mock tests, `tray.ps1 -SelfTest` for the real tests, with the Claude handlers off.

**Modules.** Runner.Tray (`MockMode`), Runner.Host and Runner.Handlers through dependency injection.

**Patterns and decisions.**

| Item | Detail |
|---|---|
| Dependency injection with replacements | Mock mode replaces the HTTP handlers and `IClaudePrint` |
| Mock mode in the same app | The self-test and the demos use the real UI and the real dispatcher |
| Each acceptance test is a self-test check | Each check saves a screenshot as proof |

**Extension.**

| To add | Do this | No change to |
|---|---|---|
| A mock dependency | Add a replacement in `MockMode.AddMocks` | The real code |
| A test | Add a self-test check | Other checks |

**Acceptance.**

| # | Given | When | Then | Mode |
|---|---|---|---|---|
| A17 | Mock mode | Any test | The traffic log has no host outside `*.mock.invalid`, and no `claude` process starts | Mock |
</details>

## 6. Non-goals

| Non-goal | Reason |
|---|---|
| A remote or web version of the widget | The runner is local, and the approval covers a local visible app |
| Edit or replay of events | The inbox is the record of events. A replay needs its own design. |
| Changes to the inbox Worker | The widget uses the inbox API as it is |
| Other platforms than Windows | The only PC is Windows, and WebView2 is a Windows control |
| A UI framework or a build step | The UI is small, and plain files load offline |

## 7. Open questions

1. **Code interface:** review the handler definition (`[On]`, `IEventHandler`, contracts) before the widget depends on more of it.
2. **Handler description and metadata:** does a handler get a `Description` property or an attribute, and what custom metadata can a handler give to its detail page?
3. **"Skipped" in the store:** the store does not record that a handler was skipped. Add a record, or compute it from the switch history?
4. **Counts:** do the source and handler counts cover all time, the retention period, or today?
5. **Sources with no handlers:** show them, or show only sources that have handlers?
6. **Icons:** bundle the Tabler font, or a small set of SVG icons?
7. **Window position:** keep it after an exit (in the settings file), or reset it at each start?
8. **Name:** the names of the project (Runner) and the app (Runner Tray) are working names.
