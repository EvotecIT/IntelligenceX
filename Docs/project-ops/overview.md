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

## Recommended start

```bash
intelligencex todo project-bootstrap --repo EvotecIT/IntelligenceX --owner EvotecIT
intelligencex todo issue-review --repo EvotecIT/IntelligenceX --proposal-only --min-consecutive-candidates 2 --min-auto-close-confidence 80
intelligencex todo project-sync --config artifacts/triage/ix-project-config.json --issue-review artifacts/triage/ix-issue-review.json --dry-run
```

## Related docs

- [Projects + PR Monitoring](/docs/reviewer/projects-pr-monitoring/)
- [Project Bootstrap and Sync](/docs/reviewer/project-bootstrap-sync/)
- [Project Views and Operations](/docs/reviewer/project-views-and-ops/)
