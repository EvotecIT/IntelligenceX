---
title: Security-First Reviewer Setup Checklist
description: Practical security hardening for IntelligenceX reviewer onboarding, secrets, branch protection, and runner strategy.
slug: security-first-reviewer-setup-checklist
collection: blog
layout: page
---

If you are preparing to scale reviewer automation across more repos, this checklist is the fast path to a secure baseline without slowing your team down.

## 1. Start with a Pinned Reusable Workflow

Use a pinned reusable workflow SHA so upgrades are intentional and auditable.

```yaml
jobs:
  review:
    uses: evotecit/github-actions/.github/workflows/review-intelligencex.yml@5f823fad4dbdb34a2de64c741cdc9cdfbcd1e4cf
    with:
      reviewer_source: source
      provider: openai
      model: gpt-5.3-codex
      review_config_path: .intelligencex/reviewer.json
      progress_updates: true
    secrets:
      INTELLIGENCEX_AUTH_B64: ${{ secrets.INTELLIGENCEX_AUTH_B64 }}
      INTELLIGENCEX_GITHUB_APP_ID: ${{ secrets.INTELLIGENCEX_GITHUB_APP_ID }}
      INTELLIGENCEX_GITHUB_APP_PRIVATE_KEY: ${{ secrets.INTELLIGENCEX_GITHUB_APP_PRIVATE_KEY }}
```

## 2. Keep Policy in reviewer.json

Treat JSON as your team policy contract and keep CI wiring out of it.

```json
{
  "review": {
    "mode": "hybrid",
    "length": "medium",
    "style": "direct",
    "reviewDiffRange": "pr-base",
    "reviewThreadsNeedsAttentionSummary": true,
    "reviewThreadsAutoResolveOnEvidence": true
  }
}
```

## 3. Choose Secret Handling Mode Explicitly

Default onboarding can upload secrets for you, but regulated teams often prefer manual flow.

```bash
intelligencex setup wizard --manual-secret
```

Break-glass only (not default guidance): if you need a one-off copy/paste flow, `--manual-secret-stdout` prints sensitive material directly to terminal output.
Use it only in a trusted local session with shell history and terminal logging controls understood.

## 4. Use a Clear Branch Protection Mode

Pick one mode per repo and document it:

- Bot-first automation mode:
  - required check: `review / review`
  - required approving reviews: `0`
  - code owner review requirement: `false`
- Human-gated mode:
  - required check: `review / review`
  - required approving reviews: `1+`
  - code owner review requirement: team dependent

Mixing modes ad hoc is what causes merge confusion.

## 5. Keep Runner Strategy Explicit

Before going public, many teams run private/self-hosted.
After going public, move to GitHub-hosted only if needed and controlled.

In managed setup, keep runner intent visible via repository vars (for example `IX_FORCE_GITHUB_HOSTED`) and avoid hidden behavior changes.

## 6. Turn on SHA Pinning Enforcement (When Ready)

Once your called workflows/actions are fully pinned, enforce it at repo policy level.
This blocks unpinned third-party action references from running.

## 7. Add a Rotation Routine

At minimum, rotate auth bundles and app credentials periodically:

```bash
intelligencex auth login
intelligencex auth export --format store-base64
intelligencex usage --events
```

Track rotation date, owner, and next due date in your team runbook.

## 8. Pre-Go-Live Checklist

Before opening a private repo to public visibility:

1. Confirm reusable workflow SHA is pinned.
2. Confirm `review / review` runs green on a sample PR.
3. Confirm secret scope is minimal and explicit.
4. Confirm branch protection mode is intentional and documented.
5. Confirm SHA pinning enforcement is enabled only after compatibility check.
6. Confirm setup changes are merged via PR, not direct pushes.

## Final Take

Security and agility are not opposites.
If ownership is explicit (YAML wiring, JSON policy, branch protection mode, secret mode), you can move fast without creating blind spots.
