---
title: Reviewer Config Playbook (YAML vs JSON)
description: A practical guide to configuring IntelligenceX Reviewer with workflow YAML, reviewer.json, and onboarding flows.
slug: reviewer-yaml-vs-json-playbook
collection: blog
layout: page
---

If your team has ever said, "nothing changed, but reviewer output looks different," this post is for you.

That sentence almost always means one thing: the setting changed source, not just value.
IntelligenceX Reviewer can read config from both workflow YAML and `.intelligencex/reviewer.json`.
Once you understand who owns what, reviewer behavior becomes boring and predictable again.

## The Mental Model

There are two surfaces:

- Workflow YAML controls pipeline wiring and per-run overrides.
- `reviewer.json` controls team review policy.

And there is one precedence rule:

`defaults -> reviewer.json -> environment/workflow inputs`

In Actions, `with:` inputs become `INPUT_*` variables, so they can override JSON.

## Real Failure Pattern

The most common drift bug looks like this:

1. Team sets `mode: hybrid` in `.intelligencex/reviewer.json`.
2. Later, a workflow update passes a different `mode` (or omits expected fields and defaults kick in).
3. Reviewer starts rendering differently.
4. Everyone blames the model.

The model is usually fine. Config precedence is what changed.

## What Goes Where (Practical, Not Theoretical)

Use this split to avoid 90% of configuration churn:

- Keep in YAML:
  - `runs_on`
  - `reviewer_source`
  - `provider`, `model`, `openai_transport`
  - secret wiring (`secrets: inherit` or explicit)
  - temporary `workflow_dispatch` overrides
- Keep in JSON:
  - `mode`, `length`, `profile`, `style`
  - `reviewDiffRange`, include/exclude/skip paths
  - thread triage and auto-resolve behavior
  - analysis policy under `analysis.*`

This gives CI engineers control of infrastructure and reviewers/maintainers control of policy.

## Build Impact: YAML vs JSON

Treat these differently in review:

- YAML change:
  - can alter CI execution path
  - can affect whether review runs at all
  - can impact auth/transport/runtime behavior
- JSON change:
  - usually leaves pipeline wiring intact
  - changes review behavior, strictness, coverage, and output shape

Same repository, same reviewer, very different risk profile.

## Onboarding: CLI and Web Are the Same Contract

Use whichever UX fits your team:

- CLI: `intelligencex setup wizard`
- Web: `intelligencex setup web`

Both follow the same path model:

- `new-setup`
- `refresh-auth`
- `cleanup`
- `maintenance`

So this is not two systems. It is one setup contract with two entry points.

## A Simple Team Playbook

When changing reviewer behavior:

1. Change one source at a time.
2. Open a small PR.
3. Confirm `review / review` ran.
4. Check footer metadata for expected `Mode` and `Length`.
5. If analysis is on, confirm policy/findings sections match expectations.

When changing workflow wiring:

1. Treat it as CI/infrastructure change.
2. Validate on a low-risk PR.
3. Document why YAML owns that specific knob.

## Final Take

You do not need to pick YAML or JSON forever.
You need explicit ownership.

Once ownership is clear, reviewer output stops feeling random, onboarding is easier, and upgrades become routine instead of stressful.

For the full reference:

- [Workflow vs JSON](/docs/reviewer/workflow-vs-json/)
- [Reviewer Configuration](/docs/reviewer/configuration/)
- [Onboarding Wizard](/docs/reviewer/onboarding-wizard/)
- [Web Setup UI](/docs/reviewer/setup-web/)
