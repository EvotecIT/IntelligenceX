---
title: IntelligenceX Troubleshooting
description: Troubleshoot common IntelligenceX issues across authentication, reviewer workflows, IX Chat runtimes, local providers, CLI setup, and self-hosted runners.
---

# Troubleshooting

Common IntelligenceX issues and fixes across reviewer onboarding, IX Chat runtimes, local providers, GitHub workflows, and CLI setup.

## Before You Dig In

- Confirm which surface is failing: reviewer workflow, IX Chat, CLI setup, local provider, or self-hosted runner.
- Re-run the closest guided flow first: [Getting Started](/docs/getting-started/), [Reviewer Overview](/docs/reviewer/overview/), or [IX Chat Overview](/docs/chat/overview/).
- If the issue involves credentials or trust boundaries, review [Security & Trust](/docs/security/).

## Authentication Errors

### 401 Unauthorized (OpenAI)

Your OpenAI session token is expired or invalid.

1. Run `intelligencex auth login`
2. Update repository secret (`INTELLIGENCEX_AUTH_B64`)
3. Confirm workflow reads the expected secret name

If the reviewer log or PR summary mentions `refresh_token_reused`, the stored ChatGPT auth bundle is stale and must be replaced:

```powershell
intelligencex auth login --set-github-secret --repo EvotecIT/IntelligenceX
```

### 403 Forbidden (GitHub API)

Token or app permissions are insufficient.

- Ensure app permission `Pull requests: Read & write`
- For org repos, confirm app install scope includes target repo
- Re-check token scopes if using PAT

### Copilot Authentication Fails

1. Verify Copilot access at [github.com/settings/copilot](https://github.com/settings/copilot)
2. In IX Chat, select **Use Copilot Subscription** (transport `copilot-cli`)
3. Use **Sign In** in the app and finish the browser flow
4. Click **Refresh Models** after login

Notes:
- `copilot-cli` uses subscription login, not API key fields.
- `compatible-http` + `https://api.githubcopilot.com/v1` is a different path and expects API-key style auth.

### Compatible HTTP (Local Providers)

Common issues when using `compatible-http` (local/self-hosted OpenAI-style endpoints):

- `http:// is not allowed by default`
  - Use `--openai-allow-insecure-http` for loopback (`localhost`, `127.0.0.1`)
  - Use `--openai-allow-insecure-http-non-loopback` only when you understand the risk
- `404 Not Found` on chat completions
  - Ensure your provider exposes `/v1/chat/completions`
  - If your base URL is `http://127.0.0.1:11434` IntelligenceX will normalize to `/v1/` internally
  - If your server is already mounted under `/v1`, you can also pass `http://127.0.0.1:11434/v1`
- Tools not being called
  - Your provider may ignore `tools` or not return `message.tool_calls`
  - Chat still works, but tool execution won't trigger

### Runtime Mode Is Confusing / Wrong Runtime Active

In **Options -> Runtime**, use this mapping:
- **Use ChatGPT Runtime** -> `native`
- **Use Copilot Subscription** -> `copilot-cli`
- **Use LM Studio Runtime** -> `compatible-http`

Then:
1. Open **Show Advanced Runtime** and confirm transport/base URL.
2. Click **Apply Runtime**.
3. Click **Refresh Models**.
4. Verify **Active runtime** badge text before sending prompts.

## Rate Limits

### OpenAI 429

- Free tiers have low limits
- Team plans are usually enough for moderate review volume
- For API key mode, monitor [usage dashboard](https://platform.openai.com/usage)

The reviewer retries transient failures, but sustained limits can still fail runs.

### GitHub API Rate Limit

- Authenticated apps typically get 5,000 requests/hour
- Large PRs can consume more requests
- Check remaining budget with `gh api rate_limit`

## Workflow Issues

### Workflow Does Not Trigger

Use `pull_request` triggers:

```yaml
on:
  pull_request:
    types: [opened, synchronize]
```

Common causes:

- Workflow file not on default branch yet
- Trigger source cannot start workflows
- Actions disabled in repository settings
- Required secrets or GitHub App settings are missing after setup

### Empty Review Comments

Usually means provider response was empty/malformed.

- Inspect job logs
- Try a different model
- Reduce oversized diffs

### Reviewer Binary Download Fails

If `reviewer_source: release` fails:

- Verify release asset exists for runner OS
- Check proxy/network policy on runner
- Fall back to `reviewer_source: build`

### Self-Hosted Runner "No space left on device"

If jobs fail during **Set up job** with `No space left on device`, the failure is runner infrastructure, not PR code.

Recommended setup:

1. Enable scheduled housekeeping workflow: `.github/workflows/runner-housekeeping.yml`
2. Keep at least ~20 GiB free on the self-hosted runner
3. Use emergency hosted-runner fallback by setting repository variable:
   - `IX_FORCE_GITHUB_HOSTED=true`
   - This routes key Linux jobs to `ubuntu-latest` until self-hosted capacity is restored

The following workflows honor `IX_FORCE_GITHUB_HOSTED`:

- `.github/workflows/review-intelligencex.yml`
- `.github/workflows/test-dotnet.yml`
- `.github/workflows/analysis-catalog-guardrail.yml`
- `.github/workflows/claude-code-review.yml`

## CLI Issues

### Wizard Cannot Create PR

1. Confirm push access
2. Check branch name collisions
3. Ensure repo is not archived/disabled

### Web UI Does Not Open

- Default is `http://localhost:5000`
- Use alternate port (`--port 5001`) if needed
- Check local firewall rules

## Getting Help

1. Search [existing issues](https://github.com/EvotecIT/IntelligenceX/issues)
2. Open a [new issue](https://github.com/EvotecIT/IntelligenceX/issues/new) with version, OS, and relevant logs
