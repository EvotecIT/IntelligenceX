---
title: CLI Quick Start
description: Common CLI workflows for IntelligenceX setup and management
collection: docs
layout: docs
nav.weight: 20
---

# CLI Quick Start

## Single Repository (Wizard)

```powershell
intelligencex setup wizard
```

## Web UI (Preview)

```powershell
intelligencex setup web
```

## Single Repository (Non-Interactive)

```powershell
intelligencex setup --repo owner/name --with-config
```

## Update Only the Auth Secret

```powershell
intelligencex setup --repo owner/name --update-secret
```

## One-Step ChatGPT Auth + GitHub Secret Sync

```powershell
intelligencex auth login --set-github-secret
```

Auto-detects repo/org + token if available.

## Manual Secret Flow

```powershell
intelligencex setup --repo owner/name --manual-secret
```

## Explicit Secrets Block (No Inherit)

```powershell
intelligencex setup --repo owner/name --explicit-secrets
```

## Clean Up

Remove the workflow and config (optionally keep the secret):

```powershell
intelligencex setup --repo owner/name --cleanup --keep-secret
```

## Release Notes

Generate release notes:

```powershell
intelligencex release notes --update-changelog
```

## Release Reviewer (Workflow)

Use `.github/workflows/release-reviewer.yml`. Inputs like `release_tag`, `release_title`, `release_repo`, and `rids` map to environment variables. Token env: `INTELLIGENCEX_REVIEWER_TOKEN` (fallback: `INTELLIGENCEX_RELEASE_TOKEN`, `GITHUB_TOKEN`).
