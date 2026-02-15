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

## Workflow Template

Template path:
- `IntelligenceX.Cli/Templates/triage-index-scheduled.yml`

Behavior:
- Scheduled + manual runs.
- Generates triage index artifacts.
- Optional control-issue comment when repo variable `IX_TRIAGE_CONTROL_ISSUE` is configured.

## Legacy Script

Removed. Use the CLI command above.
