---
title: Project Ops Demo Capture Runbook
slug: demo-capture-runbook
---

# Project Ops Demo Capture Runbook

Use this runbook to produce reliable, real screenshots for `Issue Ops` and Project triage docs.

All logic should come from built-in `intelligencex todo ...` commands. Do not use external scripts for decision logic.

## Important disclaimer

Project Ops is assistive automation, not autonomous production governance.

- Treat suggested actions and confidence as recommendations.
- Keep close and keep-open decisions human-owned.
- Use proposal-only and dry-run modes before mutating operations.

## Demo goals

Capture screenshots that prove three things:

1. `Issue Review Action` is populated by real IX runs.
2. `Issue Review Action Confidence` and linked PR context are visible in the project table.
3. Maintainers can review recommendations before any close action is applied.

## Prerequisites

- A repository with active issues and pull requests.
- GitHub Project access (`project` scope).
- IX CLI authentication completed.
- Existing or bootstrap-ready project configuration.

## End-to-end demo flow

Run these commands from the target repository root.

```bash
# 1) Bootstrap project fields and local config artifacts
intelligencex todo project-bootstrap --repo owner/repo --owner owner

# 2) Build triage index for PR and issue context
intelligencex todo build-triage-index --repo owner/repo

# 3) Run issue applicability review (non-mutating)
intelligencex todo issue-review --repo owner/repo --proposal-only --min-consecutive-candidates 2 --min-auto-close-confidence 80 --state-path artifacts/triage/ix-issue-review-state.json

# 4) Optional: align backlog with VISION.md
intelligencex todo vision-check --repo owner/repo --vision VISION.md

# 5) Sync signals into project fields in dry-run first
intelligencex todo project-sync --config artifacts/triage/ix-project-config.json --issue-review artifacts/triage/ix-issue-review.json --dry-run

# 6) Apply after maintainer approval
intelligencex todo project-sync --config artifacts/triage/ix-project-config.json --issue-review artifacts/triage/ix-issue-review.json
```

## Screenshot shot list

Capture these in one session so filters and state are consistent:

1. Project board overview with open issues and triage columns visible.
2. Table focused on `Issue Review Action` and `Issue Review Action Confidence`.
3. Row example showing linked PR context (`Matched Pull Request` or related PR fields).
4. Markdown summary from `artifacts/triage/ix-issue-review.md`.

## Capture quality checklist

- Resolution: `>= 1920x1080`.
- Keep the same theme and zoom level across all captures.
- Do not crop out key columns used in the narrative.
- Avoid screenshots with hidden filters that cannot be explained.
- Remove unrelated personal or sensitive data before publishing.

## Naming convention

Use deterministic names for website assets:

- `ix-issue-ops-01-board-overview.png`
- `ix-issue-ops-02-review-columns.png`
- `ix-issue-ops-03-linked-pr-context.png`
- `ix-issue-ops-04-issue-review-summary.png`

Place them in:

`Website/static/assets/screenshots/ix-issue-ops/`

## Publish checklist

1. Replace placeholder references in docs or blog pages.
2. Verify each image has explicit width, height, loading, and decoding hints.
3. Run website validation:
   - `Website/build.ps1 -Dev`
   - `Website/build.ps1 -CI`
4. Open PR with before/after screenshots in the description.

## Related docs

- [Project Ops Overview](/docs/project-ops/overview/)
- [Projects + PR Monitoring](/docs/reviewer/projects-pr-monitoring/)
- [Project Views and Operations](/docs/reviewer/project-views-and-ops/)
