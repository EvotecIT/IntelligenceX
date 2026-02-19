---
title: Setup Best Practices for Teams
description: A practical runbook for onboarding IntelligenceX reviewer across repositories without configuration drift.
slug: setup-best-practices-for-teams
date: 2026-02-18
categories: ["Guides"]
tags: ["setup", "teams", "onboarding"]
image: /assets/screenshots/product-reviewer.svg
collection: blog
layout: page
---

Teams usually do not struggle with the first setup. They struggle with setup number 5, when people, repos, and ownership boundaries start to drift.

This runbook keeps setup predictable.

## 1. Use One Setup Path Per Intent

IntelligenceX setup supports four distinct intents:

- `new-setup`: create/update workflow + reviewer config
- `refresh-auth`: rotate/update auth secret only
- `cleanup`: remove managed reviewer assets
- `maintenance`: inspect and choose action

Pick the intent first, then run setup. Do not mix intent mid-run.

## 2. Keep Workflow and Policy Ownership Split

- Workflow YAML owns CI wiring and runtime inputs.
- `.intelligencex/reviewer.json` owns review policy defaults.

When behavior changes unexpectedly, check source-of-truth drift before changing model/provider.

## 3. Prefer Small PRs for Setup Changes

Change one category per PR:

1. auth refresh only
2. workflow wiring only
3. policy tuning only

Small setup PRs make reviewer regressions obvious and easy to roll back.

## 4. Use This Local Validation Loop

Before pushing setup changes:

```bash
dotnet build IntelligenceX.sln -c Release
dotnet test IntelligenceX.sln -c Release
```

If your repository also uses framework-specific harness binaries, run those commands from your repo's CI or maintainer docs instead of hardcoding framework paths.

Then validate reviewer behavior against a known PR:

```bash
export INPUT_REPO=<owner>/<repo>
export INPUT_PR_NUMBER=<pr-number>
intelligencex reviewer run
```

Replace `<owner>`, `<repo>`, and `<pr-number>` with your real values.

## 5. Treat Overrides as Temporary

Workflow `with:` overrides are useful for experiments, but they can hide JSON defaults and surprise teams later.

When a test succeeds:

1. move stable policy keys into `reviewer.json`
2. remove temporary workflow overrides
3. document the ownership decision in PR description

## 6. Standardize Post-Apply Verification

After merge, check three things:

1. `review / review` ran
2. output metadata shows expected mode/length
3. thread triage behavior matches expected policy

If any one fails, revert fast and iterate in a follow-up PR.

## 7. Keep an Upgrade Cadence

For reusable workflow ref updates:

1. update pinned SHA in one PR
2. validate check startup + full review run
3. update docs/examples in the same PR

Avoid silent drift where docs still show old workflow refs.

## Final Take

Good setup is operational discipline, not one-time wizard usage.
If intent, ownership, and validation are consistent, reviewer rollout scales cleanly across repos and teams.
