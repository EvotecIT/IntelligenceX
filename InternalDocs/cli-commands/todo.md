# TODO Backlog Sync (Bot Reviews)

This repo tracks only explicit checklist items from bot reviews/comments in `TODO.md` under:

`## Review Feedback Backlog (Bots)`

## Sync Command (Recommended)

If you have the global tool installed:

```bash
intelligencex todo sync-bot-feedback --repo EvotecIT/IntelligenceX
```

Otherwise (works from a repo checkout):

```bash
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -- todo sync-bot-feedback --repo EvotecIT/IntelligenceX
```

Notes:
- This reads open PR reviews and issue comments authored by the bot login(s) (default: `intelligencex-review`).
- It only imports explicit markdown task list items (`- [ ] ...`, `- [x] ...`).
- Each imported task includes a link back to the originating review/comment.

## GitHub Issues (Optional)

To create GitHub issues for unchecked items:

```bash
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -- todo sync-bot-feedback --repo EvotecIT/IntelligenceX --create-issues
```

This will add `ix-bot-feedback-id:<id>` markers to issue bodies to avoid duplicates on re-runs.

## Triage Index (PR + Issue de-dupe + ranking)

Build a repo triage snapshot with:
- Open PR and issue inventory
- Duplicate clusters (token similarity)
- Ranked "best PR" candidate list (assistive score)

```bash
intelligencex todo build-triage-index --repo EvotecIT/IntelligenceX
```

Options:
- `--max-prs <n>` (1-2000, default `100`)
- `--max-issues <n>` (1-2000, default `100`)
- `--duplicate-threshold <0.50-1.0>` (default `0.82`)
- `--best-limit <n>` (default `20`)
- `--out <path>` (default `artifacts/triage/ix-triage-index.json`)
- `--summary <path>` (default `artifacts/triage/ix-triage-index.md`)

Important:
- This is a first-slice ranking/dedupe helper, not a hard merge gate.
- Duplicate detection is deterministic and explainable; semantic/embedding clustering can be layered later.
- PR ranking includes status-check signals (when available) in addition to mergeability/review/churn/recency.
- Item output includes assistive `category` + `tags` and PR `relatedIssues`/`matchedIssueUrl` fields for downstream automation.

## Issue Review (Infra Applicability + Auto-Manage)

Review open issues and detect infra blockers that are likely no longer applicable (for example linked PRs are already merged/closed):

```bash
intelligencex todo issue-review --repo EvotecIT/IntelligenceX
```

Options:
- `--max-issues <n>` (1-2000, default `300`)
- `--stale-days <n>` (default `14`)
- `--min-consecutive-candidates <n>` (default `1`; use `2+` for safer auto-close)
- `--min-auto-close-confidence <0-100>` (default `80`; auto-close requires meeting threshold)
- `--state-path <path>` (default `artifacts/triage/ix-issue-review-state.json`)
- `--no-state` (disable streak persistence)
- `--allow-label <label>` (repeatable; require at least one for auto-close eligibility)
- `--deny-label <label>` (repeatable; never auto-close when present)
- `--proposal-only` (force advisory mode; block close operations)
- `--apply-close` (opt-in mutating mode; closes no-longer-applicable candidates)
- `--close-reason <completed|not-planned>` (default `completed`)
- `--no-comment` (skip managed close note comment)
- `--out <path>` (default `artifacts/triage/ix-issue-review.json`)
- `--summary <path>` (default `artifacts/triage/ix-issue-review.md`)

Safety defaults:
- Dry-run by default (no issue mutation unless `--apply-close` is set).
- Issues with protected labels (`do-not-close`, `keep-open`, `pinned`, `ix/decision:accept`) are never auto-closed.
- Auto-close scope is currently conservative: infra blockers with linked PR references where all linked PRs are resolved.
- For production-like automation, prefer `--min-consecutive-candidates 2` (or higher) with a persisted `--state-path`.
- Proposed actions and confidence are generated for every scanned issue (`close`, `keep-open`, `needs-human-review`, `ignore`).
- Confidence is explainable via `confidenceSignals` (stale bucket, recent issue activity, linked PR age, reopened count).
- Reopened/too-fresh issues are intentionally downgraded to `needs-human-review`.

