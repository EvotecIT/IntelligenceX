# RFC: IntelligenceX PR Babysitter (End-to-End Review Loop)

## Status

Draft (planning PR only, no runtime changes in this RFC).

## Problem

IntelligenceX already has strong PR analysis and backlog tooling, but the day-to-day "babysit this PR until it is merge-ready" loop is still mostly manual and snapshot-based:
- operators run `gh pr checks` and comment scans repeatedly,
- retries and review follow-ups are policy-driven but not consistently stateful,
- the same PR can churn between pending/failing/review-blocked states without a unified, durable watcher state.

## Goal

Define an end-to-end PR babysitting design that:
- continuously monitors CI, reviews, and mergeability,
- recommends deterministic next actions in priority order,
- distinguishes actionable vs infra-blocked vs churn conditions,
- integrates with existing IntelligenceX reviewer/todo/project flows,
- supports both human-driven and scheduled automation modes.

## Non-Goals (Phase 1)

- auto-merge without explicit maintainer approval,
- autonomous wide-scope refactors to satisfy vague review comments,
- replacing existing IntelligenceX reviewer output formats.

## Existing IX Strengths To Reuse

IntelligenceX already provides the foundations needed for this:
- PR handling governance and blocker taxonomy in `AGENTS.md`.
- Bot checklist synchronization into `TODO.md` via `todo sync-bot-feedback`.
- PR operational signals (`mergeable`, `reviewDecision`, status-check health) in triage/project tooling.
- Language-neutral, structured watchdog/recovery patterns in chat execution logic.

This RFC proposes connecting those parts into one explicit PR babysitter contract.

## Reviewer vs Babysitter (Clarification)

### Current IX Reviewer (today)

- Runs per PR/review invocation and produces structured findings.
- Focuses on code/diff correctness and merge blockers.
- Provides high-quality analysis output, but is not itself a persistent PR-state orchestrator.

### Proposed IX Babysitter (new)

- Runs as a continuous stateful loop around PR lifecycle events.
- Watches CI/review/mergeability drift and recommends deterministic next actions.
- Keeps working until strict terminal conditions are reached (ready/closed/user-help-required).

### Relationship

- Reviewer is the analysis engine.
- Babysitter is the operational control loop.
- Babysitter consumes reviewer outputs and keeps the PR moving end-to-end.

## External Patterns Worth Adopting

From OpenAI Codex commit `7e569f11625605f501675e455cfc5e0d642503f2` (Feb 22, 2026):
- stateful watcher with explicit action recommendations,
- per-SHA flaky retry budget,
- review-feedback-first ordering before flaky reruns,
- strict terminal stop conditions,
- adaptive backoff after CI turns green,
- trusted review-author filtering to reduce noise.

These patterns map cleanly to IntelligenceX policy and should be adopted with IX-specific governance language.

## Proposed Architecture

### 1) PR Watch Snapshot Contract

Introduce a normalized snapshot payload for a single PR, for example:
- `pr`: number, url, headSha, branch, state, mergeable, mergeStateStatus, reviewDecision
- `checks`: passed/failed/pending counts, terminal flag
- `failedRuns`: rerunnable failed workflow runs for current SHA
- `newReviewItems`: trusted, unresolved, newly surfaced items
- `retryState`: retries used for current SHA, max retries
- `actions`: recommended next actions
- `stopReason`: null or terminal reason

### 2) Deterministic Action Engine

Action priority (highest first):
1. `stop_pr_closed` / `stop_ready_to_merge` / `stop_user_help_required`
2. `process_review_comment` (when actionable)
3. `diagnose_ci_failure`
4. `retry_failed_checks` (only when terminal failures and retry budget allows)
5. `idle_wait`

Key rule: if actionable review changes and flaky retry are both possible, process review feedback first to avoid rerunning old SHA checks.

### 3) Durable State

Persist per-PR watcher state under `artifacts/pr-watch/`:
- seen review item ids,
- retry counts per SHA,
- last change key for backoff/heartbeat behavior.

### 4) Stop Conditions (Strict)

Stop only when one is true:
- PR is closed or merged,
- PR is fully merge-ready:
  - all required checks terminal and passing,
  - no unresolved trusted review blockers surfaced,
  - review approval gate not blocking,
  - mergeability not conflict/blocked,
- user-help-required blocker (infra, permissions, ambiguous product decision, retry budget exhausted).

## Review Comments From "Other" Sources

Babysitter should handle review feedback from multiple sources, not only IntelligenceX reviewer output.

### Source Categories

- Maintainer/human trusted authors (OWNER, MEMBER, COLLABORATOR, configured operator accounts): blocking-capable and actionable.
- Approved automation bots (for example IntelligenceX reviewer, Codex reviewer, configured allow-list bots): actionable when items are concrete and reproducible.
- Untrusted/unknown external authors or noisy bots: informational by default, non-blocking unless maintainers explicitly escalate.

