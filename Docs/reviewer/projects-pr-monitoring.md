---
title: Projects and PR Monitoring
description: Use IntelligenceX todo commands for GitHub-native project, pull request, and backlog monitoring with maintainer-controlled automation.
---

# Projects + PR Monitoring

IntelligenceX includes a GitHub-native maintainer control plane for PR and issue backlog monitoring using the `intelligencex todo ...` command family.

## Important disclaimer

These commands are assistive. They are not an autonomous production decision system.

- Treat `category`, `tags`, `IX Suggested Decision`, duplicate clusters, and scope classification as recommendations.
- Keep final decisions human-owned (`Maintainer Decision`, merge, close, defer).
- Do not wire this into unattended production mutation flows without explicit human review gates.

## What this gives you

- Bot-review checklist sync into `TODO.md` (`sync-bot-feedback`).
- Backlog indexing and duplicate clustering across open PRs/issues (`build-triage-index`).
- Scope alignment checks against `VISION.md` (`vision-check`).
- Issue applicability review for stale/no-longer-applicable infra blockers (`issue-review`).
- Observe-mode PR babysitter snapshots and action recommendations (`pr-watch`).
- Issue applicability proposed actions (`close`, `keep-open`, `needs-human-review`) with confidence scoring and safety signals.
- GitHub Project field sync for triage at scale (`project-init`, `project-sync`, `project-bootstrap`).
- Maintainer-assist view checklist and apply plan generation (`project-view-checklist`, `project-view-apply`).
- Signal quality grading (`high`/`medium`/`low`) to separate strong recommendations from weak-context items.
- Operational PR signals (`PR Size`, `PR Churn Risk`, `PR Merge Readiness`, `PR Freshness`, `PR Check Health`, `PR Review Latency`, `PR Merge Conflict Risk`) for faster triage.

## Recommended end-to-end flow

1. Pull explicit bot checklist items into `TODO.md`.
2. Build triage index artifacts.
3. Run issue applicability review.
4. Run vision alignment check.
5. Bootstrap or initialize GitHub Project.
6. Sync triage + issue-review + vision into project fields.
7. Generate project view checklist and apply plan for maintainers.

## End-to-end example

```bash
# 1) Sync explicit bot checklist items to TODO.md
intelligencex todo sync-bot-feedback --repo EvotecIT/IntelligenceX

# 2) Build triage index artifacts
intelligencex todo build-triage-index --repo EvotecIT/IntelligenceX

# 3) Review infra blocker issue applicability (dry-run, with streak state)
intelligencex todo issue-review --repo EvotecIT/IntelligenceX --proposal-only --min-consecutive-candidates 2 --min-auto-close-confidence 80 --state-path artifacts/triage/ix-issue-review-state.json

# 4) Check backlog against VISION.md
intelligencex todo vision-check --repo EvotecIT/IntelligenceX --vision VISION.md

# 5) Bootstrap project + workflow + vision scaffold
intelligencex todo project-bootstrap --repo EvotecIT/IntelligenceX --owner EvotecIT

# 6) Sync triage + issue-review + vision into project fields
intelligencex todo project-sync --config artifacts/triage/ix-project-config.json --issue-review artifacts/triage/ix-issue-review.json --max-items 500

# 7) Generate a maintainer view checklist
intelligencex todo project-view-checklist --config artifacts/triage/ix-project-config.json --create-issue
```

## Command quick map

| Command | Purpose | Typical output |
| --- | --- | --- |
| `sync-bot-feedback` | Extract explicit bot checklist tasks and keep them tracked in `TODO.md` | Updated `TODO.md` (optional GitHub issues) |
| `build-triage-index` | Build PR/issue inventory, duplicate clusters, and best PR ranking | `artifacts/triage/ix-triage-index.json`, `.md` |
| `issue-review` | Detect stale/no-longer-applicable infra blocker issues and optionally auto-close | `artifacts/triage/ix-issue-review.json`, `.md` |
| `pr-watch` | Observe PR CI/review/mergeability state and emit deterministic action recommendations | JSON snapshot + watcher state + audit JSONL in `artifacts/pr-watch/` |
| `vision-check` | Compare backlog against `VISION.md` scope | `artifacts/triage/ix-vision-check.json`, `.md` |
| `project-init` | Create/initialize GitHub Project fields + metadata | `artifacts/triage/ix-project-config.json` |
| `project-sync` | Push triage/issue-review/vision signals to project items and optional labels/comments | Project field updates, optional comment/label updates |
| `project-bootstrap` | First-run bootstrap for project + workflow + vision scaffold | Project config + workflow + `VISION.md` scaffold |
| `project-view-checklist` | Build checklist of recommended project views | `artifacts/triage/ix-project-view-checklist.md` |
| `project-view-apply` | Build deterministic plan to apply missing views | `artifacts/triage/ix-project-view-apply.md` |

## Automation workflow

