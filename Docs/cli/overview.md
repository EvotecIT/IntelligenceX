# CLI Overview

The CLI (`intelligencex`) handles auth, onboarding, reviewer runs, and utilities like usage/credits.

Quickstart: `./quickstart.md`

## Common commands

```bash
intelligencex auth login
intelligencex auth export --format store-base64
intelligencex setup wizard
intelligencex setup web
intelligencex reviewer run
intelligencex reviewer resolve-threads
intelligencex usage
```

## Run from source

If the `intelligencex` binary is not on your PATH, run commands with `dotnet run`:

```powershell
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -c Release -- auth login
```

## Auth export for GitHub Secrets

```bash
intelligencex auth export --format store-base64
```

## Auth login (print URL)

```bash
intelligencex auth login --print
```

## Auth sync for Codex CLI

```bash
intelligencex auth sync-codex
```

## Usage and credits

```bash
intelligencex usage --events
```

```bash
intelligencex usage --json --no-cache
```

## GitHub secret upload (optional)

```bash
intelligencex auth login --set-github-secret --repo owner/name --github-token $TOKEN
```
