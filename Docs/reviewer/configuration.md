---
title: Reviewer Configuration
description: Configure IntelligenceX Reviewer with reviewer.json or workflow inputs, including precedence, schema validation, providers, and policy settings.
---

# Reviewer Configuration

You can configure the reviewer with env vars **or** a repo-local file at `.intelligencex/reviewer.json`.
The JSON file is the cleanest way to keep settings versioned with your repo.

Schema: `../../Schemas/reviewer.schema.json`

The reviewer validates `.intelligencex/reviewer.json` against the schema at runtime.
Unknown properties emit warnings; invalid types or enum values fail the run.

## Source precedence (important)

Settings are applied in this order:

1. Reviewer defaults
2. `.intelligencex/reviewer.json` (or `REVIEW_CONFIG_PATH`)
3. Environment / workflow inputs (`INPUT_*`, `REVIEW_*`)

Because workflow inputs are mapped to environment variables, they override JSON.

If you want a full YAML-vs-JSON split guide, see [Workflow vs JSON](/docs/reviewer/workflow-vs-json/).

## Multiple ChatGPT accounts (rotation + failover)

If your auth store contains more than one ChatGPT login, you can pin a primary account and optionally rotate/fail over to additional accounts.
List available account ids with:

```powershell
intelligencex auth list
```

```json
{
  "review": {
    "openaiAccountId": "acc-primary",
    "openaiAccountIds": ["acc-primary", "acc-backup", "acc-team"],
    "openaiAccountRotation": "round-robin",
    "openaiAccountFailover": true
  }
}
```

`openaiAccountRotation` supports:
- `first-available`: preserve configured order
- `round-robin`: rotate by workflow run seed (`GITHUB_RUN_NUMBER`, then `GITHUB_RUN_ID`, then `GITHUB_RUN_ATTEMPT`)
- `sticky`: prefer `openaiAccountId` first, then preserve remaining order

GitHub Actions input/env aliases:
- `openai_account_id` / `REVIEW_OPENAI_ACCOUNT_ID` / `INTELLIGENCEX_OPENAI_ACCOUNT_ID`
- `openai_account_ids` / `REVIEW_OPENAI_ACCOUNT_IDS` / `INTELLIGENCEX_OPENAI_ACCOUNT_IDS`
- `openai_account_rotation` / `REVIEW_OPENAI_ACCOUNT_ROTATION`
- `openai_account_failover` / `REVIEW_OPENAI_ACCOUNT_FAILOVER`

## Minimal example

```json
{
  "review": {
    "provider": "openai",
    "model": "gpt-5.5",
    "mode": "inline",
    "length": "long",
    "outputStyle": "compact",
    "reviewUsageSummary": false
  }
}
```

## Claude provider (Anthropic Messages API)

Use `provider: claude` when you want the reviewer to call Anthropic directly via the Messages API.
This provider uses `POST /v1/messages` and requires:
- `review.model`
- an API key (via `review.anthropic.apiKeyEnv`, `ANTHROPIC_API_KEY`, or `review.anthropic.apiKey`)

`review.anthropic.baseUrl` defaults to `https://api.anthropic.com`, and `review.anthropic.version` defaults to `2023-06-01`.

```json
{
  "review": {
    "provider": "claude",
    "model": "claude-opus-4-1",
    "reviewUsageSummary": true,
    "anthropic": {
      "apiKeyEnv": "ANTHROPIC_API_KEY",
      "timeoutSeconds": 60,
      "maxTokens": 4096
    }
  }
}
```

## OpenAI-compatible provider (Ollama/OpenRouter/etc.)

Use `provider: openai-compatible` when you want to talk to an OpenAI-style HTTP endpoint (for example Ollama or OpenRouter).
This provider uses `POST /v1/chat/completions` and requires:
- `review.model`
- `review.openaiCompatible.baseUrl`
- an API key (via `review.openaiCompatible.apiKeyEnv`, `OPENAI_COMPATIBLE_API_KEY`, or `review.openaiCompatible.apiKey`)

Security note: `baseUrl` must use `https://` for non-loopback hosts by default. Plain `http://` is allowed only for loopback
(for example `http://localhost:11434`). To allow non-loopback `http://`, set `review.openaiCompatible.allowInsecureHttp: true` and `review.openaiCompatible.allowInsecureHttpNonLoopback: true` (dangerous).

Compatibility note: by default, Authorization is kept on same-host redirects. To force-drop it when a redirect changes scheme/host/port, set `review.openaiCompatible.dropAuthorizationOnRedirect: true` (or `OPENAI_COMPATIBLE_DROP_AUTHORIZATION_ON_REDIRECT=1`).

```json
{
  "review": {
    "provider": "openai-compatible",
    "model": "gpt-4o-mini",
    "openaiCompatible": {
      "baseUrl": "http://localhost:11434",
      "apiKeyEnv": "OPENAI_COMPATIBLE_API_KEY",
      "allowInsecureHttp": false,
      "dropAuthorizationOnRedirect": false,
      "timeoutSeconds": 60
    }
  }
}
```

