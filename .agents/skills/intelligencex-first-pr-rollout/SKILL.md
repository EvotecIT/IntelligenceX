---
name: intelligencex-first-pr-rollout
description: Use to validate the first live reviewer run after onboarding, including checks, sticky summary markers, and rollout health signals.
---

# Skill: intelligencex-first-pr-rollout

Use this skill to verify the first live reviewer run after onboarding/setup is healthy and complete.

## Trigger Phrases
- "first PR rollout"
- "verify onboarding worked"
- "post-setup verification"
- "check reviewer is live"
- "sticky summary verification"

## Strict Execution Order
1. Capture rollout snapshot for target repo + PR
2. Verify workflow/config presence on default branch
3. Verify PR checks reached expected conclusions
4. Verify reviewer sticky summary includes reviewed SHA + diff-range label
5. Verify static analysis status blocks are present when expected
6. Publish a concise pass/fail report with exact artifacts paths

## Commands
- Run verification:
  - Bash: `.agents/skills/intelligencex-first-pr-rollout/scripts/verify-first-pr-rollout.sh --repo <owner/name> --pr <number> --require-config --require-analysis-sections`
  - PowerShell: `pwsh -NoLogo -NoProfile -File .agents/skills/intelligencex-first-pr-rollout/scripts/verify-first-pr-rollout.ps1 -Repo <owner/name> -Pr <number> -RequireConfig -RequireAnalysisSections`

## Fail-Fast Rules
- Stop if GitHub auth is missing.
- Stop if reviewer workflow is missing on default branch.
- Stop if required checks are missing/failed.
- Stop if sticky summary marker or reviewed SHA/diff-range labels are missing.

## References
- `references/acceptance-criteria.md`
- `references/report-template.md`
