# Vision

Repository: `{{Repo}}`
Project owner: `{{Owner}}`
Project number: `{{ProjectNumber}}`

## Mission

- Keep the backlog converging toward a small set of mergeable, high-impact changes.
- Reduce duplicate implementation work across PRs and issues.
- Prioritize maintainability, reliability, and clear ownership.

## In Scope

- Duplicate detection and consolidation for overlapping PRs/issues.
- Signals that improve merge readiness (tests, review state, CI health, churn).
- Tooling that helps maintainers decide faster without auto-merging.
- Changes that improve onboarding and operational safety for reviewers.

## Out Of Scope

- Automatic merge/close decisions without maintainer confirmation.
- Large redesigns unrelated to review throughput or triage quality.
- Experiments that bypass required checks or repository protections.
- Feature work that does not improve triage, review quality, or delivery speed.

## Maintainer Guidance

- `aligned`: clear in-scope signals and no conflicting out-of-scope evidence.
- `needs-human-review`: mixed or weak signals; ask for clarification.
- `likely-out-of-scope`: strong out-of-scope signals with limited in-scope evidence.

## Decision Notes

- Keep this document short and explicit.
- Prefer concrete bullets over abstract principles.
- Update this file when roadmap direction changes.
