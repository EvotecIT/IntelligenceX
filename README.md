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
- Optional tool contract + tool calling, with tool packs living in a separate repo (`EvotecIT/IntelligenceX.Tools`)

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

Full build check (includes legacy TFMs on any OS):

```powershell
pwsh ./Build/Build-All.ps1 -Configuration Release
```

## License

MIT
