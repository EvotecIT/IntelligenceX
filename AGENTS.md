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

**Review Feedback Backlog**
1. Aggregate bot review feedback using `gh api graphql`.
1. Preferred: run `python3 scripts/sync_bot_feedback_todo.py` to sync explicit checklist items from open PR bot reviews/comments into `TODO.md`.
2. Track only explicit checklist items in `TODO.md`.
3. Group backlog by PR in `TODO.md` and keep it collapsed.
4. Avoid nested bullets in `TODO.md`.

**Documentation Hygiene**
- Keep TODO entries accurate: mark items done only when verified in code.
- When adding new items, include links to the originating comment or review.

**Testing**
- Run targeted `dotnet build` or tests if a change touches runtime behavior.
- Note any skipped tests and why.
