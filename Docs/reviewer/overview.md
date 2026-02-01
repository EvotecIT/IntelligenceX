# Reviewer Overview

The reviewer runs in GitHub Actions and posts a structured review comment on PRs. It can use:
- ChatGPT (native transport) with a ChatGPT login bundle.
- Copilot (via Copilot CLI) for teams already using GitHub Copilot.

## Recommended onboarding
- CLI wizard: `intelligencex setup wizard`
- Local web UI (preview): `intelligencex setup web`

Related docs:
- `Docs/reviewer/onboarding-wizard.md`
- `Docs/reviewer/setup-web.md`
- `Docs/reviewer/configuration.md`
- `Docs/reviewer/security-trust.md`

## Trust model (short version)
- BYO GitHub App is supported for branded bot identity.
- Secrets are stored in GitHub Actions (you control access).
- Web UI binds to localhost only; tokens never leave your machine.

## Reusable workflow (quick start)

```yaml
jobs:
  review:
    uses: evotecit/github-actions/.github/workflows/review-intelligencex.yml@master
    with:
      reviewer_source: release
      openai_transport: native
      output_style: claude
      style: colorful
    secrets: inherit
```

## What to configure next
- Model/provider + output style
- Review length and strictness
- Auto-resolve/triage behavior for bot threads
- Usage summary line (optional)