### Handling Policy

1. Normalize all incoming feedback into a single review-item queue with source metadata.
2. De-duplicate by stable keys (comment/review id + thread context).
3. Prioritize actionable trusted human items first.
4. Process approved bot checklist/critical items next.
5. Ignore style-only/noise items from non-trusted sources unless escalated by maintainers.
6. Mark ambiguous or non-reproducible items as `needs-human-review` and avoid churn loops.

### Governance Extension

- Add a repository-level allow-list for approved review bots.
- Keep explicit mapping from source type to default severity (`blocking`, `advisory`, `ignore-until-escalated`).
- Persist source attribution in watcher state and summary artifacts for auditability.

## Operating Model (How It Should Run)

### Primary Recommendation: Hybrid Model

Use both event-driven and scheduled monitoring. Do not rely on weekly or daily only.

1. Event-driven triggers (fast reaction):
- `pull_request` (opened, synchronize, reopened, ready_for_review)
- `pull_request_review`
- `pull_request_review_comment`
- optional `issue_comment` command trigger for explicit "babysit now"

2. Scheduled sweeps (safety net + drift control):
- every 30-60 minutes for open PR monitoring,
- nightly summary sweep for backlog/reporting consistency.

3. Manual on-demand mode:
- maintainer or agent runs babysitter for a specific PR when active remediation is needed.

### Why Not Only Daily/Weekly?

PR state changes (new commits, CI transitions, review requests) happen on the order of minutes/hours, not days. Daily/weekly cadence is too slow for unblock loops and increases merge latency/churn.

### Why Not One Infinite GitHub Action Watcher?

Long-running watchers in Actions are fragile (timeouts, cost, runner availability). Prefer short scheduled runs plus state persistence and event-driven re-entry.

## Workflow Cadence Recommendation

### Workflow A: `ix-pr-babysit-monitor.yml` (scheduled + dispatch)

Purpose: monitor and classify open PRs, emit action suggestions, optionally execute safe retries.

Suggested cadence:
- `schedule`: every 30 minutes (or hourly as lower-cost default),
- `workflow_dispatch`: manual run for targeted remediation.

Default mode:
- observe/classify/report only.

Optional mutating mode (explicit opt-in):
- rerun failed checks for likely flaky failures within retry budget.

### Workflow B: existing reviewer workflows (event-driven per PR update)

Purpose: deep content review and blocking findings.

Role in E2E flow:
- babysitter consumes reviewer results/signals,
- syncs explicit checklist items into `TODO.md` or project fields,
- keeps loop moving until terminal state.

### Workflow C: nightly consolidation

Purpose: produce control-plane summaries:
- stale infra blockers,
- PRs stuck at review-required,
- retry-budget-exhausted PRs,
- no-progress PRs by age/churn class.

## Governance Modes

Roll out in three modes:

1. `observe` (default first)
- no mutations,
- snapshots, recommendations, dashboards/comments only.

2. `assist`
- allow flaky reruns and TODO/project sync updates,
- still no autonomous code commits.

3. `repair` (future, guarded)
- agent may patch+push narrow, verifiable fixes under strict policy gates.

## Integration Plan With Existing IX Components

1. `IntelligenceX.Cli/Todo`
- add a watcher command (`todo pr-watch` or equivalent) using existing `GhCli` patterns.

2. `sync-bot-feedback`
- continue as canonical explicit checklist sync.
- optionally consume babysitter surfaced blockers.

3. `project-sync`
- ingest babysitter operational signals and stop reasons as additional project metadata.

4. Agent skill
- evolve `intelligencex-pr-unblock-loop` from snapshot helpers to watcher-orchestrated loop semantics.

## Rollout Phases

### Phase 0 (this RFC PR)
- align on contract, cadence, and governance.

### Phase 1
- implement non-mutating watcher command + JSON snapshots + tests.
- add scheduled workflow in observe mode.

### Phase 2
- enable guarded flaky rerun automation with per-SHA budgets.
- integrate stop reasons into project/TODO reporting.

### Phase 3
- consider guarded repair mode for narrow deterministic fixes.

## Success Metrics

- time-to-green reduced for active PRs,
- reduced time in "blocked but unattended" state,
- lower repeat-churn from reworded/non-actionable bot feedback,
- higher merge-throughput of mergeable PRs without safety regressions.

## Risks

- over-automation on ambiguous reviewer feedback,
- noisy retries on non-flaky failures,
- permission and fork-safety constraints in mutating contexts.

Mitigations:
- default observe mode,
- strict retry budgets and trusted-author filters,
- explicit infra-blocked classification and escalation path,
- no auto-merge.

## Open Decisions For Consolidation

1. Default schedule: every 30 minutes vs hourly.
2. Whether flaky rerun automation is enabled by default in repository workflows.
3. Exact schema name/version for snapshot/action payloads.
4. Whether to store watcher state only in artifacts or also emit project field snapshots.
