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
5. Re-check PR checks and bot todo/critical items
6. Repeat within timebox
7. Merge only when required checks are green and PR is mergeable

## Commands
- Snapshot state:
  - `.agents/skills/intelligencex-pr-unblock-loop/scripts/gather-pr-state.sh <pr-number>`
- Watch checks:
  - `.agents/skills/intelligencex-pr-unblock-loop/scripts/watch-checks.sh <pr-number>`

## Fail-Fast Rules
- Do not churn code for infra-blocked failures.
- Do not re-fix reworded non-actionable bot churn.
- Stop after 2 full iterations or 60 minutes unless maintainers request more.

## References
- `references/status-template.md`
