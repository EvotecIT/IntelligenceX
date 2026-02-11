# Getting Started with IntelligenceX

Get up and running with AI-powered code reviews in under a minute.

## Prerequisites

- .NET 8.0 or later
- GitHub account with repository access
- ChatGPT account or GitHub Copilot access

## Option 1 - CLI Wizard (Recommended)

The fastest way to set up IntelligenceX:

```bash
# Install the CLI
dotnet tool install --global IntelligenceX.Cli

# Run the setup wizard
intelligencex setup wizard
```

The wizard guides you through:

1. Authenticating with ChatGPT or Copilot (your own account)
2. Connecting to GitHub (OAuth, PAT, or your own GitHub App)
3. Selecting repositories for review
4. Choosing a review preset (balanced, picky, security, minimal, etc.)
5. Creating a PR with the GitHub Actions workflow

## Option 2 - Local Web UI (Preview)

For a visual setup experience:

```bash
intelligencex setup web
```

This starts a local web server (localhost only) with a step-by-step UI. See [Web Setup UI](/docs/reviewer/setup-web/) for details.

## Option 3 - Manual Setup

For full control:

```bash
# 1. Authenticate with ChatGPT
intelligencex auth login

# 2. Export the auth bundle
intelligencex auth export --format store-base64

# 3. Add to GitHub as a secret named INTELLIGENCEX_AUTH_B64
# 4. Create the workflow file
```

See [CLI Quick Start](/docs/cli/quickstart/) for the complete manual flow.

## What Happens After Setup

Once the workflow is in place, IntelligenceX automatically reviews every PR:

- Inline comments on specific code lines with suggestions
- Summary review with overall assessment
- Hybrid mode combines both
- Auto-resolve cleans up stale bot threads

After merging your onboarding PR, run the [First PR Checklist](./reviewer/first-pr-checklist.md) on the next PR.

## Configuring the Reviewer

Create `.intelligencex/reviewer.json` in your repo:

```json
{
  "review": {
    "provider": "openai",
    "model": "gpt-5.3-codex",
    "mode": "hybrid",
    "length": "long",
    "outputStyle": "claude"
  }
}
```

See [Reviewer Configuration](/docs/reviewer/configuration/) for all options.

## Next Steps

- [Reviewer Overview](/docs/reviewer/overview/) - Understand review modes and output
- [Security & Trust](/docs/security/) - Learn about the zero-trust model
- [CLI Commands](/docs/cli/overview/) - Full CLI reference
- [.NET Library](/docs/library/overview/) - Build custom integrations