Workflow automation:
- `.github/workflows/issue-review.yml` runs nightly in dry-run mode.
- Manual close runs require explicit confirmation in workflow dispatch:
  - `apply_close=true`
  - `confirm_apply_close=CLOSE_ISSUES`
  - `min_auto_close_confidence` tune threshold for mutation runs

## PR Watch (Observe Mode Babysitter Snapshot)

Capture deterministic PR babysitter snapshots for a single PR:

```bash
intelligencex todo pr-watch --repo EvotecIT/IntelligenceX --pr 744
```

Options:
- `--repo <owner/name>` (default `EvotecIT/IntelligenceX`)
- `--pr <auto|number|url>` (default `auto`; current branch PR)
- `--once` (default; capture one snapshot and exit)
- `--watch` (continuous snapshots with adaptive backoff until terminal stop reason)
- `--poll-seconds <n>` (watch mode base interval; default `60`)
- `--max-flaky-retries <n>` (classification budget ceiling; default `3`)
- `--retry-cooldown-minutes <n>` (suppress repeat retry recommendation during cooldown; default `15`)
- `--state-file <path>` (override watcher-state file path)
- `--approved-bot <login>` (repeatable approved bot allow-list extension)
- `--apply-retry` (once mode only; executes `retry_failed_checks` if eligible)
- `--confirm-apply-retry RETRY_CHECKS` (required with `--apply-retry`)
- `--phase <observe|assist|repair>` (audit phase marker; default `observe`)
- `--source <value>` (audit source marker; default `manual_cli`)
- `--run-link <url>` (optional workflow/job URL in audit records)
- `--audit-log-path <path>` (JSONL audit path; default `artifacts/pr-watch/ix-pr-watch-audit.jsonl`)

Default approved bots include `intelligencex-review`, `intelligencex-review[bot]`, and `chatgpt-codex-connector[bot]`.

Default outputs:
- JSON snapshot on stdout with:
  - `pr`, `checks`, `failedRuns`, `newReviewItems`, `retryState`, `actions`, `stopReason`, `audit`
- Stateful tracker file at:
  - `artifacts/pr-watch/ix-pr-watch-<owner>-<repo>-pr<Number>.json`
- Audit log (JSONL append-only) at:
  - `artifacts/pr-watch/ix-pr-watch-audit.jsonl`

Workflow automation:
- `.github/workflows/ix-pr-babysit-monitor.yml` runs in observe mode on an hourly schedule.
- Manual targeted run via `workflow_dispatch` supports:
  - `pr` (specific PR number/URL),
  - `max_prs`,
  - `max_flaky_retries`,
  - `include_drafts`,
  - `approved_bots`.
- `.github/workflows/ix-pr-babysit-assist-retry.yml` provides manual guarded retry assist for one PR:
  - requires `confirm_apply_retries=RETRY_CHECKS`,
  - executes `pr-watch --apply-retry` in once mode,
  - persists retry state and cooldown metadata in `artifacts/pr-watch/ix-pr-watch-*.json`.
- `.github/workflows/ix-pr-babysit-nightly-consolidation.yml` runs daily consolidation (supports `workflow_dispatch` and reusable `workflow_call`) with:
  - `max_prs`,
  - `stale_days`,
  - `include_drafts`,
  - `approved_bots`.
- `.github/workflows/ix-pr-babysit-weekly-governance.yml` runs weekly governance consolidation by calling nightly consolidation with weekly defaults:
  - `max_prs=300`,
  - `stale_days=14`,
  - `source=weekly-governance`.
