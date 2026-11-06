# CLAUDE.md

This repository is the local runner for the `inbox` Worker (`MgCohen/inbox`): one VSA app (MgCohen/vsa-template) with a tray widget. Read [docs/CONTRACTS.md](docs/CONTRACTS.md) and [README.md](README.md) before you change code. The spec is the Notion page "Spec · Tray widget" (DOC-5).

## Hard rules

**Branch and PR.** Every change ships through a feature branch and a PR. Never commit to `main`.

**VSA rules.** Modules meet only through requests and events, as the VSA layer map permits. Do not add references or `Compile` items to a module project by hand (`VSA0001`, `VSA0006`). A presenter does not publish events (`VSA1004`); it shows them with `IPresenterEvent<TEvent>`.

**One visible process (T1–T3).** The runner runs in `App.Host`, and the user starts it (desktop shortcut or `scripts/tray.ps1`). Start no other process, except `claude -p` and `op read` (the tokens). Do not set `CreateNoWindow` on them: they write to a visible console (`VisibleConsole`). No start at log-on, no hidden process.

**`Program.cs` is the exception.** `src/App/Host/Program.cs` adds the widget window and the tray icon. Change it only for the window, the tray or the start order (spec 4.5).

**Mock first.** Each external service has a mock replacement in the `Configure` of its module (`Runner:Mock`). A change to a page or a handler keeps `.\scripts\tray.ps1 -Mock -SelfTest` green.

**Free only.** No paid services, packages or cloud resources. Local storage is SQLite.

**No secrets in git.** Tokens come from 1Password through `op read` (`src/App/Host/runner.secrets.env` holds references only). Never in code, settings, tests or docs.

**Verify before you claim done.** In `src/`, `dotnet build Runner.slnx` and `dotnet test --solution Runner.slnx` must pass. The mock self-test must also pass.

**App root.** `src/` is the VSA app root: it holds `global.json`, `Runner.slnx` and `.config/dotnet-tools.json`. Run `dotnet` and `vsa` commands in `src/`. From another folder, `dotnet test` does not read the test runner of `global.json` and fails.
