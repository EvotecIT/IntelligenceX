# CLI Quickstart

## Single repository (wizard)

```powershell
intelligencex setup wizard
```

## Web UI (preview)

```powershell
intelligencex setup web
```

## Single repository (non-interactive)

```powershell
intelligencex setup --repo owner/name --with-config
```

## Update only the auth secret

```powershell
intelligencex setup --repo owner/name --update-secret
```

## One-step ChatGPT auth login + GitHub secret sync

```powershell
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -c Release -- auth login --set-github-secret
```

The command auto-detects repo/org + token if available (see README for details).

## Manual secret flow

```powershell
intelligencex setup --repo owner/name --manual-secret
```

## Explicit secrets block (no inherit)

```powershell
intelligencex setup --repo owner/name --explicit-secrets
```

## Clean up

```powershell
intelligencex setup --repo owner/name --cleanup --keep-secret
```

## Release notes (CLI)

```powershell
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -c Release -- release notes --update-changelog
```

## Release notes (workflow)

Use `.github/workflows/release-notes.yml` (template in `IntelligenceX.Cli/Templates/release-notes.yml`).
Inputs are mapped to environment variables to keep YAML minimal.

## Release reviewer (workflow)

Use `.github/workflows/release-reviewer.yml`.
Inputs like `release_tag`, `release_title`, `release_repo`, and `rids` map to env vars.
Token env: `INTELLIGENCEX_REVIEWER_TOKEN` (fallback: `INTELLIGENCEX_RELEASE_TOKEN`, `GITHUB_TOKEN`).
