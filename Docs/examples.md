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

`@68fe2c83e1a7d97d5aad6c4c8223c1d7eb8031e7` is a pinned commit SHA for the reusable workflow.
This is recommended for supply-chain safety. To upgrade, replace it with a newer commit SHA from `evotecit/github-actions` releases.

```yaml
name: AI Code Review
on:
  pull_request:
    types: [opened, synchronize]

jobs:
  review:
    uses: evotecit/github-actions/.github/workflows/review-intelligencex.yml@68fe2c83e1a7d97d5aad6c4c8223c1d7eb8031e7
    with:
      reviewer_source: release
      openai_transport: native
      output_style: claude
      style: balanced
    secrets:
      INTELLIGENCEX_AUTH_B64: ${{ secrets.INTELLIGENCEX_AUTH_B64 }}
      INTELLIGENCEX_GITHUB_APP_ID: ${{ secrets.INTELLIGENCEX_GITHUB_APP_ID }}
      INTELLIGENCEX_GITHUB_APP_PRIVATE_KEY: ${{ secrets.INTELLIGENCEX_GITHUB_APP_PRIVATE_KEY }}
```
