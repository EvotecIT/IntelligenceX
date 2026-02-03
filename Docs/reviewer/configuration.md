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

## Usage summary line

```json
{
  "review": {
    "reviewUsageSummary": true,
    "reviewUsageSummaryCacheMinutes": 30
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

```json
{
  "review": {
    "provider": "copilot"
  },
  "copilot": {
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

## Common knobs
- `provider`: `openai` or `copilot`
- `model`: model name for the selected provider
- `mode`: `inline`, `summary`, or `hybrid`
- `length`: `short|medium|long`
- `intent`: `security|performance|perf|maintainability` (sets focus areas if none provided)
- `codeHost`: `github` or `azure`
- `reviewDiffRange`: `current`, `pr-base`, or `first-review`
- `outputStyle`: rendering style preset
- `reviewUsageSummary`: append usage line to the footer (ChatGPT auth only)
- `retryCount`: total attempts for provider requests
- `retryBackoffMultiplier`: exponential backoff multiplier (default 2.0)
- `retryJitterMinMs`/`retryJitterMaxMs`: retry jitter bounds
- `failOpen`: emit a failure summary instead of failing the workflow
- `failOpenTransientOnly`: when true, fail-open only on transient errors
- `skipPaths`: if **all** changed files in a PR match these globs, skip reviewing the entire PR
- `includePaths`: only review files matching these globs
- `excludePaths`: ignore files matching these globs
- `includeReviewThreads`: include existing review threads in context
- `triageOnly`: run thread triage only (skip full review)
- `reviewThreadsAutoResolve*`: auto-resolve rules for bot threads
- `azureOrg`/`azureProject`/`azureRepo`: Azure DevOps identifiers
- `azureBaseUrl`: override Azure DevOps base URL (defaults to `SYSTEM_COLLECTIONURI` or `https://dev.azure.com/{org}`)
- `azureTokenEnv`: env var name that contains the ADO token (default `SYSTEM_ACCESSTOKEN` if set)
- `azureAuthScheme`: `bearer` (System.AccessToken) or `basic`/`pat`
- `copilot.transport`: `cli` or `direct` (experimental)

**Path filter order of operations**
1. `skipPaths` is evaluated first at the PR level. If **every** changed file matches `skipPaths`, the PR is skipped.
2. If the PR is not skipped, `includePaths` (if set) selects which changed files are eligible for review.
3. Finally, `excludePaths` (if set) removes any remaining files from review.

## Auto-resolve notes
- `reviewThreadsAutoResolveBotLogins` defaults to `intelligencex-review` and `copilot-pull-request-reviewer`.
- `reviewThreadsAutoResolveDiffRange` supports `current`, `pr-base`, or `first-review`.

## Full example
See `../../Schemas/reviewer.schema.json` for all available options.
