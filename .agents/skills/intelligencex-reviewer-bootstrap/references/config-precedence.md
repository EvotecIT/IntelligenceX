# Reviewer Config Precedence

Use this when validating reviewer behavior drift.

## Effective Order

1. Built-in reviewer defaults
2. `.intelligencex/reviewer.json` (or `REVIEW_CONFIG_PATH`)
3. Environment values (`INPUT_*`, `REVIEW_*`, provider-specific env vars)

In GitHub Actions, reusable workflow `with:` values are mapped to `INPUT_*`, so they override JSON.

## Ownership Split

- Workflow YAML:
  - runner labels
  - reviewer source (`source` vs `release`)
  - provider transport wiring
  - secrets strategy
  - temporary run overrides
- `reviewer.json`:
  - review policy (`mode`, `length`, `profile`, `style`)
  - diff/filter/triage policy
  - analysis policy

## Behavior Impact

- Workflow changes affect CI behavior and check execution.
- JSON changes mostly affect review output and triage behavior.

## Validation Prompts

- "Do we expect a check behavior change or only a comment behavior change?"
- "Is override coming from workflow inputs instead of JSON?"
- "Is `review / review` running with expected mode/length in footer?"