- Workflow artifacts:
  - per-PR snapshots under `artifacts/pr-watch/snapshots/`,
  - rollup JSON `artifacts/pr-watch/ix-pr-watch-rollup.json`,
  - markdown summary `artifacts/pr-watch/ix-pr-watch-summary.md`,
  - audit log JSONL `artifacts/pr-watch/ix-pr-watch-audit.jsonl`,
  - nightly rollup JSON `artifacts/pr-watch/ix-pr-watch-nightly-rollup.json`,
  - nightly markdown summary `artifacts/pr-watch/ix-pr-watch-nightly-summary.md`.

Cadence matrix:

| Cadence | Workflow | Defaults | Goal |
| --- | --- | --- | --- |
| Hourly | `ix-pr-babysit-monitor.yml` | `max_prs=100`, observe mode | detect fresh CI/review regressions quickly |
| Daily | `ix-pr-babysit-nightly-consolidation.yml` | `stale_days=7`, `max_prs=200` | build no-progress backlog for next-day triage |
| Weekly | `ix-pr-babysit-weekly-governance.yml` | `stale_days=14`, `max_prs=300` | governance review of persistent blockers/churn patterns |

## Vision Check (Assistive)

Evaluate PR backlog alignment against `VISION.md`:

```bash
intelligencex todo vision-check --repo EvotecIT/IntelligenceX --vision VISION.md
```

Options:
- `--vision <path>` (default `VISION.md`)
- `--index <path>` (default `artifacts/triage/ix-triage-index.json`)
- `--refresh-index` / `--no-refresh-index`
- `--max-prs <n>` and `--max-issues <n>` used when refreshing index
- `--out <path>` (default `artifacts/triage/ix-vision-check.json`)
- `--summary <path>` (default `artifacts/triage/ix-vision-check.md`)

Output classes:
- `aligned`
- `needs-human-review`
- `likely-out-of-scope`

Explicit policy prefixes supported in `VISION.md` bullets:
- `aligned: ...`
- `likely-out-of-scope: ...`
- `needs-human-review: ...`

## Project Init (GitHub-native)

Create or initialize a GitHub Project for triage/vision workflows:

```bash
intelligencex todo project-init --repo EvotecIT/IntelligenceX --owner EvotecIT
```

Options:
- `--project <n>` initialize fields on existing project instead of creating
- `--title <text>` and `--description <text>` when creating
- `--public` / `--private`
- `--link-repo` / `--no-link-repo`
- `--ensure-labels` / `--no-ensure-labels`
- `--ensure-default-views` / `--no-ensure-default-views`
- `--view-template-project <n>` copy from a template project to preserve saved views
- `--view-template-owner <login>` owner for template project lookup
- `--out <path>` (default `artifacts/triage/ix-project-config.json`)

Note:
- GitHub API currently lacks a `createProjectV2View` mutation; IX can validate/report default view coverage, and template-copy can preserve ready view layouts.

Expected fields:
- `Vision Fit` (single-select)
- `Vision Confidence` (number)
- `Category` (single-select)
- `Tags` (text)
- `Matched Issue` (text)
- `Matched Issue Confidence` (number)
- `Issue Review Action` (single-select)
- `Issue Review Action Confidence` (number)
- `Triage Score` (number)
- `Duplicate Cluster` (text)
- `Canonical Item` (text)
- `Triage Kind` (single-select)
- `Maintainer Decision` (single-select)

## Project Sync

Sync triage, issue-review, and vision artifacts into project items:

```bash
intelligencex todo project-sync --owner EvotecIT --project 123
```

Options:
- `--config <path>` resolve owner/project from `project-init` output
- `--triage <path>`, `--issue-review <path>`, and `--vision <path>`
- `--max-items <n>` (default `500`)
- `--project-item-scan-limit <n>` (default `5000`)
- `--ensure-fields` / `--no-ensure-fields`
- `--apply-labels` (managed IX label sync; stale `ix/*` labels in managed families are removed)
- `--ensure-labels` / `--no-ensure-labels`
- `--apply-link-comments`
- `--dry-run`

