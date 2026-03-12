---
title: IntelligenceX Examples
description: Use practical IntelligenceX examples for reviewer presets, provider configuration, workflow reuse, file filtering, and multi-repository onboarding.
---

# Examples

Practical configuration examples for common IntelligenceX setups.

## Reviewer Presets

### Minimal

```json
{
  "review": {
    "provider": "openai",
    "model": "gpt-4o-mini",
    "mode": "summary",
    "length": "short",
    "style": "minimal"
  }
}
```

### Balanced

```json
{
  "review": {
    "provider": "openai",
    "model": "gpt-4o",
    "mode": "hybrid",
    "length": "medium",
    "outputStyle": "compact",
    "style": "balanced"
  }
}
```

### Picky

```json
{
  "review": {
    "provider": "openai",
    "model": "gpt-5.3-codex",
    "mode": "inline",
    "length": "long",
    "outputStyle": "compact",
    "style": "picky"
  }
}
```

## Provider Example: Copilot

```json
{
  "review": {
    "provider": "copilot",
    "mode": "hybrid",
    "length": "medium",
    "outputStyle": "compact"
  }
}
```

## File Filtering Example

```json
{
  "review": {
    "provider": "openai",
    "model": "gpt-4o",
    "mode": "hybrid",
    "excludePatterns": [
      "**/*.test.cs",
      "**/*.Designer.cs",
      "**/Migrations/**"
    ]
  }
}
```

## Multi-Repository Onboarding

```bash
intelligencex setup wizard
```

Then select multiple repositories in the wizard "Select Repositories" step.

For preflight path recommendation before selecting repos:

```bash
intelligencex setup autodetect --json
```

## Workflow Example

`@<pinned-sha>` is a pinned commit SHA for the reusable workflow.
This is recommended for supply-chain safety. To upgrade, replace it with a newer commit SHA from `EvotecIT/IntelligenceX` releases.

```yaml
name: AI Code Review
on:
  pull_request:
    types: [opened, synchronize]

jobs:
  review:
    uses: EvotecIT/IntelligenceX/.github/workflows/review-intelligencex-reusable.yml@<pinned-sha>
    with:
      reviewer_source: release
      openai_transport: native
      output_style: compact
      style: balanced
    secrets:
      INTELLIGENCEX_AUTH_B64: ${{ secrets.INTELLIGENCEX_AUTH_B64 }}
      INTELLIGENCEX_GITHUB_APP_ID: ${{ secrets.INTELLIGENCEX_GITHUB_APP_ID }}
      INTELLIGENCEX_GITHUB_APP_PRIVATE_KEY: ${{ secrets.INTELLIGENCEX_GITHUB_APP_PRIVATE_KEY }}
```
