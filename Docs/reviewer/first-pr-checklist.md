# First PR Checklist

Use this checklist right after merging your onboarding/setup PR.

## Goal

Confirm the next PR is reviewed end-to-end (analysis + reviewer) without manual workflow edits.

## Quick Checklist

1. Open a small follow-up PR (for example docs-only change).
2. Verify required checks started:
   - `Static Analysis Gate`
   - `AI Review (Fail-Open)`
   - `Ubuntu` (test workflow)
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
   - Confirm analysis artifacts are produced before reviewer step.
   - Validate config and catalog locally:
     - `dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- analyze validate-catalog --workspace .`
     - `dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- analyze run --config .intelligencex/reviewer.json --out artifacts --framework net8.0`
4. Bot cannot auto-resolve threads:
   - If logs show integration permission errors, keep review functional and treat thread auto-resolve as non-blocking until token permissions are updated.

## Debug Commands

```bash
# PR checks summary
gh pr checks <pr-number> --repo EvotecIT/IntelligenceX

# Run details / logs
gh run list --repo EvotecIT/IntelligenceX --limit 10
gh run view <run-id> --repo EvotecIT/IntelligenceX --log
```

## Related Docs

- [Onboarding Wizard](/docs/reviewer/onboarding-wizard/)
- [Static Analysis](/docs/reviewer/static-analysis/)
- [Reviewer Configuration](/docs/reviewer/configuration/)
