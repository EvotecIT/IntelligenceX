---
title: Getting Started with IntelligenceX
description: Start with IntelligenceX using the reviewer wizard, IX Chat, .NET library, or PowerShell module, then expand into tools, Project Ops, and automation.
---

# Getting Started with IntelligenceX

Get up and running with the part of IntelligenceX you need first.
The fastest path is the reviewer onboarding flow, but the platform also includes IX Chat, tool packs, a .NET library, and a PowerShell module for local automation.

## Choose Your Starting Point

- Want AI reviews on pull requests? Start with the reviewer setup below.
- Want a local desktop experience? See [IX Chat Overview](/docs/chat/overview/) and [IX Chat Quickstart](/docs/chat/quickstart/).
- Want to embed IntelligenceX in an app? Start with [Library Overview](/docs/library/overview/).
- Want scriptable automation? Start with [PowerShell Overview](/docs/powershell/overview/).

## Prerequisites

- .NET 8.0 or later
- GitHub account with repository access
- One of:
  - ChatGPT / OpenAI access
  - Claude / Anthropic API key
  - GitHub Copilot access

## Option 1 - Reviewer CLI Wizard (Recommended)

The fastest way to set up IntelligenceX:

```bash
# Run from source
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -c Release -- setup wizard
```

The wizard guides you through:

1. Running preflight auto-detect (recommended): `intelligencex setup autodetect`
2. Choosing your path (new setup, fix expired auth, cleanup, maintenance)
3. Connecting to GitHub (OAuth, PAT, or your own GitHub App)
4. Selecting repositories for review
5. Choosing a provider/model pair from recommended quick picks or a custom model id, then a review preset (balanced, picky, security, minimal, etc.)
6. Creating a PR with the GitHub Actions workflow

See [Web Onboarding Flow](/docs/reviewer/web-onboarding/) for the canonical path/auth requirements matrix and Bot contract-check flow.

## Option 2 - Local Web UI (Preview)

For a visual setup experience:

```bash
intelligencex setup web
```

This starts a local web server (localhost only) with a step-by-step UI. See [Web Setup UI](/docs/reviewer/setup-web/) for details.

## Option 3 - Manual Setup

For full control:

```bash
# 1a. OpenAI path: authenticate with ChatGPT
intelligencex auth login

# 2a. Export the auth bundle
intelligencex auth export --format store-base64

# 3a. Add to GitHub as a secret named INTELLIGENCEX_AUTH_B64

# 1b. Claude path: provide ANTHROPIC_API_KEY in GitHub
# 2b. Create the workflow file
```

See [CLI Quick Start](/docs/cli/quickstart/) for the complete manual flow.

## What Happens After Reviewer Setup

Once the workflow is in place, IntelligenceX automatically reviews every PR:

- Inline comments on specific code lines with suggestions
- Summary review with overall assessment
- Hybrid mode combines both
- Auto-resolve cleans up stale bot threads

After merging your onboarding PR, run the [First PR Checklist](/docs/reviewer/first-pr-checklist/) on the next PR.

## Configuring the Reviewer

Create `.intelligencex/reviewer.json` in your repo:

```json
{
  "review": {
    "provider": "claude",
    "model": "claude-opus-4-1",
    "mode": "hybrid",
    "length": "long",
    "outputStyle": "compact"
  }
}
```

See [Reviewer Configuration](/docs/reviewer/configuration/) for all options.

If you want to decide where each setting should live, see [Workflow vs JSON](/docs/reviewer/workflow-vs-json/).

## Next Steps

- [Reviewer Overview](/docs/reviewer/overview/) - Understand review modes and output
- [IX Chat Overview](/docs/chat/overview/) - Run IntelligenceX locally with selectable runtimes and tool packs
- [IX Tools Overview](/docs/tools/overview/) - See the packs available to IX Chat and custom integrations
- [Security & Trust](/docs/security/) - Learn about the zero-trust model
- [CLI Commands](/docs/cli/overview/) - Full CLI reference
- [.NET Library](/docs/library/overview/) - Build custom integrations
- [PowerShell Overview](/docs/powershell/overview/) - Automate chat, threads, config, and diagnostics
- [Project Ops Overview](/docs/project-ops/overview/) - Use first-party issue and project workflows for triage