- Workflow: `.github/workflows/issue-review.yml`
- Schedule: nightly dry-run (no auto-close by default)
- Manual auto-close: use `workflow_dispatch` with:
  - `apply_close=true`
  - `confirm_apply_close=CLOSE_ISSUES`
  - `min_auto_close_confidence` (default `80`)
  - optional label policy (`allow_labels`, `deny_labels`)

Observe-mode babysitter automation:

- Workflow: `.github/workflows/ix-pr-babysit-monitor.yml`
- Triggers:
  - event-driven on PR lifecycle and submitted/edited reviews (`pull_request`, `pull_request_review`)
  - hourly observe-mode safety-net sweep
- Engine command: `intelligencex todo pr-watch-monitor`
- Behavior:
  - event-driven runs target the triggering PR automatically,
  - PR-triggered monitor runs are forced onto GitHub-hosted runners (security hardening),
  - `pull_request_review_comment` is intentionally excluded to avoid duplicate paired monitor runs from the same review action,
- `workflow_dispatch` supports manual targeted runs with optional `pr` override and policy inputs (`max_prs`, `max_flaky_retries`, `include_drafts`, `approved_bots`)
- `retry_failure_policy` controls whether retry suggestions use legacy `any` mode or the opt-in smarter `non-actionable-only` mode
- Outputs: per-PR snapshots + rollup summary + audit log (`ix-pr-watch-audit.jsonl`) in `artifacts/pr-watch/`
- Monitor and nightly rollups now include failed workflow-run totals plus `actionable` / `operational` / `mixed` / `unknown` evidence counts

Guarded retry assist automation:

- Workflow: `.github/workflows/ix-pr-babysit-assist-retry.yml`
- Trigger: manual `workflow_dispatch` only (single PR target)
- Engine command: `intelligencex todo pr-watch-assist-retry`
- Safety: requires explicit confirmation token `RETRY_CHECKS`
- Scope: retries failed checks only when `pr-watch` plans an eligible `retry_failed_checks` action (dedupe + cooldown aware)
- `retry_failure_policy=non-actionable-only` makes retry planning suppress likely code/test breakages while still allowing operational or unknown failures
- Audit: emits execution outcomes (`success`/`skipped`/`failed`) into `artifacts/pr-watch/ix-pr-watch-audit.jsonl`

Nightly consolidation automation:

- Workflow: `.github/workflows/ix-pr-babysit-nightly-consolidation.yml`
- Schedule: daily consolidation sweep (plus `workflow_dispatch` and reusable `workflow_call`)
- Engine command: `intelligencex todo pr-watch-consolidate`
- Inputs: `max_prs`, `stale_days`, `include_drafts`, `approved_bots`, `source`
- Inputs also include optional `retry_failure_policy` (`any` by default, `non-actionable-only` opt-in)
- Optional tracker issue inputs: `publish_tracking_issue`, `tracker_issue_title`, `tracker_issue_labels`
- Optional governance labeling input: `apply_governance_signal_label` (default `false`)
- Outputs:
  - rollup JSON: `artifacts/pr-watch/ix-pr-watch-nightly-rollup.json`
  - markdown summary: `artifacts/pr-watch/ix-pr-watch-nightly-summary.md`
  - metrics JSON: `artifacts/pr-watch/ix-pr-watch-nightly-metrics.json`
  - metrics history JSON: `artifacts/pr-watch/ix-pr-watch-nightly-metrics-history.json`
  - tracker issue body markdown: `artifacts/pr-watch/ix-pr-watch-nightly-tracker.md`
- Nightly rollups and tracker summaries also surface failed workflow-run kind breakdowns so maintainers can judge whether failures look infra-like or code-like before adjusting retry policy
- Nightly metrics also emit conservative retry-policy guidance based on consecutive failure-profile trends, for example suggesting `non-actionable-only` only after repeated operational/unknown-dominant runs
- When that guidance becomes stable enough to recommend a change, weekly/nightly tracker output treats it as a governance signal, but it remains advisory only; workflow inputs are not changed automatically
- Nightly metrics now also expose that recommendation as a machine-readable `governanceSignals` block so downstream governance/reporting flows can sort on policy-review suggestions without scraping markdown
- Nightly and weekly markdown summaries now also include a compact `Governance:` line near the top so maintainers can spot active policy-review recommendations without opening tracker details
- If `apply_governance_signal_label=true`, the tracker issue will also manage the repo-facing label `ix/retry-policy-review-suggested` based on the current governance signal; this remains opt-in and is removed again when the signal clears
- `triage-project-sync.yml` can also opt into syncing that live governance signal onto PR project items via the managed label `ix/pr-watch:policy-review-suggested`, using `workflow_dispatch` input `apply_pr_watch_governance_labels` or repo variable `IX_TRIAGE_APPLY_PR_WATCH_GOVERNANCE_LABELS`; this path is also off by default
- `triage-project-sync.yml` can independently opt into syncing optional project fields `PR Governance Signal` and `PR Governance Summary` from the same live tracker state by using `workflow_dispatch` input `apply_pr_watch_governance_fields` or repo variable `IX_TRIAGE_APPLY_PR_WATCH_GOVERNANCE_FIELDS`; this path is also off by default
- `triage-project-sync.yml` can also independently opt into the optional `Governance Review` project view profile by using `workflow_dispatch` input `include_pr_watch_governance_views` or repo variable `IX_TRIAGE_INCLUDE_PR_WATCH_GOVERNANCE_VIEWS`; this keeps the default view set unchanged for teams that are not using the governance fields
- Generated project configs now persist that governance intent in `features.prWatchGovernance`; project-view checklist/apply uses it for optional governance view coverage, and `todo project-sync` uses it for governance label/field defaults when a config file is present
- `todo project-sync` also supports `--no-apply-pr-watch-governance-labels` and `--no-apply-pr-watch-governance-fields`, so config-backed defaults remain overridable per run
- Consolidation buckets include:
  - stale infra-like blockers,
  - review-required/stuck PRs,
  - retry-budget-exhausted PRs,
  - no-progress PRs grouped by age/churn class.
