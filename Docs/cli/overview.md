# CLI Overview

The CLI (`intelligencex`) handles auth, onboarding, reviewer runs, and utilities like usage/credits.

Quickstart: [CLI Quickstart](/docs/cli/quickstart/)

## Common commands

```bash
intelligencex auth login
intelligencex auth export --format store-base64
intelligencex setup autodetect
intelligencex setup wizard
intelligencex setup web
intelligencex reviewer run
intelligencex reviewer resolve-threads
intelligencex todo sync-bot-feedback --repo EvotecIT/IntelligenceX
intelligencex analyze run --config .intelligencex/reviewer.json --out artifacts
intelligencex analyze export-config --out artifacts/analysis-config
intelligencex analyze validate-catalog
intelligencex analyze list-rules --format markdown --pack all-50
intelligencex usage
```

## Run a review locally

```bash
export INPUT_REPO=owner/name
export INPUT_PR_NUMBER=123
intelligencex reviewer run
```

Or point to a GitHub event payload:

```bash
export GITHUB_EVENT_PATH=/path/to/event.json
intelligencex reviewer run
```

## Resolve stale threads locally

```bash
intelligencex reviewer resolve-threads --repo owner/name --pr 123
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
