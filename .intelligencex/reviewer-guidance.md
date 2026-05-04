# IntelligenceX Reviewer Guidance

Use this file as repo-owned review context for IntelligenceX itself. Keep merge blockers focused on correctness, security, reliability, test regressions, and documented branch-protection risks.

## Review Posture

- Treat `Todo List` and `Critical Issues` as merge-blocking sections. Do not put style-only or speculative items there.
- Treat Dependabot PRs as skippable unless they carry the `needs-ai-review` label or touch non-routine code, scripts, workflows, or reviewer behavior.
- For auto-approval, require explicit opt-in labels, green effective checks, no pending checks, no active review threads, and no review merge blockers.
- Fail closed when approval evidence is unavailable. Missing check data, missing thread data, or filtered-away check evidence should block approval readiness.

## Repository Boundaries

- Do not hardcode behavior for sibling EvotecIT repositories in reviewer code. Prefer repo-owned guidance files, structured configuration, and reusable conventions supplied by the target repo.
- Keep public docs in `Docs/` only when they should appear on the website. Internal trackers and maintainer playbooks belong under `InternalDocs/`.
- Do not touch website workflows or website content unless the task explicitly asks for it.

## Validation Expectations

- For reviewer behavior changes, run the focused build and harness before pushing.
- When analysis behavior changes, also run the repo-local analysis-gate fast suite.
- Preserve language-neutral routing and review logic. Prefer structured fields over natural-language keyword checks.
