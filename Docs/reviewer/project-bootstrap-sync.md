# Project Bootstrap and Sync

Use this flow to set up a GitHub Project for triage and keep it continuously synced with IntelligenceX backlog artifacts.

## Important disclaimer

This pipeline is assistive. It should not be treated as an autonomous production approval/merge system.

- Keep a human owner for final triage and merge decisions.
- Use project fields as signals, not hard gates.
- Start with `--dry-run` for any mutating sync rollout.

## Prerequisites

- GitHub token with `project` scope.
- For sync reads/writes: `read:project` + `project`.
- For issue/comment operations: issue write permissions.

## First-run bootstrap (recommended)

`project-bootstrap` is the fastest path for new adopters.

```bash
intelligencex todo project-bootstrap \
  --repo EvotecIT/IntelligenceX \
  --owner EvotecIT \
  --create-control-issue \
  --control-issue-title "IX Triage Control"
```

Default outputs:

- `artifacts/triage/ix-project-config.json`
- `.github/workflows/ix-triage-project-sync.yml`
- `VISION.md` (scaffolded if missing)

## Initialize an existing project

If you already have a GitHub Project, initialize IX fields/metadata with `project-init`:

```bash
intelligencex todo project-init \
  --repo EvotecIT/IntelligenceX \
  --owner EvotecIT \
  --project 123 \
  --out artifacts/triage/ix-project-config.json
```

Useful options:

- `--view-template-project <n>` and `--view-template-owner <login>` to preserve existing saved views from a template project.
- `--ensure-default-views` to validate recommended view coverage.
- `--no-ensure-labels` to skip taxonomy setup when labels are managed elsewhere.

## Sync triage and vision into project fields

After generating triage artifacts, run `project-sync`:

```bash
intelligencex todo project-sync \
  --config artifacts/triage/ix-project-config.json \
  --triage artifacts/triage/ix-triage-index.json \
  --vision artifacts/triage/ix-vision-check.json \
  --max-items 500
```

Dry-run example:

```bash
intelligencex todo project-sync \
  --config artifacts/triage/ix-project-config.json \
  --dry-run
```

Extended sync example with labels and link comments:

```bash
intelligencex todo project-sync \
  --config artifacts/triage/ix-project-config.json \
  --apply-labels \
  --ensure-labels \
  --apply-link-comments \
  --link-comment-min-confidence 0.55 \
  --link-comment-max-issues 3
```

## What sync updates

- Project item fields for triage and vision fit.
- Signal-quality fields (`Signal Quality`, `Signal Quality Score`, `Signal Quality Notes`).
- Operational PR fields (`PR Size`, `PR Churn Risk`, `PR Merge Readiness`, `PR Freshness`, `PR Check Health`, `PR Review Latency`, `PR Merge Conflict Risk`).
- Suggested maintainership decision signal (`IX Suggested Decision`).
- Optional managed label reconciliation for IX taxonomies.
- Optional assistive cross-link comments for related PR/issue context.

## Scheduled automation

The bootstrap command writes a workflow that can run this continuously.

Template files:

- `IntelligenceX.Cli/Templates/triage-index-scheduled.yml`
- `IntelligenceX.Cli/Templates/triage-project-sync.yml`

The project-sync workflow template also emits `artifacts/triage/ix-project-view-apply.md` so maintainers can continuously see missing view coverage and recommended columns.

Recommended rollout:

1. Run sync manually with `--dry-run`.
2. Run one controlled write sync.
3. Enable schedule after maintainers validate resulting field quality.

## Related docs

- [Projects + PR Monitoring](/docs/reviewer/projects-pr-monitoring/)
- [Project Views and Operations](/docs/reviewer/project-views-and-ops/)