## Strict (security-first) example

```json
{
  "review": {
    "mode": "inline",
    "length": "long",
    "strictness": "high",
    "focus": ["security", "correctness"],
    "reviewDiffRange": "current",
    "includeReviewThreads": true,
    "reviewThreadsIncludeBots": false,
    "maxInlineComments": 20,
    "reviewUsageSummary": true
  }
}
```

## Merge-blocker loop policy (section contract)

Use this when your repo wants different merge-blocker sections than the default `Todo List` + `Critical Issues`
contract, or when you want to relax the section-presence gate used by no-blockers thread sweep.

```json
{
  "review": {
    "outputStyle": "compact",
    "mergeBlockerSections": ["Todo List"],
    "mergeBlockerRequireAllSections": true,
    "mergeBlockerRequireSectionMatch": true
  }
}
```

- `mergeBlockerSections`: headings treated as merge-blocking sections (case-insensitive contains match).
- `mergeBlockerRequireAllSections`: `true` (default) requires every configured section to be present; `false` allows any matching configured section.
- `mergeBlockerRequireSectionMatch`: `true` (default) treats no-match output as blocked; `false` allows no-match output to be non-blocking.

Setup shortcuts:
- Vision-aligned baseline: `intelligencex setup --with-config --review-loop-policy vision`
- Vision file override: `intelligencex setup --with-config --review-loop-policy vision --review-vision-path VISION.md`
- Custom strictness + contract: `intelligencex setup --with-config --review-intent maintainability --review-strictness strict --merge-blocker-sections "Todo List,Critical Issues" --merge-blocker-require-all-sections false`

## Fast (cost-aware) example

```json
{
  "review": {
    "mode": "summary",
    "length": "short",
    "maxFiles": 30,
    "maxPatchChars": 4000,
    "progressUpdates": false,
    "reviewUsageSummary": true
  }
}
```

Note: set `maxFiles` to `0` to disable file count limits; the reviewer will still trim individual patches using `maxPatchChars`.

## Provider fallback example

Use `providerFallback` to opt into a secondary provider when the primary provider fails.

```json
{
  "review": {
    "provider": "openai",
    "providerFallback": "copilot"
  }
}
```

## CI-aware reviewer context (optional)

Use this when you want the reviewer to be aware of GitHub check state and failing workflow runs for the current PR SHA.

All of these settings are optional and default to off unless you explicitly enable them.

```json
{
  "review": {
    "ciContext": {
      "enabled": true,
      "includeCheckSummary": true,
      "includeFailedRuns": true,
      "includeFailureSnippets": "off",
      "maxFailedRuns": 3,
      "maxSnippetCharsPerRun": 4000,
      "classifyInfraFailures": true
    }
  }
}
```

Recommended rollout:
- start with `includeFailureSnippets: "off"`
- enable snippets only after you confirm summary-level CI awareness is useful for your repo

Current implementation note:
- the reviewer currently injects check summaries and failed-run metadata when enabled
- detailed failure-snippet capture is reserved for a later rollout slice

GitHub Actions input/env aliases:
- `ci_context_enabled` / `REVIEW_CI_CONTEXT_ENABLED`
- `ci_context_include_check_summary` / `REVIEW_CI_CONTEXT_INCLUDE_CHECK_SUMMARY`
- `ci_context_include_failed_runs` / `REVIEW_CI_CONTEXT_INCLUDE_FAILED_RUNS`
- `ci_context_include_failure_snippets` / `REVIEW_CI_CONTEXT_INCLUDE_FAILURE_SNIPPETS`
- `ci_context_max_failed_runs` / `REVIEW_CI_CONTEXT_MAX_FAILED_RUNS`
- `ci_context_max_snippet_chars_per_run` / `REVIEW_CI_CONTEXT_MAX_SNIPPET_CHARS_PER_RUN`
- `ci_context_classify_infra_failures` / `REVIEW_CI_CONTEXT_CLASSIFY_INFRA_FAILURES`

## Agent profiles (provider + authenticator + model)

Use `agentProfiles` when you want named review backends that bundle provider, model, and auth/runtime settings.
This keeps the IX prompt and output contract unchanged while letting you switch the backend that answers it.

For backward compatibility, the loader also accepts `modelProfiles` and `authProfiles` as legacy aliases for
`agentProfiles`, but new configs should prefer `agentProfiles`.

```json
{
  "review": {
    "agentProfile": "copilot-claude",
    "agentProfiles": {
      "chatgpt-gpt55": {
        "provider": "openai",
        "authenticator": "chatgpt",
        "openaiTransport": "native",
        "model": "gpt-5.5",
        "openaiAccountId": "acct-review"
      },
      "copilot-gpt54": {
        "provider": "copilot",
        "authenticator": "copilot-cli",
        "model": "gpt-5.4",
        "copilot": {
          "launcher": "auto",
          "autoInstall": true,
          "envAllowlist": ["COPILOT_GITHUB_TOKEN"]
        }
      },
      "copilot-claude": {
        "provider": "copilot",
        "authenticator": "copilot-cli",
        "model": "claude-sonnet-4-5",
        "copilot": {
          "envAllowlist": ["COPILOT_GITHUB_TOKEN"]
        }
      }
    }
  }
}
```

