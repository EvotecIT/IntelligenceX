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

## Auto-resolve + triage example

```json
{
  "review": {
    "mode": "inline",
    "length": "long",
    "reviewDiffRange": "current",
    "includeReviewThreads": true,
    "reviewThreadsAutoResolveAI": true,
    "reviewThreadsAutoResolveAIPostComment": true,
    "reviewThreadsAutoResolveAISummary": true,
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

## Summary stability (avoid noisy reruns)

```json
{
  "review": {
    "summaryStability": true
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
- `includePaths`: only review files matching these globs
- `excludePaths`: ignore files matching these globs
- `includeReviewThreads`: include existing review threads in context
- `triageOnly`: run thread triage only (skip full review)
- `reviewThreadsAutoResolve*`: auto-resolve rules for bot threads
- `azureOrg`/`azureProject`/`azureRepo`: Azure DevOps identifiers
- `azureBaseUrl`: override Azure DevOps base URL (defaults to `SYSTEM_COLLECTIONURI` or `https://dev.azure.com/{org}`)
- `azureTokenEnv`: env var name that contains the ADO token (default `SYSTEM_ACCESSTOKEN` if set)
- `azureAuthScheme`: `bearer` (System.AccessToken/JWT) or `basic`/`pat`
- `copilot.transport`: `cli` or `direct` (aliases: `api`, `http`)
- `copilot.inheritEnvironment`: inherit full runner environment for Copilot CLI (`true` by default)

**Path filter order of operations**
1. `skipPaths` is evaluated first at the PR level. If **every** changed file matches `skipPaths`, the PR is skipped.
2. If the PR is not skipped, `includePaths` (if set) selects which changed files are eligible for review.
3. Finally, `excludePaths` (if set) removes any remaining files from review.

## Auto-resolve notes
- `reviewThreadsAutoResolveBotLogins` defaults to `intelligencex-review` and `copilot-pull-request-reviewer`.
- `reviewThreadsAutoResolveDiffRange` supports `current`, `pr-base`, or `first-review`.

## Full example
See `../../Schemas/reviewer.schema.json` for all available options.
