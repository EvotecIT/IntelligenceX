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
5. Rebase onto `origin/master` before merge if needed.
6. Merge only when all required checks pass and the PR is mergeable.
7. Merge with squash + delete branch: `gh pr merge <num> --repo EvotecIT/IntelligenceX --squash --delete-branch`.

**PR Handling Loop (Required)**
When an agent is assigned a PR to improve or unblock, it must iterate until merge blockers are clean.

1. Read the latest IntelligenceX bot review comment.
2. Treat these sections as merge blockers: `Todo List ✅` and `Critical Issues ⚠️`.
3. Treat `Other Issues 🧯` and `Next Steps 🚀` as non-blocking unless maintainers explicitly escalate them.
4. Fix inline comments only when they map to merge blockers (todo/critical).
5. Apply fixes, then re-run checks and re-check bot output:
   Run: `gh pr checks <num> --repo EvotecIT/IntelligenceX`
   If the bot posts new todo/critical items, repeat.
6. Only move on to the next PR when the current PR has no remaining todo/critical blockers (or maintainers explicitly decide to accept the risk).

**Review Feedback Backlog**
1. Aggregate bot review feedback using `gh api graphql`.
2. Preferred: run `intelligencex todo sync-bot-feedback` (or `dotnet run ... -- todo sync-bot-feedback`) to sync explicit checklist items into `TODO.md` and optionally create issues.
3. Legacy fallback: `python3 scripts/sync_bot_feedback_todo.py` (deprecated; planned removal).
4. Track only explicit checklist items in `TODO.md`.
5. Group backlog by PR in `TODO.md` and keep it collapsed.
6. Avoid nested bullets in `TODO.md`.

**Documentation Hygiene**
- Keep TODO entries accurate: mark items done only when verified in code.
- When adding new items, include links to the originating comment or review.

**Testing**
- Run targeted `dotnet build` or tests if a change touches runtime behavior.
- Note any skipped tests and why.