The Copilot example intentionally keeps `copilot-gpt54` on `gpt-5.4` to show that provider-specific profiles can
choose a different model from the OpenAI default.

For swarm shadow runs, reviewer lanes and the aggregator can reference those same profiles:

```json
{
  "review": {
    "swarm": {
      "enabled": true,
      "shadowMode": true,
      "reviewers": [
        { "id": "correctness", "agentProfile": "chatgpt-gpt55" },
        { "id": "compat", "agentProfile": "copilot-gpt54" },
        { "id": "tests", "agentProfile": "copilot-claude" }
      ],
      "aggregator": {
        "agentProfile": "chatgpt-gpt55"
      }
    }
  }
}
```

Runtime override:
- `agent_profile` / `REVIEW_AGENT_PROFILE` / `REVIEW_MODEL_PROFILE`

## Swarm review shadow mode (optional)

Use this to opt into multi-reviewer orchestration without changing the public reviewer comment yet.
In shadow mode, the normal single-review path still produces the public PR comment; selected swarm reviewers and a
diagnostic aggregator run after the primary review succeeds, and diagnostics can log the shadow results for rollout
comparison.

All swarm settings are optional and default to off unless you explicitly enable them.

```json
{
  "review": {
    "swarm": {
      "enabled": true,
      "shadowMode": true,
      "reviewers": [
        "correctness",
        { "id": "tests", "provider": "copilot", "model": "gpt-5.2" }
      ],
      "maxParallel": 4,
      "publishSubreviews": false,
      "aggregatorModel": "gpt-5.5",
      "aggregator": {
        "provider": "openai",
        "model": "gpt-5.5",
        "reasoningEffort": "high"
      },
      "failOpenOnPartial": true,
      "metrics": true
    }
  }
}
```

Recommended rollout:
- enable `shadowMode` first
- keep `publishSubreviews: false`
- keep `diagnostics: true` during rollout if you want the resolved plan and shadow result summary in logs
- keep `metrics: true` if you want diagnostic JSON/Markdown artifacts plus append-only metrics JSONL under `artifacts/reviewer/swarm-shadow/`
- tune `maxParallel` to control how many independent reviewer lanes execute at once
- compare cost, latency, and finding quality before enabling public swarm output

GitHub Actions input/env aliases:
- `swarm_enabled` / `REVIEW_SWARM_ENABLED`
- `swarm_shadow_mode` / `REVIEW_SWARM_SHADOW_MODE`
- `swarm_reviewers` / `REVIEW_SWARM_REVIEWERS`
- `swarm_max_parallel` / `REVIEW_SWARM_MAX_PARALLEL`
- `swarm_publish_subreviews` / `REVIEW_SWARM_PUBLISH_SUBREVIEWS`
- `swarm_aggregator_model` / `REVIEW_SWARM_AGGREGATOR_MODEL`
- `swarm_fail_open_on_partial` / `REVIEW_SWARM_FAIL_OPEN_ON_PARTIAL`
- `swarm_metrics` / `REVIEW_SWARM_METRICS`

## Provider health checks + circuit breaker

Use this to preflight providers before request execution and temporarily open a breaker after repeated failures.

```json
{
  "review": {
    "providerHealthChecks": true,
    "providerHealthCheckTimeoutSeconds": 10,
    "providerCircuitBreakerFailures": 3,
    "providerCircuitBreakerOpenSeconds": 120
  }
}
```

## Auto-resolve + triage example

```json
{
  "review": {
    "mode": "inline",
    "length": "long",
    "reviewDiffRange": "current",
    "includeReviewThreads": true,
    "reviewThreadsAutoResolveAI": true,
    "reviewThreadsAutoResolveRequireEvidence": true,
    "reviewThreadsAutoResolveAIPostComment": true,
    "reviewThreadsAutoResolveSummaryComment": true,
    "reviewThreadsAutoResolveAIEmbedPlacement": "bottom",
    "reviewThreadsAutoResolveAISummary": true,    "reviewThreadsAutoResolveSummaryAlways": true,
    "reviewThreadsAutoResolveBotLogins": [
      "intelligencex-review",
      "copilot-pull-request-reviewer"
    ]
  }
}
```

## Triage-only example

Use this to skip the main review and only assess existing review threads.

```json
{
  "review": {
    "triageOnly": true,
    "reviewThreadsAutoResolveAI": true,
    "reviewThreadsAutoResolveAIPostComment": true
  }
}
```

## Intent preset example

