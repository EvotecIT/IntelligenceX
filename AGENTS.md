# IntelligenceX Agent Playbook

This file defines how automated agents should operate in this repo. Follow it for all changes and PR management.

**Core Rules**
- Use `gh` CLI for PR, review, and CI workflows.
- Work in a dedicated worktree + branch per task.
- Do not touch the Website PR or website work unless explicitly requested.
- Do not use destructive git commands (`reset --hard`, `checkout --`).

**Worktree + Branch**
1. Create a worktree for every task: `git worktree add -b <branch> <path> origin/master`.
2. Make changes only in that worktree.
3. Keep branches focused to a single change set.

**PR Review and CI**
1. List PRs: `gh pr list --repo EvotecIT/IntelligenceX --state open`.
2. Inspect details: `gh pr view <num> --repo EvotecIT/IntelligenceX`.
3. Check CI: `gh pr checks <num> --repo EvotecIT/IntelligenceX`.
4. If CI fails, inspect logs: `gh run view <run-id> --repo EvotecIT/IntelligenceX --log --job <job-id>`.
5. Classify CI failures before making code changes:
   - Actionable: compilation/test failures, lints, static analysis findings, reviewer bot blockers. Fix in code and re-run checks.
   - Infra-blocked: GitHub billing/spend-limit, runner capacity/unavailable, third-party premium/auth gating (Copilot/Claude/etc), or GitHub outage. Do not churn on code changes. Record the blocker and proceed per the PR Handling Loop infra rule.
6. Rebase onto `origin/master` before merge if needed.
7. Merge only when all required checks pass and the PR is mergeable.
8. Merge with squash + delete branch: `gh pr merge <num> --repo EvotecIT/IntelligenceX --squash --delete-branch`.

**PR Handling Loop (Required)**
When an agent is assigned a PR to improve or unblock, it must iterate until merge blockers are clean.

1. Read the latest IntelligenceX bot review comment.
2. Treat these sections as merge blockers: `Todo List ✅` and `Critical Issues ⚠️`.
3. Treat `Other Issues 🧯` and `Next Steps 🚀` as non-blocking unless maintainers explicitly escalate them.
4. Triage other automated reviews (for example the “Claude Code Review” sticky comment) and fix anything that impacts correctness, security, or reliability.
5. Fix inline comments only when they map to merge blockers or correctness/security/reliability issues; ignore style-only nits from other bots unless maintainers explicitly escalate them.
6. Apply fixes, then re-run checks and re-check bot output:
   Run: `gh pr checks <num> --repo EvotecIT/IntelligenceX`
   If the bot posts new todo/critical items, repeat.
7. Infra-blocked escape hatch:
   - If required checks cannot run due to infra-blocked reasons (billing/spend limit, runner outage/capacity, third-party premium/auth gating), stop iterating.
   - Create a single tracking item (preferred: sync explicit bot checklist items into `TODO.md`, otherwise create a GitHub issue) with a link to the failed run/check.
   - Move on to the next PR only after the infra blocker is recorded, or maintainers explicitly decide to accept the risk.
8. Timebox rule (to prevent endless bot-chasing):
   - Default limit: 2 full iterations of the bot loop or 60 minutes per PR (whichever comes first).
   - If still blocked after the timebox, post a short status summary (what’s fixed, what’s left, why it’s hard) and wait for maintainer direction to continue.
9. Only move on to the next PR when the current PR has no remaining todo/critical blockers (or maintainers explicitly decide to accept the risk, or the PR is infra-blocked and recorded per step 7).

**Review Feedback Backlog**
1. Aggregate bot review feedback using `gh api graphql`.
2. Preferred: run `intelligencex todo sync-bot-feedback` (or `dotnet run ... -- todo sync-bot-feedback`) to sync explicit checklist items into `TODO.md` and optionally create issues.
3. Track only explicit checklist items in `TODO.md`.
4. Group backlog by PR in `TODO.md` and keep it collapsed.
5. Avoid nested bullets in `TODO.md`.

**Documentation Hygiene**
- Keep TODO entries accurate: mark items done only when verified in code.
- When adding new items, include links to the originating comment or review.

**Testing**
- Run targeted `dotnet build` or tests if a change touches runtime behavior.
- Note any skipped tests and why.
