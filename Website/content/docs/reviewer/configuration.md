---
title: Reviewer Configuration
description: All configuration options for the IntelligenceX reviewer
collection: docs
layout: docs
nav.weight: 40
---

# Reviewer Configuration

You can configure the reviewer with environment variables **or** a repo-local file at `.intelligencex/reviewer.json`. The JSON file is the cleanest way to keep settings versioned with your repo.

## Minimal Example

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

## Common Knobs

| Setting | Values | Description |
|---------|--------|-------------|
| `provider` | `openai`, `copilot` | AI provider to use |
| `model` | model name | Model for the selected provider |
| `mode` | `inline`, `summary`, `hybrid` | Review comment style |
| `length` | `short`, `medium`, `long` | Review detail level |
| `outputStyle` | style preset | Rendering style for output |
| `reviewUsageSummary` | `true`/`false` | Append usage line to footer |
| `includeReviewThreads` | `true`/`false` | Include existing threads in context |

## Auto-Resolve

The reviewer can automatically resolve stale bot threads:

- `reviewThreadsAutoResolveBotLogins` defaults to `intelligencex-review` and `copilot-pull-request-reviewer`
- `reviewThreadsAutoResolveDiffRange` supports `current`, `pr-base`, or `first-review`

## Review Presets

Use presets during wizard setup:

| Preset | Description |
|--------|-------------|
| `balanced` | Default, well-rounded review |
| `picky` | Strict, catches more issues |
| `security` | Focus on security vulnerabilities |
| `performance` | Focus on performance issues |
| `tests` | Focus on test coverage and quality |
| `minimal` | Brief, high-level review only |

## Full Schema

See the [reviewer.schema.json](https://github.com/EvotecIT/IntelligenceX/blob/master/Schemas/reviewer.schema.json) for all available options.
