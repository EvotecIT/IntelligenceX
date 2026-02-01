# CLI Overview

The CLI (`intelligencex`) handles auth, onboarding, reviewer runs, and utilities like usage/credits.

Quickstart: `Docs/cli/quickstart.md`

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

## Auth export for GitHub Secrets

```bash
intelligencex auth export --format store-base64
```

## Usage and credits

```bash
intelligencex usage --events
```

## GitHub secret upload (optional)

```bash
intelligencex auth login --set-github-secret --repo owner/name --github-token $TOKEN
```
