# First PR Rollout Acceptance Criteria

## Repo State
- `.github/workflows/review-intelligencex.yml` exists on default branch.
- Managed block markers are present (`INTELLIGENCEX:BEGIN` / `INTELLIGENCEX:END`).
- `.intelligencex/reviewer.json` exists when onboarding used `--with-config`.

## PR Checks
- `Static Analysis Gate`: `SUCCESS`.
- `AI Review (Fail-Open)`: `SUCCESS`.
- Main test job (for this repo usually `Ubuntu`): `SUCCESS`.

## Reviewer Output
- Sticky summary marker exists: `<!-- intelligencex:summary -->`.
- Summary includes `Reviewed commit:`.
- Summary/triage includes `Diff range:`.
- If analysis is enabled, comment includes:
  - `### Static Analysis Policy`
  - `### Static Analysis`