```json
{
  "review": {
    "intent": "security"
  }
}
```

## Azure DevOps example (summary-only)

```json
{
  "review": {
    "codeHost": "azure",
    "azureOrg": "my-org",
    "azureProject": "my-project",
    "azureRepo": "my-repo",
    "azureTokenEnv": "SYSTEM_ACCESSTOKEN",
    "azureAuthScheme": "bearer"
  }
}
```

## Azure DevOps notes
- The reviewer uses the PR-level changes endpoint and follows continuation tokens for large diffs.
- Auth scheme heuristic when `azureAuthScheme` is not set: `SYSTEM_ACCESSTOKEN` defaults to `bearer`; otherwise a JWT-style token (two or more `.`) is treated as `bearer`; everything else defaults to `basic`/`pat`.
- Set `azureAuthScheme` explicitly to override the heuristic.

## Path filters example

```json
{
  "review": {
    "includePaths": ["src/**", "tests/**"],
    "excludePaths": ["**/*.md", "**/*.snap"],
    "skipPaths": ["**/*.lock"]
  }
}
```

## Language hints

```json
{
  "review": {
    "languageHints": true
  }
}
```

## Usage summary line

```json
{
  "review": {
    "reviewUsageSummary": true,
    "reviewUsageSummaryCacheMinutes": 30
  }
}
```

For OpenAI, the footer uses the ChatGPT account usage snapshot. For Claude, it uses the live Anthropic provider-limit snapshot.

## Usage budget guard (credits vs weekly limit)

Use this to fail early when selected usage budget sources are exhausted, and explicitly choose whether credits and/or weekly windows are allowed to keep runs going.

```json
{
  "review": {
    "reviewUsageBudgetGuard": true,
    "reviewUsageBudgetAllowCredits": true,
    "reviewUsageBudgetAllowWeeklyLimit": true
  }
}
```

GitHub Actions input/env aliases:
- `usage_budget_guard` / `REVIEW_USAGE_BUDGET_GUARD`
- `usage_budget_allow_credits` / `REVIEW_USAGE_BUDGET_ALLOW_CREDITS`
- `usage_budget_allow_weekly_limit` / `REVIEW_USAGE_BUDGET_ALLOW_WEEKLY_LIMIT`

## Budget summary note

```json
{
  "review": {
    "reviewBudgetSummary": true
  }
}
```

When context is truncated, the note explains impact directly in the PR comment:
- only included files/patch snippets were reviewed
- findings may miss issues outside included diff context
- increase `review.maxFiles` and/or `review.maxPatchChars` to widen coverage

## Structured findings (automation)

```json
{
  "review": {
    "structuredFindings": true
  }
}
```

## Static analysis (preview)

Enable analysis summaries sourced from SARIF or IntelligenceX findings JSON.

```json
{
  "analysis": {
    "enabled": true,
    "packs": ["all-50"],
    "configMode": "respect",
    "disabledRules": ["CA2000"],
    "severityOverrides": { "CA1062": "error" },
    "run": {
      "strict": false
    },
    "gate": {
      "enabled": true,
      "minSeverity": "warning",
      "types": ["vulnerability", "bug"],
      "failOnUnavailable": true,
      "failOnNoEnabledRules": true,
      "includeOutsidePackRules": false,
      "failOnHotspotsToReview": false
    },
    "results": {
      "inputs": ["artifacts/**/*.sarif", "artifacts/intelligencex.findings.json"],
      "minSeverity": "warning",
      "maxInline": 0,
      "summary": true,
      "summaryMaxItems": 10,
      "summaryPlacement": "bottom",
      "showPolicy": true,
      "policyRulePreviewItems": 10
    }
  }
}
```

`analysis.results.policyRulePreviewItems` controls how many rule IDs are shown per policy line:
- `0`: hide per-rule lists and keep counts only
- `10` (default): compact preview
- `50`, `100`, `500`: progressively fuller visibility (schema max: `500`)

`analysis.results.maxInline` defaults to `0`, which keeps static-analysis output in the summary only. Set it to a positive value only if you explicitly want the legacy inline-comment behavior.

`analysis.gate` enables a deterministic CI gate via `intelligencex analyze gate`:
- `minSeverity`: minimum severity to consider for gating.
- `types`: optional filter of rule types (when empty, all types are considered).
- `ruleIds`: optional explicit rule IDs (for example `IXTOOL001`) that should be gated even when `types` would not match.
- `failOnUnavailable`: fail when no result files match configured inputs or when result parsing fails.
- `failOnNoEnabledRules`: fail when `analysis.packs` selects zero rules.
- `includeOutsidePackRules`: when `true`, all findings from non-enabled rules can still fail the gate; explicit `ruleIds` always remain eligible.
  Gate output `Outside-pack findings` included/ignored counts are post-filter scoped (after `types`/`ruleIds` gate filters).
- `failOnHotspotsToReview`: when `true`, security hotspots in `to-review` state can fail the gate (after `minSeverity`/`types` filtering).

