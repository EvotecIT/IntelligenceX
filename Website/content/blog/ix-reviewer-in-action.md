---
title: IX Reviewer in Action
description: From quick thumbs-up feedback to evidence-driven PR review with blockers, optional issues, inline follow-up, and thread auto-resolve.
slug: ix-reviewer-in-action
date: 2026-02-12
categories: ["Walkthrough"]
tags: ["reviewer", "pull-requests", "automation"]
image: /assets/screenshots/ix-reviewer-in-action/ix-reviewer-01-pr-overview-and-baseline.jpg
collection: blog
layout: page
---

I posted this on X first, but it is worth expanding here with real screenshots and the full workflow.

Short version: yes, a quick thumbs-up review can feel good, but in real pull requests you usually need something more useful than "looks fine".
IX Reviewer is built for that real path.

## The "Looks Good" Baseline

This screenshot shows the type of quick baseline output many teams start with.
It is not wrong, and for tiny content changes it can be enough, but it is often too basic for production PR work.

![GitHub pull request view showing a simple summary comment and a thumbs-up reaction on a content-only blog PR](/assets/screenshots/ix-reviewer-in-action/ix-reviewer-01-pr-overview-and-baseline.jpg)

## Where IX Reviewer Starts Adding Value

On a similarly simple PR, IX Reviewer still keeps structure and keeps the result readable: summary, clear blocker sections, and explicit test/coverage notes.
Even when nothing blocks merge, the feedback is still practical.

![IntelligenceX Review comment on a content-only PR with structured sections for summary todo critical issues other issues and tests](/assets/screenshots/ix-reviewer-in-action/ix-reviewer-02-structured-summary-no-blockers.jpg)

## On Real PRs, It Gets Much More Useful

On runtime-impacting changes, the reviewer moves from generic comments to actionable triage.
You get explicit blockers and why they matter, not only a list of lint-style nits.

![IntelligenceX Review on a runtime PR showing merge-blocking todo and critical issues tied to behavior regression risk](/assets/screenshots/ix-reviewer-in-action/ix-reviewer-03-real-pr-blockers-overview.jpg)

It also separates what is required from what is optional:

- `Todo List ✅` and `Critical Issues ⚠️` are merge blockers
- `Other Issues 🧯` are suggestions unless maintainers escalate them

That separation helps teams avoid endless churn and focus on what actually decides merge readiness.

## It Tracks Context, Not Just One Diff Snippet

Another important part is transparency.
The reviewer output can include the assessed commit, diff range, model details, and usage summary so maintainers know what context was used.

![Review detail block showing diff range thread triage needs-attention list and model usage metadata including gpt-5.3-codex](/assets/screenshots/ix-reviewer-in-action/ix-reviewer-05-model-usage-and-thread-triage.jpg)

This is also why the workflow is stronger than "changed lines only" review habits.
You can tune diff strategy (`current`, `pr-base`, `first-review`) and thread triage settings to verify whether issues were actually addressed end to end.

## Inline Follow-Up and Fix Verification

IX Reviewer does not stop at one top-level comment.
It can continue through inline threads, re-check with each commit, and auto-resolve bot threads when evidence shows the issue is truly fixed.

![Inline review thread showing suggested code change and follow-up triage comment that checks whether the required implementation evidence is present](/assets/screenshots/ix-reviewer-in-action/ix-reviewer-06-inline-thread-followup.jpg)

## How It Works Behind the Scenes

At a high level, the reviewer pipeline is:

- collect PR context (files, patch chunks, review threads, diff range)
- apply filtering and safety checks
- call the configured provider/model
- format structured summary + inline output
- run thread triage and optional auto-resolve with evidence checks

If you want setup details, see:

- [Reviewer Overview](/docs/reviewer/overview/)
- [Reviewer Configuration](/docs/reviewer/configuration/)
- [Review Output Structure](/docs/reviewer/review-output/)

## Your Auth, Your App, Your Policy

The trust model is intentionally BYO:

- authentication uses your own `intelligencex auth login` flow
- usage runs on your own account/subscription limits
- bot identity can use your own GitHub App installation
- provider/model is configurable (for example `gpt-5.3-codex`)

## Options You Can Tune

Some high-impact knobs teams usually tune first:

- `mode`: `inline`, `summary`, or `hybrid`
- `model` and `reasoningEffort`: from fast/cheap to deeper reasoning passes
- `reviewDiffRange`: `current`, `pr-base`, `first-review`
- `strictness` and `focus`: bias toward correctness/security/reliability as needed
- `reviewThreadsAutoResolve*`: evidence-gated thread cleanup and summary behavior
- `triageOnly`: skip full review and only triage existing threads

## Exact YAML and JSON Pattern (Copy/Paste Starter)

If you want output shape similar to this walkthrough, start with this split.

### Workflow YAML

```yaml
jobs:
  review:
    uses: <org>/<workflow-repo>/.github/workflows/review-intelligencex.yml@<pinned-sha>
    with:
      reviewer_source: source
      provider: openai
      model: <model-id>
      mode: hybrid
      length: medium
      review_config_path: .intelligencex/reviewer.json
      include_issue_comments: true
      include_review_comments: true
      include_related_prs: true
      progress_updates: true
    secrets:
      INTELLIGENCEX_AUTH_B64: ${{ secrets.INTELLIGENCEX_AUTH_B64 }}
      INTELLIGENCEX_GITHUB_APP_ID: ${{ secrets.INTELLIGENCEX_GITHUB_APP_ID }}
      INTELLIGENCEX_GITHUB_APP_PRIVATE_KEY: ${{ secrets.INTELLIGENCEX_GITHUB_APP_PRIVATE_KEY }}
```

Replace `<org>`, `<workflow-repo>`, `<pinned-sha>`, and `<model-id>` with your values.
For latest examples, see [/docs/examples/](/docs/examples/).

### Reviewer JSON

```json
{
  "review": {
    "mode": "hybrid",
    "length": "medium",
    "style": "direct",
    "reviewDiffRange": "pr-base",
    "reviewThreadsNeedsAttentionSummary": true,
    "reviewThreadsAutoResolveOnEvidence": true,
    "reviewThreadsAutoResolveComment": true
  }
}
```

This keeps CI wiring in YAML and policy in JSON, which makes behavior easier to debug when teams iterate quickly.

## Reproduce the Review Loop Locally

For a quick local verification before pushing workflow/config changes:

```bash
export INPUT_REPO=<owner>/<repo>
export INPUT_PR_NUMBER=<pr-number>
intelligencex reviewer run
```

Then after fixes are pushed:

```bash
intelligencex reviewer resolve-threads --repo <owner>/<repo> --pr <pr-number>
```

Replace `<owner>`, `<repo>`, and `<pr-number>` with your real values.

That mirrors the practical PR dance: run review, fix blockers, re-run, then resolve stale bot threads when evidence is present.

## Why Teams Keep It

The value is not "more comments". The value is better merge decisions:

- clearer separation between blockers and optional improvements
- stronger signal on correctness/security/reliability risks
- better continuity across commits and review-thread lifecycle
- less reviewer fatigue from style-only noise

A practical follow-up to this post can walk through one PR from first failing review to final auto-resolved thread set, including the exact config used.
