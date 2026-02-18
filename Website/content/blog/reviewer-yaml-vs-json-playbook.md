---
title: Reviewer Config Playbook (YAML vs JSON)
description: A practical guide to configuring IntelligenceX Reviewer with workflow YAML, reviewer.json, and onboarding flows.
slug: reviewer-yaml-vs-json-playbook
collection: blog
layout: page
---

When teams say "the reviewer changed behavior," the root cause is usually simple:
settings were applied from a different source than expected.

IntelligenceX Reviewer can read configuration from both workflow YAML and `.intelligencex/reviewer.json`.
That is powerful, but only if everyone knows which file owns which decision.

This post is the quick playbook.

## One Minute Version

- Use workflow YAML for CI plumbing (runner, source, transport, secrets mode, temporary overrides).
- Use `.intelligencex/reviewer.json` for review policy (mode, length, strictness, diff strategy, filters, triage).
- Remember precedence: defaults -> JSON -> workflow/env inputs.

If you want the full contract details, read:
- [Workflow vs JSON](/docs/reviewer/workflow-vs-json/)
- [Reviewer Configuration](/docs/reviewer/configuration/)

## Why This Matters

YAML and JSON changes have different blast radius:

- YAML changes can alter CI behavior and check execution.
- JSON changes usually keep CI wiring stable but change review output and triage behavior.

That is why two PRs can both "touch reviewer config" but have very different operational risk.

## Recommended Team Pattern

Use a hybrid split with explicit boundaries:

- YAML owns:
  - `runs_on`
  - `reviewer_source`
  - `provider`, `model`, `openai_transport`
  - secrets wiring
- JSON owns:
  - `mode`, `length`, `profile`, `style`
  - `reviewDiffRange`, include/exclude/skip paths
  - thread triage/auto-resolve options
  - analysis policy under `analysis.*`

This keeps behavior predictable and lowers config drift.

## CLI Wizard vs Web Setup

Both paths use the same onboarding contract and path IDs:

- `new-setup`
- `refresh-auth`
- `cleanup`
- `maintenance`

Use whichever UX you prefer:

- CLI: `intelligencex setup wizard`
- Web: `intelligencex setup web`

Docs:
- [Onboarding Wizard](/docs/reviewer/onboarding-wizard/)
- [Web Setup UI](/docs/reviewer/setup-web/)
- [Web Onboarding Flow](/docs/reviewer/web-onboarding/)

## What Happens After You Change Config

### If you edit workflow YAML

- You are editing CI behavior directly.
- Workflow-only PRs may be intentionally review-skipped depending on guard settings.
- Validate checks on a small PR before rolling out broadly.

### If you edit reviewer.json

- Reviewer check wiring typically stays the same.
- Comment format, strictness, and findings can change immediately.
- Validate on one representative PR and compare summary structure + triage decisions.

## Practical Validation Loop

1. Make one focused config change.
2. Open a small PR.
3. Confirm `review / review` ran.
4. Check reviewer footer for expected `Mode` and `Length`.
5. If analysis is enabled, confirm policy/findings sections match intent.

## Final Take

You do not need to choose YAML or JSON exclusively.
You need a clear ownership model.

Once your team documents that split, reviewer behavior becomes predictable, upgrades are safer, and onboarding gets much easier.