Behavior:
- `Issue Review Action` and `Issue Review Action Confidence` are synced on issue items when `ix-issue-review.json` is available.

## Project Bootstrap (Project + Workflow in one command)

Recommended first-run command:

```bash
intelligencex todo project-bootstrap --repo EvotecIT/IntelligenceX --owner EvotecIT
```

Default outputs:
- `artifacts/triage/ix-project-config.json`
- `.github/workflows/ix-triage-project-sync.yml`
- `VISION.md` (starter template if missing)

Options:
- `--project <n>` use existing project instead of creating
- `--workflow-out <path>`
- `--vision-out <path>` scaffold a vision file at custom location
- `--config-out <path>`
- `--max-items <n>` default sync size for schedule runs
- `--skip-project-init` regenerate workflow from existing config only
- `--force-workflow-write` overwrite existing workflow file
- `--skip-vision-scaffold` do not create/update vision file
- `--force-vision-write` overwrite existing vision file
- `--view-template-project <n>` pass template-copy through to `project-init`
- `--view-template-owner <login>` template owner override for `project-init`
- `--ensure-default-views` / `--no-ensure-default-views` pass-through to `project-init`
- `--control-issue <n>` set `IX_TRIAGE_CONTROL_ISSUE` to an existing issue number
- `--create-control-issue` create a control issue and set `IX_TRIAGE_CONTROL_ISSUE`
- `--control-issue-title <text>` customize the title when creating a control issue

## Project View Checklist (Maintainer Assist)

Generate a markdown checklist for default GitHub Project views and optionally post it on an issue:

```bash
intelligencex todo project-view-checklist --config artifacts/triage/ix-project-config.json --create-issue
```

Options:
- `--owner <login>` and `--project <n>` target project directly
- `--repo <owner/name>` repository context for issue posting
- `--out <path>` output markdown path
- `--print` print markdown to stdout
- `--issue <n>` upsert comment on existing issue
- `--create-issue` create issue with checklist markdown body
- `--issue-title <text>` custom issue title when creating

## Project View Apply Plan (Maintainer Assist)

Generate a deterministic apply plan for missing default views and optionally post it on an issue:

```bash
intelligencex todo project-view-apply --config artifacts/triage/ix-project-config.json --create-issue --open-web
```

Options:
- `--owner <login>` and `--project <n>` target project directly
- `--repo <owner/name>` repository context for issue posting
- `--out <path>` output markdown path
- `--print` print markdown to stdout
- `--issue <n>` upsert comment on existing issue
- `--create-issue` create issue with apply-plan markdown body
- `--issue-title <text>` custom issue title when creating
- `--open-web` open project web UI after generating plan
- `--fail-if-missing` exit with code `2` when recommended views are missing

## Workflow Template

Template path:
- `IntelligenceX.Cli/Templates/triage-index-scheduled.yml`
- `IntelligenceX.Cli/Templates/triage-project-sync.yml`

Behavior:
- Scheduled + manual runs.
- Generates triage index artifacts.
- Optional control-issue summary comment upsert when repo variable `IX_TRIAGE_CONTROL_ISSUE` is configured.
- `triage-index-scheduled.yml` upserts a single marker comment with the latest triage index summary on the control issue.
- `triage-project-sync.yml` upserts a single marker comment with the latest combined triage + issue-review + vision markdown summary on the control issue.
- Both workflows also upsert a shared `intelligencex:triage-control-dashboard` comment linking to the latest summary comments.
- The shared dashboard comment includes quick links: control issue, `VISION.md`, project board (when `artifacts/triage/ix-project-config.json` is available), project-view apply issue (`IX_PROJECT_VIEW_APPLY_ISSUE`), and bootstrap links comment.
- `todo project-bootstrap --create-control-issue` can configure the control issue variable automatically.

## Legacy Script

Removed. Use the CLI command above.