`analysis.run.strict` controls `intelligencex analyze run` exit semantics:
- `false` (default): tool runner failures are reported as warnings and the command exits `0`.
- `true`: any tool runner failure returns exit `1`.
- `--strict` or `--strict true`: force strict behavior for that invocation.
- `--strict false`: force non-strict behavior for that invocation (even if config has `run.strict=true`).
- `--pack <id>` / `--packs <id1,id2>`: override `analysis.packs` for that invocation.
- Setup shortcut: `intelligencex setup --with-config --analysis-enabled true --analysis-run-strict true`.

Setup analysis option constraints:
- `--analysis-*` options are supported only for `setup` when generating config from presets (`--with-config` and no `--config-json/--config-path` override).
- `--analysis-gate`, `--analysis-run-strict`, `--analysis-packs`, and `--analysis-export-path` require `--analysis-enabled true`.

Setup review-policy option constraints:
- `--review-intent`, `--review-strictness`, `--review-loop-policy`, `--review-vision-path`, and `--merge-blocker-*` options are setup-time config-generation options.
- They are intended for `setup` with generated config (`--with-config` and no `--config-json/--config-path` override).
- `--review-vision-path` is only supported with `--review-loop-policy vision`.

## Summary stability (avoid noisy reruns)

```json
{
  "review": {
    "summaryStability": true
  }
}
```

## Review history snapshot (repeat-review context)

Use this to feed a compact normalized history block into the review prompt instead of relying only on raw prior-summary prose.

```json
{
  "review": {
    "history": {
      "enabled": true,
      "includeIxSummaryHistory": true,
      "includeReviewThreads": true,
      "includeExternalBotSummaries": false,
      "externalBotLogins": ["claude", "copilot-pull-request-reviewer"],
      "artifacts": true,
      "maxRounds": 6,
      "maxItems": 8
    }
  }
}
```

Recommended first use:
- enable this when you want repeat reviewer runs to understand prior IX sticky-summary blockers and current thread state
- leave `includeExternalBotSummaries` off unless you explicitly want bounded Claude/Copilot bot summary excerpts as supporting context
- keep `summaryStability: true` enabled as well if you want same-SHA wording to stay stable across reruns

GitHub Actions input/env aliases:
- `history_enabled` / `REVIEW_HISTORY_ENABLED`
- `history_include_ix_summary_history` / `REVIEW_HISTORY_INCLUDE_IX_SUMMARY_HISTORY`
- `history_include_review_threads` / `REVIEW_HISTORY_INCLUDE_REVIEW_THREADS`
- `history_include_external_bot_summaries` / `REVIEW_HISTORY_INCLUDE_EXTERNAL_BOT_SUMMARIES`
- `history_external_bot_logins` / `REVIEW_HISTORY_EXTERNAL_BOT_LOGINS`
- `history_artifacts` / `REVIEW_HISTORY_ARTIFACTS`
- `history_max_rounds` / `REVIEW_HISTORY_MAX_ROUNDS`
- `history_max_items` / `REVIEW_HISTORY_MAX_ITEMS`

## Redaction (secrets)

When `redactPii` is enabled, the reviewer applies default redaction patterns for common secrets
(private keys, GitHub tokens, AWS access keys, JWTs, Authorization headers, and generic key/value secrets).
Override the defaults by setting `redactionPatterns`.

```json
{
  "review": {
    "redactPii": true,
    "redactionPatterns": [
      "-----BEGIN [A-Z ]*PRIVATE KEY-----[\\s\\S]+?-----END [A-Z ]*PRIVATE KEY-----",
      "\\bgh[pousr]_[A-Za-z0-9]{36}\\b"
    ],
    "redactionReplacement": "[REDACTED]"
  }
}
```

## Untrusted PR guardrails (forks)

When a pull request comes from a fork, the reviewer treats it as untrusted. By default it **skips** the review
to avoid accessing secrets. You can override this behavior explicitly if you are using `pull_request_target`
or have other safeguards in place.

```json
{
  "review": {
    "untrustedPrAllowSecrets": false,
    "untrustedPrAllowWrites": false
  }
}
```

Set `untrustedPrAllowSecrets` to `true` to allow reviews on forked PRs. Set `untrustedPrAllowWrites` to `true`
to allow posting comments or resolving threads on untrusted PRs (default is `false`).

## Workflow integrity guardrail

By default the reviewer applies a workflow integrity guardrail:
- Workflow files (`.github/workflows/*.yml|*.yaml`) are excluded from review context.
- If a PR changes only workflow files, the reviewer skips with an explicit summary note.

This prevents self-modifying workflow runs while still reviewing non-workflow changes in mixed PRs.
Set `allowWorkflowChanges` (or `REVIEW_ALLOW_WORKFLOW_CHANGES=true`) to review workflow files too.

```json
{
  "review": {
    "allowWorkflowChanges": true
  }
}
```

## Secrets audit logging

