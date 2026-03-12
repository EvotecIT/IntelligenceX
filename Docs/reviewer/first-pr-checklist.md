---
title: First PR Checklist
description: Validate the first post-onboarding pull request so IntelligenceX review, analysis, and required checks run end to end.
---

# First PR Checklist

Use this checklist right after merging your onboarding/setup PR.

## Goal

Confirm the next PR is reviewed end-to-end (analysis + reviewer) without manual workflow edits.

## Quick Checklist

1. Open a small follow-up PR (for example docs-only change).
2. Verify required checks started:
   - `review / review` (IntelligenceX Review)
   - `Ubuntu` (test workflow)
   - plus any repo-specific required checks (for example `website-build`)
3. Confirm the IntelligenceX review comment includes:
   - `Reviewed commit: <sha>`
   - A diff range line (for example `PR base → head`)
   - Static analysis status block (`pass` or `unavailable`)
4. If static analysis is enabled, verify the review shows policy + findings summary.
5. If inline mode is enabled, confirm inline comments appear when findings/review points exist.

## Expected Variations

- Dependabot PRs can show comments from `github-actions` instead of your app bot. This is expected because secrets are usually not exposed to Dependabot workflows.
- If no actionable findings exist, review output may include only summary sections.

## Common Issues and Fixes

1. Checks did not start:
   - Ensure `.github/workflows/review-intelligencex.yml` exists on the PR branch.
   - Ensure PR is not draft (or transition it to ready-for-review).
2. Reviewer reports auth/secrets issue:
   - Confirm `INTELLIGENCEX_AUTH_B64` (and optional `INTELLIGENCEX_AUTH_KEY`) exist in repo/org secrets.
   - Re-run setup secret update if needed: `intelligencex setup wizard` (operation: update secret).
3. Static analysis shows `unavailable`:
   - This usually means no analysis result artifacts matched configured inputs.
   - Validate config and catalog locally:
     - `dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- analyze validate-catalog --workspace .`
     - `dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- analyze run --config .intelligencex/reviewer.json --out artifacts --framework net8.0`
4. Bot cannot auto-resolve threads:
   - If logs show integration permission errors, keep review functional and treat thread auto-resolve as non-blocking until token permissions are updated.

## Debug Commands

```bash
# PR checks summary
gh pr checks <pr-number>

# Run details / logs
gh run list --limit 10
gh run view <run-id> --log
```

## Related Docs

- [Onboarding Wizard](../onboarding-wizard/)
- [Static Analysis](../static-analysis/)
- [Reviewer Configuration](../configuration/)
