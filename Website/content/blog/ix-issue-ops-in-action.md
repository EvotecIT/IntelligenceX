---
title: IX Issue Ops in Action
description: First-party GitHub Project triage with issue applicability review, confidence signals, and maintainer-owned close or keep-open decisions.
slug: ix-issue-ops-in-action
date: 2026-02-14
categories: ["Walkthrough"]
tags: ["issue-ops", "github-projects", "triage"]
image: /assets/screenshots/ix-issue-ops/ix-issue-ops-01-board-overview.svg
collection: blog
layout: page
---

Issue Ops extends reviewer workflows into backlog operations.
It helps maintainers spot stale or no-longer-applicable infra blockers, but keeps final decisions human-owned.

## Why This Exists

Issue lists decay fast: old infra blockers stay open, linked PR context drifts, and teams lose confidence in the backlog.
Issue Ops gives maintainers one triage surface where every recommendation includes a confidence score and explainable signals.

## Board Overview

The project view below is optimized for issue triage at scale.
You can quickly separate likely-invalid blockers from items that still need active ownership.

<img src="/assets/screenshots/ix-issue-ops/ix-issue-ops-01-board-overview.svg" alt="Issue Ops project board view with open infra blocker issues, status, linked pull request columns, and quick filtering for maintainer triage" width="1600" height="900" loading="lazy" decoding="async" />

## Signals That Matter

Each issue recommendation includes:

- `Issue Review Action` (`close`, `keep-open`, `needs-human-review`)
- `Issue Review Action Confidence` (`0-100`, with high/medium/low levels)
- linked PR context (`Matched Pull Request`, related PRs)
- supporting confidence signals (stale age, reopen history, activity recency)

<img src="/assets/screenshots/ix-issue-ops/ix-issue-ops-02-review-columns.svg" alt="Issue Ops table columns showing Issue Review Action, confidence score, and linked pull request matching used for stale infra blocker triage" width="1600" height="900" loading="lazy" decoding="async" />

## Safety First

Issue Ops is assistive, not autonomous governance.

- Do not run unattended close/merge decisions in production.
- Use dry-run and proposal-only passes first.
- Require a maintainer-owned approval step for mutating actions.

## Start Here

```bash
intelligencex todo project-bootstrap --repo owner/repo --owner owner
intelligencex todo issue-review --repo owner/repo --proposal-only --min-consecutive-candidates 2
intelligencex todo project-sync --config artifacts/triage/ix-project-config.json --issue-review artifacts/triage/ix-issue-review.json --dry-run
```

## Related Docs

- [Project Ops Related Docs Hub](/docs/project-ops/related-docs/)
