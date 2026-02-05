---
title: Reviewer Overview
description: AI-powered PR code review with ChatGPT or GitHub Copilot in GitHub Actions
collection: docs
layout: docs
nav.weight: 10
---

# Reviewer Overview

The reviewer runs in GitHub Actions and posts a structured review comment on PRs. It can use:
- **ChatGPT** (native transport) with a ChatGPT login bundle
- **Copilot** (via Copilot CLI) for teams already using GitHub Copilot

## Recommended Onboarding

- CLI wizard: `intelligencex setup wizard`
- Local web UI (preview): `intelligencex setup web`

## Trust Model

- BYO GitHub App is supported for branded bot identity
- Secrets are stored in GitHub Actions (you control access)
- Web UI binds to localhost only; tokens never leave your machine

## Reusable Workflow (Quick Start)

```yaml
jobs:
  review:
    uses: evotecit/github-actions/.github/workflows/review-intelligencex.yml@master
    with:
      reviewer_source: release
      openai_transport: native
      output_style: claude
      style: colorful
    secrets: inherit
```

## What to Configure Next

- Model/provider + output style
- Review length and strictness
- Auto-resolve/triage behavior for bot threads
- Usage summary line (optional)

## Usage and Credits Line

Enable `reviewUsageSummary` to append limits/credits (ChatGPT native only).  
When a code-review rate-limit window is present, its label is explicitly prefixed with `code review` (for example, `code review weekly limit`) so it is distinct from general limits.

See [Configuration](/docs/reviewer/configuration/) for all options.
