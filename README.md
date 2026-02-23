# IntelligenceX

Zero-trust AI platform with code reviews, desktop chat, tool packs, and developer libraries.  
Your credentials, your GitHub App, your control.

[![top language](https://img.shields.io/github/languages/top/EvotecIT/IntelligenceX.svg)](https://github.com/EvotecIT/IntelligenceX)
[![license](https://img.shields.io/github/license/EvotecIT/IntelligenceX.svg)](https://github.com/EvotecIT/IntelligenceX)
[![build](https://github.com/EvotecIT/IntelligenceX/actions/workflows/test-dotnet.yml/badge.svg)](https://github.com/EvotecIT/IntelligenceX/actions/workflows/test-dotnet.yml)

## Platform Areas

- GitHub Actions Reviewer
- IX Chat (Windows desktop tray app)
- IX Tools (tool packs for chat and integrations)
- CLI tools (setup/auth/ops workflows)
- .NET library
- PowerShell module
- Issue Ops + Project Control

Note: some tool packs are private/licensed by default depending on deployment and usage.

## Quick Start

Recommended onboarding:

```powershell
intelligencex setup wizard
```

Local web setup flow:

```powershell
intelligencex setup web
```

From source:

```powershell
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -c Release -- setup wizard
```

## Trust Model

- No backend service: IntelligenceX runs locally and/or in your GitHub Actions.
- Secrets stay under your control: stored in the environments you own.
- Bring your own GitHub App for identity, permissions, and auditability.
- Workflow changes happen via PRs so setup changes stay reviewable.

## Documentation

- Start here: `Docs/getting-started.md`
- Reviewer: `Docs/reviewer/overview.md`
- IX Chat: `Docs/chat/overview.md`
- Tools: `Docs/tools/overview.md`
- CLI: `Docs/cli/overview.md`
- .NET library: `Docs/library/overview.md`
- PowerShell: `Docs/powershell/overview.md`
- Project Ops: `Docs/project-ops/overview.md`
- Security: `Docs/security.md`

## Repository Layout

- `IntelligenceX/`: core library
- `IntelligenceX.Reviewer/`: review pipeline executable
- `IntelligenceX.Cli/`: setup/auth/ops commands
- `IntelligenceX.Chat/`: desktop chat app, host, service, client
- `IntelligenceX.Tools/`: in-repo tool packs and contracts
- `Docs/`: source docs (published to the website)

## Build

Core CI-equivalent build check:

```powershell
dotnet build IntelligenceX.CI.slnf -c Release
dotnet test IntelligenceX.CI.slnf -c Release

# CI also runs the executable test harness:
dotnet ./IntelligenceX.Tests/bin/Release/net8.0/IntelligenceX.Tests.dll
dotnet ./IntelligenceX.Tests/bin/Release/net10.0/IntelligenceX.Tests.dll
```

`IntelligenceX.sln` includes Chat/Tools projects for local integration work.

Publish CLI (self-contained single-file):

```powershell
pwsh ./Build/Publish-Cli.ps1 -Runtime win-x64 -Configuration Release -Framework net8.0
```

## License

MIT
