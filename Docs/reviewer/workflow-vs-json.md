# Workflow YAML vs reviewer.json

This page explains where to configure IntelligenceX Reviewer, what wins when settings overlap, and how each change affects CI behavior.

## Quick Recommendation

Use both files, with clear ownership:

- `.github/workflows/review-intelligencex.yml`: pipeline wiring and per-run overrides
- `.intelligencex/reviewer.json`: review policy and team defaults

Rule of thumb:

- Put stable team policy in JSON.
- Put runner/transport wiring in YAML.
- Use workflow inputs only for temporary overrides.

## Configuration Sources and Precedence

Reviewer settings are applied in this order:

1. Built-in defaults
2. `.intelligencex/reviewer.json` (or `REVIEW_CONFIG_PATH` override)
3. Environment variables / workflow `INPUT_*` values

That means workflow inputs and env values override JSON values.
Outside GitHub Actions, direct `REVIEW_*` (and provider-specific) environment variables have the same override effect.

## What Belongs Where

### YAML (`.github/workflows/review-intelligencex.yml`)

Best for:

- Runner targeting (`runs_on`)
- Reviewer binary source (`reviewer_source: source|release`)
- Provider wiring and transport (`provider`, `model`, `openai_transport`)
- Secrets mode (`secrets: inherit` vs explicit mapping)
- Temporary run-time overrides from `workflow_dispatch` inputs

### JSON (`.intelligencex/reviewer.json`)

Best for:

- Mode/length/profile policy (`mode`, `length`, `profile`, `style`)
- Diff and filtering policy (`reviewDiffRange`, include/exclude/skip paths)
- Thread triage and auto-resolve behavior
- Usage guardrails and reliability tuning
- Analysis policy (`analysis.*`)

## Common Option Matrix

| Concern | Workflow YAML (`with:`) | reviewer.json | Typical impact |
| --- | --- | --- | --- |
| Provider/model wiring | `provider`, `model`, `openai_transport` | `review.provider`, `review.model`, `review.openaiTransport` | Runtime identity, latency/cost, provider behavior |
| Review shape | `mode`, `length`, `style` | `review.mode`, `review.length`, `review.style` | Comment visual structure and verbosity |
| Diff policy | optional override | `review.reviewDiffRange` | What context the model sees |
| Path filters | optional override | `review.includePaths`, `review.excludePaths`, `review.skipPaths` | Which files are reviewed |
| Thread triage | optional override | `review.reviewThreads*` | Auto-resolve and triage behavior |
| Progress/diagnostics | `progress_updates`, `diagnostics`, `preflight` | `review.progressUpdates`, `review.diagnostics`, `review.preflight` | Check logs and review pipeline transparency |
| Usage budget guard | `usage_budget_*` | `review.reviewUsageBudget*` | Early fail/allow behavior when budget is low |
| Analysis policy | `analysis_*` dispatch overrides | `analysis.*` | Findings and policy sections in output; gate behavior if analyze gate is used |
| Runner/source | `runs_on`, `reviewer_source` | not applicable | CI execution path and release-vs-source behavior |
| Secrets strategy | `secrets: inherit` / explicit | not applicable | Auth availability and trust boundaries |

## Build and Check Impact

### If you change workflow YAML

- You are changing CI behavior directly.
- PR checks can change trigger behavior, runtime, and execution path.
- Workflow-only PRs may be intentionally review-skipped unless `allowWorkflowChanges` is enabled.

### If you change reviewer.json

- Build/test pipelines do not change.
- Reviewer behavior changes (comment shape, strictness, filters, triage behavior).
- Existing check names usually stay the same, but review content can change significantly.

## Onboarding Outcomes (CLI and Web)

Both `intelligencex setup wizard` and `intelligencex setup web` follow the same path contract:

- `new-setup`: create/update workflow and config
- `refresh-auth`: update auth secret only
- `cleanup`: remove workflow/config (optional secret keep)
- `maintenance`: inspect and choose operation

Default onboarding creates both:

- `.github/workflows/review-intelligencex.yml`
- `.intelligencex/reviewer.json`

## Practical Patterns

### Pattern A: JSON-first (recommended)

- Keep YAML minimal and stable.
- Keep policy in JSON.
- Use workflow input overrides only for one-off debugging.

### Pattern B: YAML-first

- Put many behavior knobs in workflow `with:` block.
- Useful for central CI control.
- Higher drift risk if teams also edit JSON.

### Pattern C: Hybrid with explicit boundaries

- YAML: runner/source/secrets/transport only
- JSON: mode/length/policy/analysis/triage

This gives predictable behavior and easier maintenance.

## Validation Checklist

After config changes:

1. Confirm workflow is valid and managed block markers are intact.
2. Confirm `review / review` check runs on a small PR.
3. Confirm reviewer comment shows expected `Mode` and `Length`.
4. If analysis is enabled, verify policy/findings sections match expectation.

## Related Docs

- [Reviewer Overview](/docs/reviewer/overview/)
- [Reviewer Configuration](/docs/reviewer/configuration/)
- [Onboarding Wizard](/docs/reviewer/onboarding-wizard/)
- [Web Setup UI](/docs/reviewer/setup-web/)
- [Web Onboarding Flow](/docs/reviewer/web-onboarding/)
