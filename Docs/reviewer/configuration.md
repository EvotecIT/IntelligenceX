# Reviewer Configuration

You can configure the reviewer with env vars **or** a repo-local file at `.intelligencex/reviewer.json`.
The JSON file is the cleanest way to keep settings versioned with your repo.

Schema: `../../Schemas/reviewer.schema.json`

The reviewer validates `.intelligencex/reviewer.json` against the schema at runtime.
Unknown properties emit warnings; invalid types or enum values fail the run.

## Minimal example

```json
{
  "review": {
    "provider": "openai",
    "model": "gpt-5.2-codex",
    "mode": "inline",
    "length": "long",
    "outputStyle": "claude",
    "reviewUsageSummary": false
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

## Budget summary note

```json
{
  "review": {
    "reviewBudgetSummary": true
  }
}
```

## Structured findings (automation)

```json
{
  "review": {
    "structuredFindings": true
  }
}
```

## Static analysis (preview)

Enable analysis summaries and inline findings sourced from SARIF or IntelligenceX findings JSON.

```json
{
  "analysis": {
    "enabled": true,
    "packs": ["csharp-default", "powershell-default"],
    "configMode": "respect",
    "disabledRules": ["CA2000"],
    "severityOverrides": { "CA1062": "error" },
    "results": {
      "inputs": ["artifacts/**/*.sarif", "artifacts/intelligencex.findings.json"],
      "minSeverity": "warning",
      "maxInline": 20,
      "summary": true,
      "summaryMaxItems": 10,
      "summaryPlacement": "bottom"
    }
  }
}
```

## Summary stability (avoid noisy reruns)

```json
{
  "review": {
    "summaryStability": true
  }
}
```

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

By default the reviewer skips PRs that modify GitHub Actions workflows. This prevents self-modifying workflow runs.
Set `allowWorkflowChanges` (or `REVIEW_ALLOW_WORKFLOW_CHANGES=true`) to override.

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
    "outputStyle": "claude",
    "style": "colorful",
    "tone": "friendly"
  }
}
```

## Copilot CLI auth env pass-through

Use this to forward selected environment variables into the Copilot CLI process without committing secrets.
By default the CLI process **does** inherit the runner environment. Set `inheritEnvironment` to `false` and use
`envAllowlist`/`env` to pass only what the CLI needs when you want a strict environment.

```json
{
  "review": {
    "provider": "copilot"
  },
  "copilot": {
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
- `provider`: `openai` or `copilot`
- `model`: model name for the selected provider
- `reasoningEffort`: `minimal|low|medium|high|xhigh` (when set to low/medium/high, the header shows a reasoning level label)
- `mode`: `inline`, `summary`, or `hybrid`
- `length`: `short|medium|long`
- `intent`: `security|performance|perf|maintainability` (sets focus areas if none provided)
- `codeHost`: `github` or `azure`
- `reviewDiffRange`: `current`, `pr-base`, or `first-review`
- `outputStyle`: rendering style preset
- `reviewUsageSummary`: append usage line to the footer (ChatGPT auth only)
- `githubMaxConcurrency`: limit concurrent GitHub API requests (default 4)
- `languageHints`: include language-aware hint block in the prompt
- `reviewBudgetSummary`: include a note when review context is truncated
- `retryCount`: total attempts for provider requests
- `retryBackoffMultiplier`: exponential backoff multiplier (default 2.0)
- `retryJitterMinMs`/`retryJitterMaxMs`: retry jitter bounds
- `failOpen`: emit a failure summary instead of failing the workflow
- `failOpenTransientOnly`: when true, fail-open only on transient errors
- `summaryStability`: reuse the previous summary (same commit) as prompt context to avoid noisy rewrites
- `structuredFindings`: emit a structured findings JSON block for automation
- `skipPaths`: if **all** changed files in a PR match these globs, skip reviewing the entire PR
- `skipBinaryFiles`: skip binary assets (images, archives, executables) from review context (default true)
- `skipGeneratedFiles`: skip generated files (build output, generated sources) from review context (default true)
- `generatedFileGlobs`: extra glob patterns to treat as generated files (appended to defaults)
- `includePaths`: only review files matching these globs
- `excludePaths`: ignore files matching these globs
- `allowWorkflowChanges`: allow reviews to run when `.github/workflows/*` changes are present
- `secretsAudit`: emit an audit log of secret sources used (default true)
- `includeReviewThreads`: include existing review threads in context
- `triageOnly`: run thread triage only (skip full review)
- `reviewThreadsAutoResolve*`: auto-resolve rules for bot threads
- `reviewThreadsAutoResolveAIReply`: reply on kept threads to explain why they were not resolved (includes resolve failures)
- `reviewThreadsAutoResolveRequireEvidence`: require a diff evidence snippet to resolve threads
- `reviewThreadsAutoResolveSummaryAlways`: always append a triage summary line to the main review comment
- `reviewThreadsAutoResolveSummaryComment`: post a standalone summary comment for auto-resolve decisions
- `reviewThreadsAutoResolveAIEmbedPlacement`: `top` or `bottom` placement for embedded triage blocks- `azureOrg`/`azureProject`/`azureRepo`: Azure DevOps identifiers
- `azureBaseUrl`: override Azure DevOps base URL (defaults to `SYSTEM_COLLECTIONURI` or `https://dev.azure.com/{org}`)
- `azureTokenEnv`: env var name that contains the ADO token (default `SYSTEM_ACCESSTOKEN` if set)
- `azureAuthScheme`: `bearer` (System.AccessToken/JWT) or `basic`/`pat`
- `copilot.transport`: `cli` or `direct` (aliases: `api`, `http`)
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

## Full example
See `../../Schemas/reviewer.schema.json` for all available options.
