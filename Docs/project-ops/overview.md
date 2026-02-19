---
title: Project Ops Overview
slug: overview
---

# Project Ops

Project Ops is the maintainer-first control plane for backlog triage in GitHub Projects.

It combines:

- `issue-review` for stale or no-longer-applicable infra blockers
- `build-triage-index` for duplicate clustering and ranking
- `vision-check` for scope alignment against `VISION.md`
- `project-sync` for pushing signals into Project fields

## Important disclaimer

Project Ops is assistive automation, not autonomous production governance.

- Treat proposed actions and confidence as recommendations.
- Keep close/defer/accept decisions human-owned.
- Use dry-run and proposal-only modes before any mutating workflow.

## What this looks like in practice

Issue Ops creates a maintainer-focused board view for stale/no-longer-applicable blockers:

![Issue Ops project board overview with issue review action and confidence fields visible for infra blocker triage](/assets/screenshots/ix-issue-ops/ix-issue-ops-01-board-overview.svg)

Key decision fields are synced directly to project rows:

- `Issue Review Action`
- `Issue Review Action Confidence`
- `Matched Pull Request`
- `Signal Quality`

![Issue Ops table detail with action confidence and matched pull request columns used during issue applicability review](/assets/screenshots/ix-issue-ops/ix-issue-ops-02-review-columns.svg)

## Recommended start

```bash
intelligencex todo project-bootstrap --repo EvotecIT/IntelligenceX --owner EvotecIT
intelligencex todo issue-review --repo EvotecIT/IntelligenceX --proposal-only --min-consecutive-candidates 2 --min-auto-close-confidence 80
intelligencex todo project-sync --config artifacts/triage/ix-project-config.json --issue-review artifacts/triage/ix-issue-review.json --dry-run
```

## Related docs

- [Project Ops Related Docs Hub](/docs/project-ops/related-docs/)
