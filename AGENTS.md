# IntelligenceX Agent Playbook

This file defines how automated agents should operate in this repo. Follow it for all changes and PR management.

**Core Rules**
- Use `gh` CLI for PR, review, and CI workflows.
- Work in a dedicated worktree + branch per task.
- Do not touch the Website PR or website work unless explicitly requested.
- Do not use destructive git commands (`reset --hard`, `checkout --`).
- Avoid CI deadlocks: for private repos, ensure PR workflows can run on GitHub-hosted runners unless
  the repo explicitly opts into self-hosted via repo `vars` (see workflow comments).

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
6. Do not chase provider/auth diagnostics unless they cause a required check to fail (the reviewer workflow is designed to fail-open for many provider issues).
7. Rebase onto `origin/master` before merge if needed.
8. Merge only when all required checks pass and the PR is mergeable.
9. Merge with squash + delete branch: `gh pr merge <num> --repo EvotecIT/IntelligenceX --squash --delete-branch`.

**Dependabot + Workflow-Only PRs**
- On Dependabot PRs, comments may be authored by `github-actions` rather than the IntelligenceX GitHub App. This is expected: GitHub typically does not expose repo secrets (including app private keys) to Dependabot PR workflows.
- The IntelligenceX reviewer may intentionally skip PRs that only modify workflow files to avoid self-modifying workflow runs. If checks are green and the diff is limited to pin bumps (e.g., `uses:` SHA updates), treat this as mergeable unless maintainers explicitly require a manual review.
- Never touch `/.github/workflows/deploy-website.yml` unless explicitly requested (even when updating pinned action SHAs elsewhere).

**Local Preflight (Before You Push)**
To avoid bot/CI churn, run local checks before pushing when you touched runtime behavior or workflows.
1. Build: `dotnet build IntelligenceX.sln -c Release`
2. Tests: `dotnet test IntelligenceX.sln -c Release`
3. Harness (matches CI): `dotnet ./IntelligenceX.Tests/bin/Release/net8.0/IntelligenceX.Tests.dll`
4. Harness (matches CI): `dotnet ./IntelligenceX.Tests/bin/Release/net10.0/IntelligenceX.Tests.dll`
5. Reviewer static analysis (optional but useful when reviewer logic/templates change):
   - `dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- analyze validate-catalog --workspace .`
   - `dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- analyze run --config .intelligencex/reviewer.json --out artifacts --framework net8.0`

**PR Handling Loop (Required)**
When an agent is assigned a PR to improve or unblock, it must iterate until merge blockers are clean.

1. Read the latest IntelligenceX bot review comment and snapshot it (copy the raw Markdown to a scratchpad) so you can diff "before vs after" and avoid re-fixing the same item.
2. Treat these sections as merge blockers: `Todo List ✅` and `Critical Issues ⚠️`.
3. Treat `Other Issues 🧯` and `Next Steps 🚀` as non-blocking unless maintainers explicitly escalate them.
4. Triage other automated reviews (for example the “Claude Code Review” sticky comment) and fix anything that impacts correctness, security, or reliability.
5. Fix inline comments only when they map to merge blockers or correctness/security/reliability issues; ignore style-only nits from other bots unless maintainers explicitly escalate them.
6. Apply fixes in a single coherent batch (avoid micro-commits that "poke" the bot repeatedly), then re-run checks and re-check bot output:
   Run: `gh pr checks <num> --repo EvotecIT/IntelligenceX`
   If the bot posts new todo/critical items, repeat.
7. Infra-blocked escape hatch:
   - If required checks cannot run due to infra-blocked reasons (billing/spend limit, runner outage/capacity, third-party premium/auth gating), stop iterating.
   - Create a single tracking item (preferred: sync explicit bot checklist items into `TODO.md`, otherwise create a GitHub issue) with a link to the failed run/check.
   - Move on to the next PR only after the infra blocker is recorded, or maintainers explicitly decide to accept the risk.
8. Bot churn guard (to prevent endless "new items" that are just reworded or non-deterministic):
   - Only treat newly-introduced todo/critical items as blockers if they are clearly tied to the diff and are actionable/verifiable.
   - If the bot rewords the same issue across iterations, treat it as the same item and do not reopen it unless the underlying code regressed.
   - If an item is vague, style-only, or not reproducible after you applied a correct fix, record it in `TODO.md` (unchecked) with a link to the bot comment and stop chasing it in the PR unless maintainers explicitly escalate it.
9. Timebox rule (to prevent endless bot-chasing):
   - Default limit: 2 full iterations of the bot loop or 60 minutes per PR (whichever comes first).
   - If still blocked after the timebox, post a short status summary (what’s fixed, what’s left, why it’s hard) and wait for maintainer direction to continue.
10. Only move on to the next PR when the current PR has no remaining todo/critical blockers, or one of these is true:
   - maintainers explicitly decide to accept the risk
   - the PR is infra-blocked and recorded per step 7
   - the only remaining bot todo/critical items are classified as churn per step 8, recorded in `TODO.md`, and surfaced to maintainers in a status summary comment

**Commenting Hygiene (Avoid Shell Foot-Guns)**
- When posting PR comments from a shell, prefer `--body-file -` with a single-quoted heredoc to avoid accidental command substitution (for example backticks interpreted by zsh):
  - `cat <<'EOF' | gh pr comment <num> --repo EvotecIT/IntelligenceX --body-file -`
  - `...comment body here...`
  - `EOF`

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
