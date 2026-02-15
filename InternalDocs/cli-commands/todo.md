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
- `--out <path>` (default `artifacts/triage/ix-project-config.json`)

Expected fields:
- `Vision Fit` (single-select)
- `Vision Confidence` (number)
- `Category` (single-select)
- `Tags` (text)
- `Matched Issue` (text)
- `Matched Issue Confidence` (number)
- `Triage Score` (number)
- `Duplicate Cluster` (text)
- `Canonical Item` (text)
- `Triage Kind` (single-select)
- `Maintainer Decision` (single-select)

## Project Sync

Sync triage and vision artifacts into project items:

```bash
intelligencex todo project-sync --owner EvotecIT --project 123
```

Options:
- `--config <path>` resolve owner/project from `project-init` output
- `--triage <path>` and `--vision <path>`
- `--max-items <n>` (default `500`)
- `--project-item-scan-limit <n>` (default `5000`)
- `--ensure-fields` / `--no-ensure-fields`
- `--apply-labels`
- `--ensure-labels` / `--no-ensure-labels`
- `--dry-run`

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
- `--control-issue <n>` set `IX_TRIAGE_CONTROL_ISSUE` to an existing issue number
- `--create-control-issue` create a control issue and set `IX_TRIAGE_CONTROL_ISSUE`
- `--control-issue-title <text>` customize the title when creating a control issue

## Workflow Template

Template path:
- `IntelligenceX.Cli/Templates/triage-index-scheduled.yml`
- `IntelligenceX.Cli/Templates/triage-project-sync.yml`

Behavior:
- Scheduled + manual runs.
- Generates triage index artifacts.
- Optional control-issue comment when repo variable `IX_TRIAGE_CONTROL_ISSUE` is configured.
- `triage-project-sync.yml` posts a combined triage + vision markdown summary to the control issue.
- `todo project-bootstrap --create-control-issue` can configure the control issue variable automatically.

## Legacy Script

Removed. Use the CLI command above.
