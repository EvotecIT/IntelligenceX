---
title: Examples
description: Real-world configuration examples for IntelligenceX - presets, custom configs, and multi-repo setups
collection: docs
layout: docs
---

# Examples

Practical configuration examples to help you get the most out of IntelligenceX.

## Reviewer Presets

IntelligenceX ships with built-in presets that control how thorough the review is.

### Minimal Preset

Quick feedback on obvious issues only:

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

### Balanced Preset (Default)

Good coverage without being noisy:

```json
{
  "review": {
    "provider": "openai",
    "model": "gpt-4o",
    "mode": "hybrid",
    "length": "medium",
    "outputStyle": "claude",
    "style": "balanced"
  }
}
```

### Picky Preset

Thorough review with inline comments on every file:

```json
{
  "review": {
    "provider": "openai",
    "model": "gpt-5.2-codex",
    "mode": "inline",
    "length": "long",
    "outputStyle": "claude",
    "style": "picky"
  }
}
```

## Custom Configuration

### Using GitHub Copilot Instead of OpenAI

```json
{
  "review": {
    "provider": "copilot",
    "mode": "hybrid",
    "length": "medium",
    "outputStyle": "claude"
  }
}
```

### Filtering Files

Exclude test files and auto-generated code from reviews:

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

## Multi-Repo Setup

Use the CLI wizard to onboard multiple repositories at once:

```bash
# Interactive multi-repo setup
intelligencex setup wizard --repos org/repo1 org/repo2 org/repo3

# Or use a config file
intelligencex setup wizard --config repos.json
```

Example `repos.json`:

```json
{
  "repositories": [
    { "owner": "myorg", "name": "frontend", "preset": "balanced" },
    { "owner": "myorg", "name": "backend", "preset": "picky" },
    { "owner": "myorg", "name": "docs", "preset": "minimal" }
  ]
}
```

## GitHub Actions Workflow

### Basic Workflow

```yaml
name: AI Code Review
on:
  pull_request:
    types: [opened, synchronize]

jobs:
  review:
    uses: evotecit/github-actions/.github/workflows/review-intelligencex.yml@master
    with:
      reviewer_source: release
      openai_transport: native
      output_style: claude
      style: balanced
    secrets: inherit
```

### With Custom Config File

```yaml
jobs:
  review:
    uses: evotecit/github-actions/.github/workflows/review-intelligencex.yml@master
    with:
      reviewer_source: release
      config_path: .github/intelligencex.json
    secrets: inherit
```
