---
name: intelligencex-pr-unblock-loop
description: Use when iterating on PR blockers, CI failures, and bot todo or critical items until mergeability is restored.
---

# Skill: intelligencex-pr-unblock-loop

Use this skill when a PR must be unblocked/merged and checks or bot comments require iteration.

## Trigger Phrases
- "unblock PR"
- "fix CI"
- "address bot comments"
- "merge blockers"
- "review loop"

## Strict Execution Order
1. Snapshot PR state and latest bot comments
2. Classify blockers (actionable vs infra)
3. Apply one coherent fix batch
4. Re-run local validation relevant to changed area
5. Re-check PR checks, bot todo/critical items, and unresolved review threads
6. If checks are green but merge is still blocked, inspect `reviewThreads` for unresolved conversations before assuming another hidden policy problem
7. Repeat within timebox
8. Merge only when required checks are green, unresolved review threads are resolved, and the PR is actually mergeable/clean

## Commands
- Snapshot state:
  - Bash: `.agents/skills/intelligencex-pr-unblock-loop/scripts/gather-pr-state.sh <pr-number>`
  - PowerShell: `pwsh -NoLogo -NoProfile -File .agents/skills/intelligencex-pr-unblock-loop/scripts/gather-pr-state.ps1 <pr-number>`
- Watch checks:
  - Bash: `.agents/skills/intelligencex-pr-unblock-loop/scripts/watch-checks.sh <pr-number>`
  - PowerShell: `pwsh -NoLogo -NoProfile -File .agents/skills/intelligencex-pr-unblock-loop/scripts/watch-checks.ps1 <pr-number>`
- Review threads:
  - `gh api graphql -f query='query { repository(owner:"EvotecIT", name:"IntelligenceX") { pullRequest(number:<pr-number>) { reviewThreads(first:100) { nodes { id isResolved isOutdated path line comments(first:10) { nodes { author { login } body url } } } } } } }'`

## Fail-Fast Rules
- Do not churn code for infra-blocked failures.
- Do not re-fix reworded non-actionable bot churn.
- If branch protection requires conversation resolution, treat unresolved review threads as real merge blockers even when the latest review summary looks clean.
- Resolve stale threads only after verifying the underlying code issue is already fixed.
- Stop after 2 full iterations or 60 minutes unless maintainers request more.

## References
- `references/status-template.md`
