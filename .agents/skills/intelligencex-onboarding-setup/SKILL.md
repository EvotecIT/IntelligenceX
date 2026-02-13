---
name: intelligencex-onboarding-setup
description: Use when work touches onboarding and setup UX/flows in IntelligenceX CLI setup commands, setup web, onboarding docs, and first-run experience validation.
metadata:
  short-description: Onboarding setup workflow
---

# Skill: intelligencex-onboarding-setup

Use this skill when work touches onboarding/setup behavior in:
- `IntelligenceX.Cli/Setup/**`
- `IntelligenceX.Cli/Program.Help.cs`
- onboarding docs (`Docs/reviewer/onboarding-wizard.md`, `Docs/reviewer/setup-web.md`, `Docs/getting-started.md`)

## Trigger Phrases
- "onboarding"
- "setup wizard"
- "setup web"
- "first-run experience"
- "quick start path"

## Strict Execution Order
1. Scope and impact map
2. Preflight checks
3. Implement code changes
4. Run onboarding validation suite
5. Confirm docs/help text alignment
6. Prepare PR summary with exact commands run

## Commands
- Preflight:
  - `.agents/skills/intelligencex-onboarding-setup/scripts/preflight.sh`
- Fast validation:
  - `.agents/skills/intelligencex-onboarding-setup/scripts/local-validate.sh fast`
- Full validation:
  - `.agents/skills/intelligencex-onboarding-setup/scripts/local-validate.sh full`

## Fail-Fast Rules
- Stop if preflight fails (dirty tree, wrong branch, missing tools).
- Stop if `dotnet build` fails.
- Stop if onboarding-related tests fail.
- Do not proceed to PR merge if required checks are red.

## Worktree Hygiene
- Branch must start with `codex/`.
- Keep only task-related file changes.
- No destructive git commands.

## References
- `references/checklist.md`
