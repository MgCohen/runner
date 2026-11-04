# CLAUDE.md

This repository is the local runner for the `inbox` Worker (`MgCohen/inbox`). Read [CONTRACTS.md](CONTRACTS.md) before you change code.

## Hard rules

**Branch and PR.** Every change ships through a feature branch and a PR. Never commit to `main`.

**Stay in your project.** `src/Runner.Host` is the connector. `src/Runner.Handlers` is business logic. They meet only through `src/Runner.Abstractions`. A change to `Runner.Abstractions` is a contract change: update CONTRACTS.md and both projects in the same PR.

**Free only.** No paid services, packages or cloud resources. Local storage is SQLite.

**No secrets in git.** Use .NET user secrets or environment variables. Never in code, `appsettings.json`, tests or docs.

**Verify before you claim done.** `dotnet build` and `dotnet test` must pass.
