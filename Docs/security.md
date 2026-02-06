# Security and Trust Model

IntelligenceX is built on a zero-trust design. You do not have to trust us - you trust your own environment.

## Core Principles

### No Backend Service

IntelligenceX has no backend. Onboarding happens locally on your machine, and the reviewer runs in your GitHub Actions environment.

### Your Credentials, Your Control

You authenticate with your own ChatGPT or Copilot account:

- ChatGPT: OAuth login via browser, auth bundle stored as GitHub Actions secret
- Copilot: uses your existing GitHub Copilot access
- Optional encryption: set `INTELLIGENCEX_AUTH_KEY` to encrypt local auth storage

### Bring Your Own GitHub App

For organizations that need full control:

- Create a GitHub App under your organization
- Keep your own branding and permission scopes
- Keep your own audit trail in org settings

## GitHub Authentication Modes

| Mode | Best For | How It Works |
| --- | --- | --- |
| GitHub App (recommended) | Organizations | Install your own app for branded bot identity and fine-grained permissions |
| OAuth Device Flow | Single repos | Fast setup, no app required |
| Personal Access Token | Restricted environments | Policy-compliant manual token management |

## What the Tool Changes

IntelligenceX keeps repo changes minimal and reviewable:

- Adds `.github/workflows/review-intelligencex.yml`
- Optionally adds `.intelligencex/reviewer.json`
- Uses PRs by default for setup changes

## Data Flow

When the reviewer analyzes a PR:

1. GitHub Actions checks out your code diff
2. Diff is sent directly to your selected AI provider
3. AI response is posted as PR comments

No data passes through IntelligenceX infrastructure.

Provider policies still apply:

- [OpenAI policies](https://openai.com/policies)
- [GitHub Copilot docs](https://docs.github.com/en/copilot)

## Manual Secret Mode

If you do not want automatic secret upload:

```bash
intelligencex setup wizard --manual-secret
```

The CLI prints the base64 auth bundle for manual secret entry.

## Best Practices

1. Set `INTELLIGENCEX_AUTH_KEY` for encrypted local storage
2. Use least-privilege GitHub App permissions
3. Rotate tokens periodically (`intelligencex auth login`)
4. Keep setup changes in PRs
5. Monitor provider usage with `intelligencex usage --events`

## Open Source

IntelligenceX is open source (MIT):

- [Source code](https://github.com/EvotecIT/IntelligenceX)
- Reviewable security model
- Forkable and extensible
