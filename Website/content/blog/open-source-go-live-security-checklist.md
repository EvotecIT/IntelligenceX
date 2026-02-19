---
title: Open-Source Go-Live Security Checklist (GitHub Actions)
description: A practical checklist to harden CI/CD, secrets, and branch protection before opening a repository to the public.
slug: open-source-go-live-security-checklist
collection: blog
layout: page
---

Opening a repository to the public is not only a visibility change. It changes your threat model, automation exposure, and incident response expectations.

Use this checklist to go live safely without slowing delivery.

## Prerequisites

- GitHub branch protection is enabled on your default branch.
- You can update repository secrets and variables.
- You have at least one passing CI run from a recent pull request.

## 1. Lock Down Workflow Permissions

Use least privilege at workflow level, then elevate per job only when needed.

```yaml
name: review
on:
  pull_request:
permissions:
  contents: read
  pull-requests: write
  checks: write
jobs:
  review:
    uses: <org>/<workflow-repo>/.github/workflows/review-intelligencex.yml@<pinned-sha>
    with:
      review_config_path: .intelligencex/reviewer.json
```

If a job does not need write scopes, keep it read-only.

## 2. Pin Reusable Workflows and Actions

Pin third-party actions and reusable workflows by full commit SHA.
This makes upgrades intentional and auditable.

```yaml
uses: <org>/<workflow-repo>/.github/workflows/review-intelligencex.yml@<40-char-sha>
```

For internal repositories, decide your agility policy up front:

- strict mode: pin everything, including internal actions
- balanced mode: pin third-party actions, allow branch refs for internal actions

## 3. Keep Secrets Scoped and Explicit

Prefer explicit secret mapping over broad inheritance in public-facing repositories.

```yaml
secrets:
  INTELLIGENCEX_AUTH_B64: ${{ secrets.INTELLIGENCEX_AUTH_B64 }}
  INTELLIGENCEX_GITHUB_APP_ID: ${{ secrets.INTELLIGENCEX_GITHUB_APP_ID }}
  INTELLIGENCEX_GITHUB_APP_PRIVATE_KEY: ${{ secrets.INTELLIGENCEX_GITHUB_APP_PRIVATE_KEY }}
```

Checklist:

1. remove unused secrets
2. separate production and testing credentials
3. rotate credentials on a fixed schedule

## 4. Define Branch Protection Behavior Clearly

Pick one merge contract and document it in `README.md` or maintainer docs.

Option A: bot-first automation

- required status check: `review / review`
- required approving reviews: `0`
- code owner reviews: optional

Option B: human-gated merges

- required status check: `review / review`
- required approving reviews: `1+`
- code owner reviews: enabled for sensitive paths

Switching between modes ad hoc usually causes confusion and merge delays.

## 5. Treat Runner Choice as a Security Control

Runner strategy affects data exposure and cost.

- GitHub-hosted runners: simpler isolation model, low maintenance
- self-hosted runners: more control, more operational responsibility

If you use self-hosted runners:

1. isolate runner groups per trust boundary
2. block persistent credentials on the runner host
3. patch and rotate runner images regularly

## 6. Add an Incident Drill Before Go-Live

Practice one dry run before opening the repo:

1. revoke one credential
2. rotate and reconfigure secrets
3. rerun required checks
4. confirm merge policy still works

If this takes too long, your runbook needs simplification.

## Common Mistakes

- using broad `secrets: inherit` without reviewing exposure
- using mutable `@main` references for third-party actions
- enabling strict policy before compatibility checks
- documenting process in private notes only, not in repo-visible docs

## Final Take

Go-live security is mostly operational clarity.
If permissions, pins, secrets, and branch policy are explicit, you can move fast in public with fewer surprises.