When enabled, the reviewer emits a short audit log listing which secret sources were accessed
(environment variable names, auth bundle source). Secret values are never logged.

Env: `REVIEW_SECRETS_AUDIT`

```json
{
  "review": {
    "secretsAudit": true
  }
}
```

## Output style example

```json
{
  "review": {
    "outputStyle": "compact",
    "narrativeMode": "freedom",
    "style": "colorful",
    "tone": "friendly"
  }
}
```

`narrativeMode` controls writing style while preserving merge-blocking behavior:
- `structured` (default): deterministic, concise rationale formatting
- `freedom`: natural reviewer voice (bullets/paragraphs/tables) while keeping explicit unblock actions

GitHub Actions input/env aliases:
- `narrative_mode` / `REVIEW_NARRATIVE_MODE`

## Copilot CLI auth env pass-through

Use this to forward selected environment variables into the Copilot CLI process without committing secrets.
By default the CLI process **does** inherit the runner environment. Set `inheritEnvironment` to `false` and use
`envAllowlist`/`env` to pass only what the CLI needs when you want a strict environment.
Set `launcher` to `gh` to explicitly run Copilot through `gh copilot --`.
Set `launcher` to `auto` to use the standalone `copilot` binary path. This is the safer default for reviewer CI runs:
the reviewer validates status/auth through the CLI server protocol, then generates the review through the documented
non-interactive prompt mode with built-in Model Context Protocol (MCP) servers disabled via
`--disable-builtin-mcps` and with the Copilot tool surface disabled via `--available-tools=none` to avoid
long-running server-session hangs, tool startup failures, and prompt-injection risk on hosted runners.
Use `autoInstall` with the standalone path when the runner does not already have `copilot` installed.
Set `model` only when you want to force a Copilot CLI model id. When `provider` is `copilot` and only the generic
`review.model` is the default OpenAI value, the reviewer leaves Copilot model selection to the CLI default.

For GitHub Actions runs, set a repository or organization Actions secret named `COPILOT_GITHUB_TOKEN` to a
fine-grained GitHub token with the `Copilot Requests` permission. The built-in Actions `GITHUB_TOKEN` and GitHub App
installation tokens are not sufficient for Copilot CLI model requests.

GitHub Actions repo/org variable aliases:
- `IX_REVIEW_PROVIDER`
- `IX_REVIEW_MODEL`
- `IX_REVIEW_COPILOT_MODEL`
- `IX_REVIEW_AGENT_PROFILE`
- `IX_REVIEW_COPILOT_LAUNCHER`
- `IX_REVIEW_COPILOT_AUTO_INSTALL`
- `IX_REVIEW_COPILOT_AUTO_INSTALL_METHOD`
- `IX_REVIEW_COPILOT_AUTO_INSTALL_PRERELEASE`

```json
{
  "review": {
    "provider": "copilot"
  },
  "copilot": {
    "launcher": "gh",
    "model": "claude-sonnet-4.6",
    "inheritEnvironment": false,
    "envAllowlist": ["GH_TOKEN", "GITHUB_TOKEN"]
  }
}
```

`copilot.env` can be used to set fixed, non-secret environment variables for the Copilot CLI.

## Copilot direct (experimental)

This path skips the Copilot CLI and posts directly to a compatible HTTP endpoint. It is not enabled by default.

```json
{
  "review": {
    "provider": "copilot"
  },
  "copilot": {
    "transport": "direct",
    "directUrl": "https://example.internal/copilot/chat",
    "directTokenEnv": "COPILOT_DIRECT_TOKEN",
    "directTimeoutSeconds": 60
  }
}
```

If `directTokenEnv` is set, the value is pulled from the environment at runtime.
`directToken` or an `Authorization` header in `directHeaders` is required for most endpoints.
Use `directHeaders` to attach custom headers required by your gateway.
Prefer `directTokenEnv` over `directToken` to avoid committing secrets to source control.