- Tracker issue behavior:
  - source-scoped upsert using marker `<!-- intelligencex:pr-watch-rollup-tracker:<source> -->`,
  - auto-enabled for scheduled/reusable runs, opt-in for manual dispatch.

Weekly governance automation:

- Workflow: `.github/workflows/ix-pr-babysit-weekly-governance.yml`
- Schedule: weekly consolidation sweep (Monday)
- Implementation: calls the nightly consolidation workflow via `workflow_call` with weekly profile defaults (`max_prs=300`, `stale_days=14`, `source=weekly-governance`)
- Manual `workflow_dispatch` also exposes optional overrides for `retry_failure_policy`, tracker publishing, governance-signal labeling, tracker title/labels, and the weekly scan window so teams can opt in deliberately without changing the scheduled defaults
- Outputs: same rollup/summary/metrics schema as nightly consolidation for comparable trend analysis
- Tracker issue: enabled by default (`publish_tracking_issue=true`) for weekly governance runs
- Stable retry-policy recommendations can keep the weekly governance tracker issue open even when blocker buckets are otherwise quiet, so maintainers can opt in deliberately

### Cadence matrix

| Cadence | Workflow | Phase | Purpose | Expected maintainer action |
| --- | --- | --- | --- | --- |
| Event-driven | `.github/workflows/ix-pr-babysit-monitor.yml` | `observe` | Immediate reaction to PR updates and submitted/edited reviews | Validate newest blocker classification quickly and decide whether targeted assist is needed |
| Hourly | `.github/workflows/ix-pr-babysit-monitor.yml` | `observe` | Fast detection of CI/review/mergeability drift on open PRs | Triage fresh blockers and decide whether to run assist retry on specific PRs |
| Daily | `.github/workflows/ix-pr-babysit-nightly-consolidation.yml` | `observe` | Portfolio-level no-progress and stale-blocker rollup | Prioritize next-day unblock queue, review metrics deltas, and maintain source tracker issue |
| Weekly | `.github/workflows/ix-pr-babysit-weekly-governance.yml` | `observe` | Wider-window governance snapshot (older stalls/churn classes) | Review systemic patterns, validate stale/no-progress trend direction, and adjust operational ownership |

This cadence keeps mutation guarded and targeted:

- observe runs are event-driven plus scheduled (hourly/daily/weekly),
- retry assist remains explicit/manual per PR (`ix-pr-babysit-assist-retry.yml`),
- no autonomous merge or policy mutation is introduced.

### Issue-review confidence signals

`issue-review` now emits a proposed action and confidence score for each issue:

- `proposedAction`: `close`, `keep-open`, `needs-human-review`, `ignore`
- `actionConfidence`: `0-100` with level hints (`high`/`medium`/`low`)
- `confidenceSignals`: explainable signal list (for example stale bucket, recent activity, linked PR age, reopened count)
- `project-sync` maps these into project fields: `Issue Review Action`, `Issue Review Action Confidence`

Safety behavior:

- Auto-close requires both policy eligibility and confidence threshold (`--min-auto-close-confidence`).
- Recently active issues and reopened issues are downgraded to `needs-human-review`.
- Use `--proposal-only` for calibration runs where any close operation must be blocked.

## Permissions and safety

- Project setup and sync requires GitHub `project` scope (`read:project` also required for sync reads).
- Issue-posting helpers require issue write permission.
- Use `--dry-run` on sync commands before enabling mutating operations.
- Treat low-signal items (`Signal Quality = low`, `ix/signal:low`) as context-gathering tasks, not decision-ready recommendations.

## Related docs

- [Project Bootstrap and Sync](/docs/reviewer/project-bootstrap-sync/)
- [Project Views and Operations](/docs/reviewer/project-views-and-ops/)
- [Reviewer Overview](/docs/reviewer/overview/)
