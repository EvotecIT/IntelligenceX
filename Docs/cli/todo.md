# TODO Backlog Sync (Bot Feedback)

Maintainers can aggregate explicit checklist items from IntelligenceX bot reviews into `TODO.md` under:

`## Review Feedback Backlog (Bots)`

## Sync

```bash
intelligencex todo sync-bot-feedback --repo EvotecIT/IntelligenceX
```

Notes:
- Only explicit markdown task list items are imported (`- [ ] ...`, `- [x] ...`).
- Tasks are grouped by PR and kept in collapsible `<details>` blocks.
- Re-running the sync should be safe and should avoid noisy diffs.
- This is a repo-level backlog file (not “a TODO for a single PR”). Each PR gets its own collapsible block.
- Existing PR blocks are matched by PR number and updated in-place.
- Task items are merged by task text (case-insensitive) so manual checkbox state in `TODO.md` is preserved. If a bot rewords an item, it will appear as a new task.
- The sync does not delete tasks that disappeared from a PR review; remove them manually if they become stale/noise.

## Optional: create issues for unchecked items

```bash
intelligencex todo sync-bot-feedback --repo EvotecIT/IntelligenceX --create-issues
```

Notes:
- Issue creation is opt-in. It creates issues only for unchecked tasks after merging with existing `TODO.md` state, so manually checking a task in `TODO.md` suppresses issue creation for that task.
- Issues are deduplicated using a stable `ix-bot-feedback-id:<id>` marker embedded in the issue body.
- Issues are labeled with `--label` (default: `ix-bot-feedback`).

## Build triage index (PRs + Issues)

Generate a machine-readable index to help de-duplicate backlog items and rank likely merge candidates.

```bash
intelligencex todo build-triage-index --repo EvotecIT/IntelligenceX
```

Default outputs:
- `artifacts/triage/ix-triage-index.json`
- `artifacts/triage/ix-triage-index.md`

Useful options:

```bash
intelligencex todo build-triage-index \
  --repo EvotecIT/IntelligenceX \
  --max-prs 100 \
  --max-issues 100 \
  --duplicate-threshold 0.82 \
  --best-limit 20 \
  --out artifacts/triage/openclaw-index.json \
  --summary artifacts/triage/openclaw-index.md
```

Notes:
- Uses open PRs and open issues only.
- Supports paginated fetches for larger backlogs (`--max-prs` / `--max-issues` up to 2000).
- Uses token-based similarity for duplicate clusters (deterministic and explainable).
- PR ranking uses mergeability/review/status-check/churn/recency signals as assistive scoring, not an automatic merge decision.
- Emits assistive `category` + `tags` per item for reliable labeling workflows.
- Emits PR `relatedIssues` candidates plus `matchedIssueUrl`/`matchedIssueConfidence` (explicit references like `closes #123` + similarity fallback).

## Vision check (assistive scope alignment)

Evaluate PR backlog alignment against a local vision document (default: `VISION.md`).

```bash
intelligencex todo vision-check --repo EvotecIT/IntelligenceX --vision VISION.md
```

Default outputs:
- `artifacts/triage/ix-vision-check.json`
- `artifacts/triage/ix-vision-check.md`

Useful options:

```bash
intelligencex todo vision-check \
  --repo EvotecIT/IntelligenceX \
  --vision VISION.md \
  --index artifacts/triage/ix-triage-index.json \
  --refresh-index \
  --max-prs 500 \
  --max-issues 500 \
  --out artifacts/triage/openclaw-vision.json \
  --summary artifacts/triage/openclaw-vision.md
```

Notes:
- Classification is assistive (`aligned`, `needs-human-review`, `likely-out-of-scope`), not an automatic reject gate.
- Uses `VISION.md` section heuristics (`In Scope`, `Out of Scope`, `Goals`, `Non-Goals`) plus token overlap.
- Supports explicit policy bullets for stronger guidance:
  - `aligned: ...`
  - `likely-out-of-scope: ...`
  - `needs-human-review: ...`