## Common knobs
- `provider`: `openai`/`codex`/`chatgpt`/`openai-codex`, `claude`/`anthropic`, `openai-compatible` (aliases: `openai-api`, `ollama`, `openrouter`), or `copilot`
- `providerFallback`: optional fallback provider (same value set as `provider`)
- `model`: model name for the selected provider
- `reasoningEffort`: `minimal|low|medium|high|xhigh` (when set to low/medium/high, the header shows a reasoning level label)
- `mode`: `inline`, `summary`, or `hybrid`
- `length`: `short|medium|long`
- `intent`: `security|performance|perf|maintainability` (sets focus areas and default strictness/notes when not set)
- `visionPath`: optional repository-local vision markdown path used by setup-time vision policy inference
- `mergeBlockerSections`: custom heading names that represent merge-blocking sections in the review output
- `mergeBlockerRequireAllSections`: require every configured blocker section to be present before treating review as unblocked
- `mergeBlockerRequireSectionMatch`: require at least one configured blocker section to appear before treating review as unblocked
- `codeHost`: `github` or `azure`
- `reviewDiffRange`: `current`, `pr-base`, or `first-review`
- `outputStyle`: rendering style preset (for the compact template, use `compact`)
- `narrativeMode`: `structured` (default) or `freedom`
- `reviewUsageSummary`: append usage line to the footer (OpenAI ChatGPT usage snapshot or Claude live limit snapshot)
- `openaiAccountId`: pin a preferred ChatGPT account id
- `openaiAccountIds`: ordered account ids used for rotation/failover
- `openaiAccountRotation`: `first-available`, `round-robin`, or `sticky`
- `openaiAccountFailover`: allow trying additional accounts when the selected account is blocked/unavailable
- `reviewUsageBudgetGuard`: fail early when configured usage budget sources are exhausted
- `reviewUsageBudgetAllowCredits`: allow runs when credits are available
- `reviewUsageBudgetAllowWeeklyLimit`: allow runs when weekly limit capacity is available
- `ciContext.enabled`: opt into reviewer-side CI/check awareness (default off)
- `ciContext.includeCheckSummary`: include PR check counts and failing check names when CI context is enabled
- `ciContext.includeFailedRuns`: include failed workflow run metadata for the current SHA when CI context is enabled
- `ciContext.includeFailureSnippets`: `off|auto|always` for bounded failed job/step summaries when CI context is enabled
- `ciContext.maxFailedRuns`: cap failing workflow runs included in reviewer context
- `ciContext.maxSnippetCharsPerRun`: cap failure evidence chars per failed run
- `ciContext.classifyInfraFailures`: separate infra-blocked failures from actionable code/test failures
- `githubMaxConcurrency`: limit concurrent GitHub API requests (default 4)
- `languageHints`: include language-aware hint block in the prompt
- `reviewBudgetSummary`: include a note when review context is truncated
- `retryCount`: total attempts for provider requests
- `retryBackoffMultiplier`: exponential backoff multiplier (default 2.0)
- `retryJitterMinMs`/`retryJitterMaxMs`: retry jitter bounds
- `providerHealthChecks`: run provider health checks before calls (default true)
- `providerHealthCheckTimeoutSeconds`: timeout for provider health checks
- `providerCircuitBreakerFailures`: consecutive failures before opening provider circuit (set `0` to disable)
- `providerCircuitBreakerOpenSeconds`: how long the provider circuit remains open
- `failOpen`: emit a failure summary instead of failing the workflow
- `failOpenTransientOnly`: when true, fail-open only on transient errors
  The bundled GitHub workflow exports `REVIEW_FAIL_OPEN=true` and `REVIEW_FAIL_OPEN_TRANSIENT_ONLY=false` so provider auth/runtime failures leave a summary comment instead of blocking CI.
- `summaryStability`: reuse the previous summary (same commit) as prompt context to avoid noisy rewrites
- `history.enabled`: include a compact repeat-review history snapshot in the prompt
- `history.includeIxSummaryHistory`: include the prior IX sticky summary as normalized state
- `history.includeReviewThreads`: include review thread state/counts in the history snapshot
- `history.includeExternalBotSummaries`: include bounded external bot summary excerpts as supporting context only
- `history.externalBotLogins`: external bot authors allowed when `includeExternalBotSummaries` is enabled
- `history.artifacts`: emit bounded JSON/Markdown history artifacts under `artifacts/reviewer/history/`
- `history.maxRounds`: cap how many prior owned IX summary rounds are folded into history
- `history.maxItems`: cap normalized history items and thread excerpts
- `structuredFindings`: emit a structured findings JSON block for automation
- `swarm.enabled`: opt into swarm review orchestration (default off)
- `swarm.shadowMode`: run swarm internally without replacing the public review output
- `swarm.reviewers`: selected swarm reviewer roles, either as ids or reviewer objects with provider/model overrides
- `swarm.maxParallel`: swarm concurrency cap
- `swarm.publishSubreviews`: whether sub-review outputs can be published directly (recommended false)
- `swarm.aggregatorModel`: optional compatibility alias for the swarm aggregator model
- `swarm.aggregator`: optional structured aggregator definition with provider/model/reasoningEffort
- `swarm.failOpenOnPartial`: allow swarm aggregation to proceed when one sub-reviewer fails
- `swarm.metrics`: emit swarm metrics/artifacts for comparison and rollout evaluation
- `skipPaths`: if **all** changed files in a PR match these globs, skip reviewing the entire PR
- `skipBinaryFiles`: skip binary assets (images, archives, executables) from review context (default true)
- `skipGeneratedFiles`: skip generated files (build output, generated sources) from review context (default true)
- `generatedFileGlobs`: extra glob patterns to treat as generated files (appended to defaults)
- `includePaths`: only review files matching these globs
- `excludePaths`: ignore files matching these globs
- `allowWorkflowChanges`: include `.github/workflows/*` changes in review context (default excludes them)
- `secretsAudit`: emit an audit log of secret sources used (default true)
- `includeReviewThreads`: include existing review threads in context
- `triageOnly`: run thread triage only (skip full review)
- `reviewThreadsAutoResolve*`: auto-resolve rules for bot threads
- `reviewThreadsAutoResolveAIReply`: reply on kept threads to explain why they were not resolved (includes resolve failures)
- `reviewThreadsAutoResolveRequireEvidence`: require a diff evidence snippet to resolve threads
- `reviewThreadsAutoResolveSummaryAlways`: always append a triage summary line to the main review comment
- `reviewThreadsAutoResolveSummaryComment`: post a standalone summary comment for auto-resolve decisions
- `reviewThreadsAutoResolveAIEmbedPlacement`: `top` or `bottom` placement for embedded triage blocks
- `azureOrg`/`azureProject`/`azureRepo`: Azure DevOps identifiers
- `azureBaseUrl`: override Azure DevOps base URL (defaults to `SYSTEM_COLLECTIONURI` or `https://dev.azure.com/{org}`)
- `azureTokenEnv`: env var name that contains the ADO token (default `SYSTEM_ACCESSTOKEN` if set)
- `azureAuthScheme`: `bearer` (System.AccessToken/JWT) or `basic`/`pat`
- `copilot.transport`: `cli` or `direct` (aliases: `api`, `http`)
- `copilot.model`: optional Copilot-specific model override; when omitted, the CLI default is used unless `review.model` was set to a non-default value
- `copilot.launcher`: `binary`, `gh`, or `auto`; `auto` uses the standalone binary path, while `gh` explicitly executes `gh copilot --` before the reviewer server flags
- `copilot.inheritEnvironment`: inherit full runner environment for Copilot CLI (`true` by default)

