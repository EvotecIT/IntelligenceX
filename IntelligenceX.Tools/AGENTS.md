# IntelligenceX.Tools Agent Playbook

This file defines how automated agents should operate in this repo.

Core rules:
- Use `gh` CLI for PR, review, and CI workflows.
- Work in a dedicated worktree + branch per task.
- Do not use destructive git commands (`reset --hard`, `checkout --`).

Worktree + branch:
1. Create a worktree for every task: `git worktree add -b <branch> <path> origin/main`
2. Worktree location matters in this repo because projects reference the sibling `../IntelligenceX` checkout.
   - Recommended: create the worktree as a direct child of the same parent folder that contains both `IntelligenceX` and `IntelligenceX.Tools`.
   - Example (from `IntelligenceX.Tools` repo root): `git worktree add -b <branch> ../.wt-ix-tools-<branch> origin/main`
2. Make changes only in that worktree.
3. Keep branches focused to a single change set.

PR review and CI:
1. List PRs: `gh pr list --repo EvotecIT/IntelligenceX.Tools --state open`
2. Inspect details: `gh pr view <num> --repo EvotecIT/IntelligenceX.Tools`
3. Check CI: `gh pr checks <num> --repo EvotecIT/IntelligenceX.Tools`
4. If CI fails, inspect logs: `gh run view <run-id> --repo EvotecIT/IntelligenceX.Tools --log --job <job-id>`
5. Rebase onto `origin/main` before merge if needed.
6. Merge only when all required checks pass and the PR is mergeable.
7. Merge with squash + delete branch: `gh pr merge <num> --repo EvotecIT/IntelligenceX.Tools --squash --delete-branch`

Quality bar:
- Nullable enabled.
- Treat warnings as errors.
- Prefer AOT-safe patterns where feasible.
- Keep packs provider-agnostic and dependency-isolated (see `CONTRIBUTING.md`).

Duplication and layering:
- Prefer improving upstream engine libraries over duplicating logic in tool packs. Typical homes: AD logic in `ADPlayground`, system/registry logic in `ComputerX`, event log parsing/reporting in `EventViewerX`.
- `IntelligenceX.Tools.*` projects should stay a thin tool layer: define tool schema and JSON output shape, call engine/library code to do the real work, and enforce caps/timeouts/paging/redaction at the tool boundary.
- If you find yourself adding repeated helpers (DN parsing, typed reads from `SearchResult`, JSON array builders, argument-capping utilities), first search in the relevant engine library. If it’s generic, put it in a shared helper (prefer `IntelligenceX.Tools.Common` when it exists). Avoid per-tool copies of the same helper methods.