## Initialize GitHub Project (assistive control plane)

Create or initialize a GitHub Project with IX triage/vision fields.

```bash
intelligencex todo project-init \
  --repo EvotecIT/IntelligenceX \
  --owner EvotecIT \
  --title "IX Triage Control" \
  --description "IntelligenceX triage and vision control plane" \
  --out artifacts/triage/ix-project-config.json
```

Notes:
- Creates a project (or initializes an existing one with `--project <n>`).
- Ensures required custom fields such as `Vision Fit`, `Category`, `Tags`, `Matched Issue`, `Triage Score`, `Duplicate Cluster`, and `IX Suggested Decision`.
- Ensures IX label taxonomy in the repo by default (`--no-ensure-labels` to skip).
- Writes a reusable config file containing owner/project/field metadata.
- Requires GitHub token scopes: `project` (and typically `read:project` for follow-up sync).

## Sync triage + vision into GitHub Project

Push triage and vision outputs into project item fields so maintainers can triage in GitHub only.

```bash
intelligencex todo project-sync \
  --owner EvotecIT \
  --project 123 \
  --triage artifacts/triage/ix-triage-index.json \
  --vision artifacts/triage/ix-vision-check.json \
  --max-items 500
```

Useful options:
- `--config <path>` to resolve owner/project from `project-init` output.
- `--ensure-fields` / `--no-ensure-fields`.
- `--apply-labels` to apply IX labels (`ix/category:*`, `ix/vision:*`, `ix/match:*`, `ix/decision:*`) on PRs/issues.
- `--ensure-labels` / `--no-ensure-labels` for label taxonomy management.
- `--apply-link-comments` to upsert assistive link comments on PRs (related issues) and issues (related PRs).
- `--project-item-scan-limit <n>` for larger projects.
- `--dry-run` for a no-write sync preview.

Behavior:
- `IX Suggested Decision` is populated automatically (`accept`, `defer`, `reject`, `merge-candidate`) from combined triage + vision signals.
- `Maintainer Decision` remains human-owned for final triage decisions.

## Bootstrap project + workflow (recommended first run)

Create/initialize the GitHub Project and generate a ready workflow in one command:

```bash
intelligencex todo project-bootstrap \
  --repo EvotecIT/IntelligenceX \
  --owner EvotecIT
```

Default outputs:
- `artifacts/triage/ix-project-config.json`
- `.github/workflows/ix-triage-project-sync.yml`
- `VISION.md` (starter template if missing)

Useful options:
- `--project <n>` to bootstrap against an existing project.
- `--workflow-out <path>` to choose a different workflow file name.
- `--vision-out <path>` to scaffold the vision file at a custom location.
- `--config-out <path>` to control where project metadata is written.
- `--max-items <n>` to set default sync volume for scheduled runs.
- `--skip-project-init` to only regenerate workflow from existing config.
- `--force-workflow-write` to overwrite an existing workflow file.
- `--skip-vision-scaffold` to keep the bootstrap workflow only.
- `--force-vision-write` to overwrite an existing vision file.

## GitHub Actions template

A reusable scheduled workflow template is available at:

- `IntelligenceX.Cli/Templates/triage-index-scheduled.yml`
- `IntelligenceX.Cli/Templates/triage-project-sync.yml`

It runs `build-triage-index`, uploads artifacts, and can optionally post the markdown summary to a control issue
when repository variable `IX_TRIAGE_CONTROL_ISSUE` is set.
For `triage-project-sync.yml`, the posted comment includes both triage and vision markdown summaries.

## Options

```bash
intelligencex todo sync-bot-feedback \
  --repo EvotecIT/IntelligenceX \
  --todo TODO.md \
  --max-prs 30 \
  --bot intelligencex-review \
  --create-issues \
  --label ix-bot-feedback \
  --max-issues 20
```
