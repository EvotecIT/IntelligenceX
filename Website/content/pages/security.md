---
title: Security & Trust Model
description: IntelligenceX zero-trust design - no backend, your credentials, your control
slug: security
collection: pages
layout: page
---

# Security and Trust Model

IntelligenceX is built on a zero-trust design. You don't have to trust us - you trust yourself.

## Core Principles

### No Backend Service

IntelligenceX has **no backend**. All onboarding happens locally on your machine. The reviewer runs entirely within your GitHub Actions environment. We don't operate any servers that touch your code or credentials.

### Your Credentials, Your Control

You authenticate with **your own** ChatGPT or Copilot account. No shared API keys, no pooled quotas:

- **ChatGPT**: OAuth login via your browser, auth bundle stored as a GitHub Actions secret
- **Copilot**: Uses your existing GitHub Copilot access
- **Optional encryption**: Set `INTELLIGENCEX_AUTH_KEY` to encrypt the local auth store

### Bring Your Own GitHub App

For organizations that need full control:

- Create a GitHub App under **your** organization
- **Your** branding on the review bot
- **Your** permission scopes
- **Your** audit trail in org settings
- The CLI wizard supports this flow natively

## GitHub Authentication Modes

| Mode | Best For | How It Works |
|------|----------|-------------|
| **GitHub App** (recommended) | Organizations | Install your own App for branded bot identity and fine-grained permissions |
| **OAuth Device Flow** | Single repos | Fastest setup, no App required |
| **Personal Access Token** | Restricted environments | Policy-compliant, manual token management |

## What the Tool Changes

IntelligenceX makes minimal, transparent changes to your repository:

- Adds `.github/workflows/review-intelligencex.yml` (the review workflow)
- Optionally adds `.intelligencex/reviewer.json` (configuration)
- **All changes are made via PRs by default** - you review before merging

## Data Flow

When the reviewer analyzes a PR:

1. GitHub Actions checks out your code diff
2. The diff is sent **directly** to your chosen AI provider (ChatGPT or Copilot)
3. The AI response is posted as PR comments
4. **No data passes through IntelligenceX infrastructure** - there is none

The AI provider's data handling policies apply. Review [OpenAI's terms](https://openai.com/policies) or [GitHub Copilot's terms](https://docs.github.com/en/copilot) as applicable.

## Manual Secret Mode

If you don't want the CLI to upload secrets automatically:

```bash
intelligencex setup wizard --manual-secret
```

The CLI prints the base64-encoded auth bundle for you to paste into GitHub secrets manually.

## Best Practices

1. **Use encrypted local storage**: Set `INTELLIGENCEX_AUTH_KEY` environment variable
2. **Limit repo access**: Use fine-grained GitHub App permissions
3. **Rotate tokens**: Refresh auth tokens periodically with `intelligencex auth login`
4. **Review workflow changes**: All setup creates PRs, never direct commits
5. **Monitor usage**: Run `intelligencex usage --events` to track AI provider consumption

## Open Source

IntelligenceX is fully open source under the MIT license. You can:

- [Review all source code](https://github.com/EvotecIT/IntelligenceX)
- Audit security practices
- Fork and customize
- Contribute improvements
