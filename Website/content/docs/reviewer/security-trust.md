---
title: Reviewer Security & Trust
description: Security model for the IntelligenceX reviewer
collection: docs
layout: docs
nav.weight: 50
---

# Security and Trust Model

## Principles

- No backend service required for onboarding
- Secrets and tokens stay on the user's machine
- GitHub App path is BYO (bring your own) for org trust

## GitHub Auth Modes

- **GitHub App installation token** (recommended for orgs)
- **OAuth device flow** (fastest for single repo)
- **Personal access token** (policy-driven)

## OpenAI Auth

- Uses ChatGPT OAuth (native transport)
- Secret stored in the repo or org as `INTELLIGENCEX_AUTH_B64`
- Optional `INTELLIGENCEX_AUTH_KEY` if you encrypt the local store

## Manual Secret Mode

If you prefer not to upload secrets automatically:

```powershell
intelligencex setup --manual-secret
```

The CLI prints the base64 auth store for manual paste into GitHub secrets.

## What the Tool Changes

- Adds or updates `.github/workflows/review-intelligencex.yml`
- Optionally adds `.intelligencex/config.json`

All changes are made via PRs by default.

For the complete security overview, see [Security & Trust](/docs/security/).
