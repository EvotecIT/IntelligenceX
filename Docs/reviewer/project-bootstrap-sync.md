---
title: Project Bootstrap and Sync
description: Set up a GitHub Project for IntelligenceX triage and keep project fields synced with backlog artifacts and review signals.
---

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

## Sync triage, issue-review, and vision into project fields

After generating triage artifacts (including `ix-issue-review.json`), run `project-sync`:

```bash
intelligencex todo project-sync \
  --config artifacts/triage/ix-project-config.json \
  --triage artifacts/triage/ix-triage-index.json \
  --issue-review artifacts/triage/ix-issue-review.json \
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
- Issue-review fields for stale infra blockers (`Issue Review Action`, `Issue Review Action Confidence`).
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

The project-sync workflow template runs `issue-review` in proposal mode before sync, and emits:

- `artifacts/triage/ix-issue-review.json`
- `artifacts/triage/ix-issue-review.md`
- `artifacts/triage/ix-project-view-apply.md`
- a control-issue dashboard comment that can also surface the open `pr-watch` governance tracker status when weekly or daily tracker issues exist

The shared triage control dashboard now checks for open `pr-watch` tracker issues with source markers `weekly-governance` first and `schedule` second. When found, it links the tracker issue and copies the compact `Governance:` status line into the dashboard so maintainers can see retry-policy guidance without leaving the control issue.

If you want that same live governance signal to show up directly on synced PR items, `triage-project-sync.yml` can also opt into a managed PR label path. Manual runs expose `apply_pr_watch_governance_labels`, and scheduled runs can read repo variable `IX_TRIAGE_APPLY_PR_WATCH_GOVERNANCE_LABELS`. Both stay off by default.

For teams that prefer project-board sorting over labels, the same workflow can also opt into project fields `PR Governance Signal` and `PR Governance Summary`. Manual runs expose `apply_pr_watch_governance_fields`, and scheduled runs can read repo variable `IX_TRIAGE_APPLY_PR_WATCH_GOVERNANCE_FIELDS`. This is independent from the label toggle and also stays off by default.

If those fields are enabled and teams want a dedicated board lane for them, the workflow can also opt into the optional `Governance Review` view profile. Manual runs expose `include_pr_watch_governance_views`, and scheduled runs can read repo variable `IX_TRIAGE_INCLUDE_PR_WATCH_GOVERNANCE_VIEWS`. The bootstrap CLI can also pass this profile through to `project-init` with `--include-pr-watch-governance-views`.

Generated `ix-project-config.json` files now also carry a machine-readable `features.prWatchGovernance` block. The view-assist commands use that intent as a default when resolving optional governance view coverage from config, and `todo project-sync` now uses the same config intent for governance label/field defaults when a config file is present. One-off runs can still force either path off with `--no-apply-pr-watch-governance-labels` or `--no-apply-pr-watch-governance-fields`.

Recommended rollout:

1. Run sync manually with `--dry-run`.
2. Run one controlled write sync.
3. Enable schedule after maintainers validate resulting field quality.

## Related docs

- [Projects + PR Monitoring](/docs/reviewer/projects-pr-monitoring/)
- [Project Views and Operations](/docs/reviewer/project-views-and-ops/)
