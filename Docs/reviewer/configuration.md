# Reviewer Configuration

You can configure the reviewer with env vars **or** a repo-local file at `.intelligencex/reviewer.json`.
The JSON file is the cleanest way to keep settings versioned with your repo.

Schema: `../../Schemas/reviewer.schema.json`

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

## Common knobs
- `provider`: `openai` or `copilot`
- `model`: model name for the selected provider
- `mode`: `inline`, `summary`, or `hybrid`
- `length`: `short|medium|long`
- `outputStyle`: rendering style preset
- `reviewUsageSummary`: append usage line to the footer (ChatGPT auth only)
- `includePaths`: only review files matching these globs
- `excludePaths`: ignore files matching these globs
- `includeReviewThreads`: include existing review threads in context
- `reviewThreadsAutoResolve*`: auto-resolve rules for bot threads

## Auto-resolve notes
- `reviewThreadsAutoResolveBotLogins` defaults to `intelligencex-review` and `copilot-pull-request-reviewer`.
- `reviewThreadsAutoResolveDiffRange` supports `current`, `pr-base`, or `first-review`.

## Full example
See `../../Schemas/reviewer.schema.json` for all available options.
