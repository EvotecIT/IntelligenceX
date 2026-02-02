# Reviewer Overview

The reviewer runs in GitHub Actions and posts a structured review comment on PRs. It can use:
- ChatGPT (native transport) with a ChatGPT login bundle.
- Copilot (via Copilot CLI) for teams already using GitHub Copilot.

## Recommended onboarding
- CLI wizard: `intelligencex setup wizard`
- Local web UI (preview): `intelligencex setup web`

Related docs:
- `./onboarding-wizard.md`
- `./setup-web.md`
- `./configuration.md`
- `./security-trust.md`

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

## Inputs → environment mapping (short)

The reusable workflow maps `with:` inputs to environment variables the reviewer reads.

| Workflow input | Environment variable |
| --- | --- |
| `repo` | `INPUT_REPO` |
| `pr_number` | `INPUT_PR_NUMBER` |
| `reviewer_token` | `INTELLIGENCEX_GITHUB_TOKEN` |
| `reviewer_source` | `REVIEWER_SOURCE` |
| `reviewer_release_repo` | `REVIEWER_RELEASE_REPO` |
| `reviewer_release_tag` | `REVIEWER_RELEASE_TAG` |
| `reviewer_release_asset` | `REVIEWER_RELEASE_ASSET` |
| `reviewer_release_url` | `REVIEWER_RELEASE_URL` |

## Minimal config (native ChatGPT)

```json
{
  "review": {
    "provider": "openai",
    "openaiTransport": "native",
    "model": "gpt-5.2-codex",
    "mode": "inline",
    "length": "long",
    "reviewUsageSummary": true
  }
}
```

## Quick flow (end-to-end)

```powershell
# 1) Auth login (stores tokens locally)
intelligencex auth login

# 2) Setup reviewer (creates PR)
intelligencex setup wizard
```

## What to configure next
- Model/provider + output style
- Review length and strictness
- Auto-resolve/triage behavior for bot threads
- Usage summary line (optional)

## Usage and credits line

Enable `reviewUsageSummary` to append limits/credits (ChatGPT native only). See `./configuration.md`.
