---
name: intelligencex-onboarding-setup
description: Use when work touches setup wizard, setup web, onboarding UX, onboarding docs, or first-run validation flows.
---

# Skill: intelligencex-onboarding-setup

Use this skill when work touches onboarding/setup behavior in:
- `IntelligenceX.Cli/Setup/**`
- `IntelligenceX.Cli/Program.Help.cs`
- onboarding docs (`Docs/reviewer/onboarding-wizard.md`, `Docs/reviewer/setup-web.md`, `Docs/getting-started.md`, `Docs/reviewer/workflow-vs-json.md`)

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
  - Bash: `.agents/skills/intelligencex-onboarding-setup/scripts/preflight.sh`
  - PowerShell: `pwsh -NoLogo -NoProfile -File .agents/skills/intelligencex-onboarding-setup/scripts/preflight.ps1`
- Fast validation:
  - Bash: `.agents/skills/intelligencex-onboarding-setup/scripts/local-validate.sh fast`
  - PowerShell: `pwsh -NoLogo -NoProfile -File .agents/skills/intelligencex-onboarding-setup/scripts/local-validate.ps1 -Mode fast`
- Full validation:
  - Bash: `.agents/skills/intelligencex-onboarding-setup/scripts/local-validate.sh full`
  - PowerShell: `pwsh -NoLogo -NoProfile -File .agents/skills/intelligencex-onboarding-setup/scripts/local-validate.ps1 -Mode full`

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
- `../intelligencex-reviewer-bootstrap/references/config-precedence.md`
