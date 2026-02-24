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
- Schedule: hourly observe-mode sweep
- Manual targeted run: `workflow_dispatch` with optional `pr` and policy inputs (`max_prs`, `max_flaky_retries`, `include_drafts`, `approved_bots`)
- Outputs: per-PR snapshots + rollup summary + audit log (`ix-pr-watch-audit.jsonl`) in `artifacts/pr-watch/`

Guarded retry assist automation:

- Workflow: `.github/workflows/ix-pr-babysit-assist-retry.yml`
- Trigger: manual `workflow_dispatch` only (single PR target)
- Safety: requires explicit confirmation token `RETRY_CHECKS`
- Scope: retries failed checks only when `pr-watch` plans an eligible `retry_failed_checks` action (dedupe + cooldown aware)
- Audit: emits execution outcomes (`success`/`skipped`/`failed`) into `artifacts/pr-watch/ix-pr-watch-audit.jsonl`

Nightly consolidation automation:

- Workflow: `.github/workflows/ix-pr-babysit-nightly-consolidation.yml`
- Schedule: daily consolidation sweep (plus `workflow_dispatch`)
- Inputs: `max_prs`, `stale_days`, `include_drafts`, `approved_bots`
- Outputs:
  - rollup JSON: `artifacts/pr-watch/ix-pr-watch-nightly-rollup.json`
  - markdown summary: `artifacts/pr-watch/ix-pr-watch-nightly-summary.md`
- Consolidation buckets include:
  - stale infra-like blockers,
  - review-required/stuck PRs,
  - retry-budget-exhausted PRs,
  - no-progress PRs grouped by age/churn class.

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