**Path filter order of operations**
1. `skipPaths` is evaluated first at the PR level. If **every** changed file matches `skipPaths`, the PR is skipped.
2. If the PR is not skipped, `skipBinaryFiles`/`skipGeneratedFiles` (when enabled) remove binary and generated files from the review list.
   `generatedFileGlobs` extends the default generated-file patterns.
3. `includePaths` (if set) selects which changed files are eligible for review.
4. Finally, `excludePaths` (if set) removes any remaining files from review.

## Auto-resolve notes
- `reviewThreadsAutoResolveBotLogins` defaults to `intelligencex-review` and `copilot-pull-request-reviewer`. When set,
  it acts as an allowlist for auto-resolve; set an empty list to fall back to generic bot detection.
- `reviewThreadsAutoResolveDiffRange` supports `current`, `pr-base`, or `first-review`.
- Thread assessment ids are trimmed, expected to be unique, and keyed case-insensitively; missing ids are skipped and duplicate ids keep the last occurrence (with warnings).

## Choosing a diff range (review vs last commit)

The reviewer does not default to "last commit only". By default, `reviewDiffRange: current` uses the current PR file list
from GitHub (cumulative changes for the PR at HEAD).

When you want the bot to always have an explicit base SHA range in its prompt (useful for debugging and determinism),
set `reviewDiffRange: pr-base` so the prompt includes a `PR base → head (abc1234..def5678)` range label.

There is no dedicated "last commit only" diff range at the moment. If you want an incremental review, prefer keeping the
review comment sticky and rely on `summaryStability: true` so the summary stays consistent unless new evidence changes it.

Tip: if you enable `reviewThreadsIncludeBots: true`, the model can see its own prior guidance in thread context, which
can reduce repeated suggestions. Tradeoff: it can also "echo" incorrect prior bot advice, so consider leaving this off
in repos where you expect frequent bot mistakes or highly sensitive review requirements.

## Review thread states (active/resolved/stale)

GitHub can mark a review thread as:
- active: still anchored to the current diff
- stale: shown as "Outdated" in the GitHub UI (the diff hunk changed), but the thread may still be unresolved
- resolved: explicitly resolved (hidden/collapsed by default)

Important: **Outdated does not mean resolved**. Outdated only means the diff context changed. If you want to reduce PR noise from bot-only outdated threads, enable `reviewThreadsAutoResolveStale` to auto-resolve stale threads that contain only bot comments.

## Auto-resolve troubleshooting
If you see `Resource not accessible by integration` when resolving threads:
- Re-authorize or reinstall the GitHub App after permission changes.
- Confirm the app installation includes this repository.
- Ensure the app has Pull requests: Read & write (and Issues: write if needed).
- Verify `INTELLIGENCEX_GITHUB_APP_ID`/`INTELLIGENCEX_GITHUB_APP_PRIVATE_KEY` point to the intended app.
- To bypass the app token, remove the app secrets so `GITHUB_TOKEN` is used instead.
- `GITHUB_TOKEN` is available in GitHub Actions; outside Actions you need a PAT and set it as `GITHUB_TOKEN`.

## Full example
See `../../Schemas/reviewer.schema.json` for all available options.
