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
    "outputStyle": "claude",
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
    "outputStyle": "claude",
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
    "outputStyle": "claude"
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
