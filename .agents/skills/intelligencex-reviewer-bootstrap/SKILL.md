---
name: intelligencex-reviewer-bootstrap
description: Use when bootstrapping or validating IntelligenceX reviewer setup, managed workflow blocks, and reviewer.json generation/apply flow.
---

# Skill: intelligencex-reviewer-bootstrap

Use this skill when setting up or validating IntelligenceX Reviewer onboarding in a target repo, especially:
- `.github/workflows/review-intelligencex.yml`
- `.intelligencex/reviewer.json`
- setup flows (`setup`, `setup wizard`, `setup web`)

## Trigger Phrases
- "reviewer setup"
- "bootstrap reviewer"
- "workflow yaml"
- "reviewer.json"
- "setup dry-run"
- "onboarding config"

## Strict Execution Order
1. Confirm repo/target mode and auth source
2. Run setup dry-run profile
3. Extract generated `reviewer.json` and workflow YAML from dry-run output
4. Validate managed workflow block + core review/analysis keys
5. Share concise apply plan (what will change)
6. Apply only after explicit approval
7. Run post-apply validation

## Core Commands
- Dry-run + extraction + checks:
  - Bash: `.agents/skills/intelligencex-reviewer-bootstrap/scripts/bootstrap-dry-run.sh --repo <owner/name> --mode setup`
  - PowerShell: `pwsh -NoLogo -NoProfile -File .agents/skills/intelligencex-reviewer-bootstrap/scripts/bootstrap-dry-run.ps1 -Repo <owner/name> -Mode setup`
- Validate existing committed workflow in current repo:
  - Bash: `.agents/skills/intelligencex-reviewer-bootstrap/scripts/verify-managed-workflow.sh .github/workflows/review-intelligencex.yml`
  - PowerShell: `pwsh -NoLogo -NoProfile -File .agents/skills/intelligencex-reviewer-bootstrap/scripts/verify-managed-workflow.ps1 .github/workflows/review-intelligencex.yml`

## Modes
- `setup`: write/update workflow + reviewer config
- `update-secret`: secret refresh flow only
- `cleanup`: remove workflow/config (optionally keep secret)

## Fail-Fast Rules
- Stop if GitHub auth token is unavailable.
- Stop if generated workflow lacks `INTELLIGENCEX:BEGIN/END` markers.
- Stop if setup mode output lacks `.intelligencex/reviewer.json`.
- Stop if analysis was requested but `analysis` block is missing.

## References
- `references/setup-command-matrix.md`
- `references/workflow-managed-block.md`
