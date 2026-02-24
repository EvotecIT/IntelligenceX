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

### Scope-Drift Guardrails (All Phases)

- no autonomous merge execution in any mode,
- no autonomous mutation of repository policy or governance files (for example branch protection, required checks, or `AGENTS.md` policy semantics),
- no autonomous mutation of workflow permissions/secrets policy,
- no destructive git operations (force-push, history rewrite, or hard reset) as part of babysitter flows.

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

Normative requirements:
- The action planner MUST be idempotent for the same `(pr, headSha, stateVersion)` input.
- The action planner MUST emit a stable dedupe key for mutating actions (`retry_failed_checks`, future `repair` actions) so concurrent event-driven + scheduled runs do not duplicate execution.
- If an action with the same dedupe key was already executed successfully, repeated planners SHOULD emit `idle_wait` (or equivalent no-op) unless state materially changed.
- Review-source precedence MUST be deterministic:
  - trusted human maintainer feedback overrides approved bot feedback when they conflict,
  - approved bot feedback overrides untrusted/unknown noisy sources by default.

### 3) Durable State

Persist per-PR watcher state under `artifacts/pr-watch/`:
- seen review item ids,
- retry counts per SHA,
- last change key for backoff/heartbeat behavior.

### 3.1) Retry Budget and Cooldown Semantics

- Flaky retry budget MUST be enforced per head SHA.
- Default budget SHOULD be `max 3` rerun cycles per SHA.
- Babysitter MUST NOT rerun failed checks while non-terminal checks are still pending for the same SHA.
- Babysitter SHOULD enforce a cooldown between reruns (recommended: 10-15 minutes) to avoid rerun storms.
- On new SHA, retry budget resets unless maintainers explicitly lock retries for that PR.
- Exhausted retry budget MUST transition to `stop_user_help_required` with explicit `retry_budget_exhausted` reason.

### 3.2) Minimal Audit Log Contract

Each watcher cycle MUST emit or persist an audit record with:
- `timestampUtc`
- `prNumber`
- `repo`
- `headSha`
- `phase` (`observe`, `assist`, `repair`)
- `action` (planned/executed)
- `dedupeKey` (for mutating actions)
- `source` (scheduler, event, manual dispatch)
- `reason` (human-readable deterministic reason)
- `result` (`success`, `no-op`, `failed`, `skipped`)
- `runLink` (workflow/job URL when available)

### 4) Stop Conditions (Strict)

Stop only when one is true:
- PR is closed or merged,
- PR is fully merge-ready:
  - all required checks terminal and passing,
  - no unresolved trusted review blockers surfaced,
  - review approval gate not blocking,
  - mergeability not conflict/blocked,
- user-help-required blocker (infra, permissions, ambiguous product decision, retry budget exhausted).

Terminal-state examples:
- `ready_to_merge`: checks passing + mergeable + no blocking review decision + no unresolved trusted blockers.
- `blocked_and_escalated/infra`: required checks unavailable due to runner/billing/provider outage.
- `blocked_and_escalated/policy`: conflicting trusted maintainer requests requiring human product/priority decision.
- `blocked_and_escalated/retry_budget`: rerun budget exhausted for current SHA with persistent failures.

## Review Comments From "Other" Sources

Babysitter should handle review feedback from multiple sources, not only IntelligenceX reviewer output.

### Source Categories

- Maintainer/human trusted authors (OWNER, MEMBER, COLLABORATOR, configured operator accounts): blocking-capable and actionable.
- Approved automation bots (for example IntelligenceX reviewer, Codex reviewer, configured allow-list bots): actionable when items are concrete and reproducible.
- Untrusted/unknown external authors or noisy bots: informational by default, non-blocking unless maintainers explicitly escalate.

### Conflict Precedence (Source of Truth)

When review guidance conflicts, babysitter resolves by this precedence order:
1. trusted human maintainers,
2. explicit maintainer override comments/labels,
3. approved bot checklist/critical findings,
4. untrusted/unknown/noisy automated feedback.

If two trusted humans conflict and no explicit override exists, babysitter MUST stop with `needs-human-review` and avoid unilateral mutation.

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

## Failure and Rollback Protocol (Assist/Repair)

When running outside pure `observe` mode, babysitter must degrade safely.

### Immediate rollback triggers

- repeated CI mutation failures with no state improvement across 2 babysitter cycles,
- flaky rerun budget exhausted for the same SHA,
- detection of infra-blocked state for required checks,
- permissions/auth failures for required actions,
- conflicting or ambiguous review instructions that cannot be resolved safely.

### Rollback behavior

1. Switch affected PR back to `observe` behavior (no further mutations).
2. Emit a status summary with:
- what was attempted,
- what failed,
- the exact rollback trigger.
3. Record a tracking item:
- preferred: sync into `TODO.md` backlog entry with run/check links,
- fallback: create/attach a GitHub issue.
4. Require explicit maintainer acknowledgment before re-entering `assist` or `repair`.

### Exit criteria to re-enable mutating mode

- infra blocker resolved and verified,
- permissions/auth path verified,
- maintainer confirms re-enable decision in PR/issue thread,
- retry counters reset on new SHA or explicit maintainer override.

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

- median time-to-unblock (`first failing required check` -> `all required checks passing`) reduced by at least 20% from baseline,
- count of stale open PRs in blocked state (`>= 7 days`) reduced by at least 25%,
- percentage of babysitter stop events with explicit classified reason (`ready`, `closed`, `infra-blocked`, `user-help-required`) at 100%,
- reduction in repeat bot-churn loops (same underlying blocker resurfacing) by at least 30%,
- no increase in safety regressions:
  - zero autonomous merge executions,
  - zero policy/workflow-permission mutations by babysitter runtime.

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

| Decision | Owner | Target Decision Date |
| --- | --- | --- |
| Default schedule: every 30 minutes vs hourly | IX Reviewer Maintainers | March 6, 2026 |
| Enable flaky-rerun automation by default vs opt-in | IX Reviewer Maintainers + Repo Maintainers | March 6, 2026 |
| Snapshot/action schema name and versioning strategy | IntelligenceX.Cli Maintainers | March 6, 2026 |
| Watcher-state persistence scope (artifacts only vs artifacts + project fields) | IntelligenceX.Cli Maintainers + Project Ops Maintainers | March 6, 2026 |
