# Project Views and Operations

This page covers maintainer-facing operations after project setup: view checklist/apply planning, bot-feedback sync, triage index generation, and vision drift checks.

## Important disclaimer

These commands provide assistive operational intelligence, not unattended production governance.

- Keep human review in the loop for merge/close/defer decisions.
- Use output confidence and links as context, not as sole authority.

## View checklist and apply plan

GitHub Project views are not fully creatable through public API mutations today. IntelligenceX therefore provides deterministic checklist/apply-plan guidance.

### Build a checklist of recommended views

```bash
intelligencex todo project-view-checklist \
  --config artifacts/triage/ix-project-config.json \
  --create-issue
```

Useful options:

- `--issue <n>` upserts checklist comment on an existing issue.
- `--issue-title <text>` customizes title when creating issue.
- `--print` emits markdown to stdout.

### Build an apply plan for missing views

```bash
intelligencex todo project-view-apply \
  --config artifacts/triage/ix-project-config.json \
  --create-issue \
  --open-web
```

Useful options:

- `--fail-if-missing` exits with code `2` when recommended views are missing.
- `--issue <n>` upserts plan comment to an existing issue.
- `--out <path>` writes markdown to custom location.

Both checklist and apply-plan outputs include recommended columns per view. Use those column sets, otherwise triage quality signals stay hidden and the board appears weak.
Recommended columns now include operational PR signals such as `PR Size`, `PR Churn Risk`, `PR Merge Readiness`, `PR Freshness`, `PR Check Health`, `PR Review Latency`, and `PR Merge Conflict Risk`.
Default view guidance now also includes `Issue Ops`, which highlights issue applicability fields (`Issue Review Action`, `Issue Review Action Confidence`) for infra-blocker triage.

## Sync bot feedback into TODO.md

Use this to keep explicit bot checklist tasks visible in one backlog:

```bash
intelligencex todo sync-bot-feedback --repo EvotecIT/IntelligenceX
```

Create issues for unchecked tasks:

```bash
intelligencex todo sync-bot-feedback \
  --repo EvotecIT/IntelligenceX \
  --create-issues \
  --label ix-bot-feedback \
  --max-issues 20
```

Behavior notes:

- Imports explicit markdown task-list items only (`- [ ]` / `- [x]`).
- Preserves existing checkbox state in `TODO.md` where possible.
- Uses stable markers for issue deduplication when `--create-issues` is enabled.

## Build triage, issue-review, and vision artifacts

### Build triage index

```bash
intelligencex todo build-triage-index \
  --repo EvotecIT/IntelligenceX \
  --max-prs 300 \
  --max-issues 300 \
  --duplicate-threshold 0.82 \
  --best-limit 20
```

Outputs:

- `artifacts/triage/ix-triage-index.json`
- `artifacts/triage/ix-triage-index.md`

### Build issue-review signals

```bash
intelligencex todo issue-review \
  --repo EvotecIT/IntelligenceX \
  --proposal-only \
  --state-path artifacts/triage/ix-issue-review-state.json \
  --out artifacts/triage/ix-issue-review.json \
  --summary artifacts/triage/ix-issue-review.md
```

Outputs:

- `artifacts/triage/ix-issue-review.json`
- `artifacts/triage/ix-issue-review.md`

### Run vision check

```bash
intelligencex todo vision-check \
  --repo EvotecIT/IntelligenceX \
  --vision VISION.md \
  --refresh-index \
  --drift-threshold 0.70 \
  --no-fail-on-drift
```

Outputs:

- `artifacts/triage/ix-vision-check.json`
- `artifacts/triage/ix-vision-check.md`

## Suggested maintainer cadence

1. Run `sync-bot-feedback` during PR-review sweeps.
2. Refresh triage/issue-review/vision artifacts daily or per release train.
3. Sync project fields before backlog grooming.
4. Triage `Signal Quality = low` items first and request better PR/issue context before acting on recommendations.
5. Regenerate view checklist/apply plan when project layout drifts.

## Related docs

- [Projects + PR Monitoring](/docs/reviewer/projects-pr-monitoring/)
- [Project Bootstrap and Sync](/docs/reviewer/project-bootstrap-sync/)
