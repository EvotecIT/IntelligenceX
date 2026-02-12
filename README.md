# IntelligenceX

IntelligenceX is a .NET toolkit for the Codex app-server protocol and a GitHub Actions reviewer.
It manages the app-server process, speaks JSON-RPC over JSONL, and ships a CLI/web wizard to onboard
review automation quickly and safely.

Status: Active development | APIs in flux | Actions in beta

## Project Information

[![top language](https://img.shields.io/github/languages/top/EvotecIT/IntelligenceX.svg)](https://github.com/EvotecIT/IntelligenceX)
[![license](https://img.shields.io/github/license/EvotecIT/IntelligenceX.svg)](https://github.com/EvotecIT/IntelligenceX)
[![build](https://github.com/EvotecIT/IntelligenceX/actions/workflows/test-dotnet.yml/badge.svg)](https://github.com/EvotecIT/IntelligenceX/actions/workflows/test-dotnet.yml)

## What it is

- GitHub Actions reviewer (ChatGPT or Copilot provider)
- CLI + local web wizard for onboarding and auth
- .NET library for Codex app-server + Copilot JSON-RPC
- PowerShell module (binary cmdlets)
- Optional tool contract + tool calling, with tool packs now mirrored in-repo under `IntelligenceX.Tools`

## Repo layout

- `EvotecIT/IntelligenceX` (this repo): core library + reviewer + CLI
- `IntelligenceX.Tools/`: in-repo tool packs snapshot (source of current migration work)
- `IntelligenceX.Chat/`: in-repo chat host/service snapshot (source of current migration work)

Standalone `EvotecIT/IntelligenceX.Tools` and `EvotecIT/IntelligenceX.Chat` remain as separate repos temporarily during migration.

## Choose your path

- Reviewer (GitHub Actions): `Docs/reviewer/overview.md`
- CLI tools: `Docs/cli/overview.md`
- .NET library: `Docs/library/overview.md`
- PowerShell module: `Docs/powershell/overview.md`
- Tool packs: `Docs/library/tool-packs.md`

## Quick start (Reviewer)

Recommended onboarding:

```powershell
intelligencex setup wizard
```

Local web UI (preview):

```powershell
intelligencex setup web
```

Docs:
- `Docs/reviewer/onboarding-wizard.md`
- `Docs/reviewer/setup-web.md`
- `Docs/reviewer/security-trust.md`
- `Docs/reviewer/configuration.md`

## Docs index

Start here: `Docs/index.md`

## Roadmaps

- Project roadmap: `TODO.md`

## Trust model (short version)

- BYO GitHub App is supported for branded bot identity.
- Secrets are stored in GitHub Actions (you control access).
- Web UI binds to localhost only; tokens never leave your machine.

## Build

Core CI-equivalent build check (temporary migration baseline):

```powershell
dotnet build IntelligenceX.CI.slnf -c Release
dotnet test IntelligenceX.CI.slnf -c Release

# CI also runs the executable test harness (recommended locally before pushing):
dotnet ./IntelligenceX.Tests/bin/Release/net8.0/IntelligenceX.Tests.dll
dotnet ./IntelligenceX.Tests/bin/Release/net10.0/IntelligenceX.Tests.dll
```

`IntelligenceX.sln` now includes Chat/Tools projects for local integration work. CI intentionally uses `IntelligenceX.CI.slnf` while engine-backed tool projects are being migrated.

Publish the CLI (self-contained single-file):

```powershell
pwsh ./Build/Publish-Cli.ps1 -Runtime win-x64 -Configuration Release -Framework net8.0
```

## License

MIT
