# PR Backlog Sweep (Close Bot Feedback Gaps)

Goal: reduce PR churn by systematically addressing merge blockers reported by the IntelligenceX review bot across the most recently updated open PRs.

## Scope

- Target: last 20-30 open PRs (sorted by `updatedAt`).
- Fix: `Todo List ✅` and `Critical Issues ⚠️` from the IntelligenceX bot review comment.
- Do not do: website work unless explicitly requested.

## Workflow

1. Sync the backlog list into `TODO.md`.
   - `intelligencex todo sync-bot-feedback --repo EvotecIT/IntelligenceX`
2. Get the current open PRs (limit 30).
   - `gh pr list --repo EvotecIT/IntelligenceX --state open --limit 30`
3. For each PR, loop until clean.
   - Open PR details: `gh pr view <num> --repo EvotecIT/IntelligenceX`
   - Check CI: `gh pr checks <num> --repo EvotecIT/IntelligenceX`
   - Address merge blockers, push updates, re-check bot output, repeat.
4. When a PR is clean (no todo/critical blockers), move to the next PR.

## Agent Prompt (Copy/Paste)

Use this when delegating the sweep to another agent.

```text
You are an automated agent working in EvotecIT/IntelligenceX. Follow AGENTS.md strictly.

Task: Sweep the most recently updated open PRs (limit 30) and close merge blockers from our IntelligenceX bot reviews.

Rules:
- Use gh CLI for PR operations.
- Use a dedicated git worktree + branch per PR you touch: git worktree add -b <branch> <path> origin/master
- Do not use destructive git commands.
- Do not do website work unless explicitly asked.
- Merge blockers are only items in "Todo List ✅" and "Critical Issues ⚠️" sections from the IntelligenceX bot.
- Other Issues and style-only bot nits are non-blocking unless maintainers explicitly say otherwise.
- Iterate on one PR until it is clean (no todo/critical blockers), then move to the next PR.
- Keep changes focused to unblocking the PR.

Process:
1) Run: intelligencex todo sync-bot-feedback --repo EvotecIT/IntelligenceX
2) Run: gh pr list --repo EvotecIT/IntelligenceX --state open --limit 30
3) For each PR number in that list:
   - gh pr view <num> --repo EvotecIT/IntelligenceX
   - Identify merge blockers from our bot review comment.
   - Create a worktree + branch, implement fixes, run dotnet build/test if runtime behavior changed.
   - Push the branch and post a short PR comment describing what was fixed and what remains.
   - Re-check: gh pr checks <num> --repo EvotecIT/IntelligenceX
   - If new bot blockers appear, continue the loop.
Stop after the top 30 PRs (or earlier if the backlog is clean).
```

