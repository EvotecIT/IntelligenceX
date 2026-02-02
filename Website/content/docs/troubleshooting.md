---
title: Troubleshooting
description: Solutions for common IntelligenceX issues - authentication, rate limits, and workflow errors
collection: docs
layout: docs
---

# Troubleshooting

Common issues and their solutions.

## Authentication Errors

### "401 Unauthorized" from OpenAI

Your OpenAI session token has expired or is invalid.

1. Run `intelligencex auth login` to re-authenticate
2. Verify the secret is updated in your repository's GitHub Actions secrets
3. Check that the secret name matches what your workflow expects

### "403 Forbidden" from GitHub API

The GitHub App or token doesn't have sufficient permissions.

- Ensure your GitHub App has **Pull requests: Read & write** permission
- For organization repos, confirm the app is installed on the org (not just your personal account)
- Check the app's installation scope includes the target repository

### Copilot Authentication Fails

GitHub Copilot auth requires an active Copilot subscription.

1. Verify your subscription at [github.com/settings/copilot](https://github.com/settings/copilot)
2. Run `intelligencex auth login --provider copilot`
3. Complete the device flow in your browser

## Rate Limits

### OpenAI Rate Limit (429)

You've hit the provider's request limit.

- **Free tier**: Very low limits. Consider upgrading or switching to Copilot
- **Plus/Team**: Usually sufficient for small teams. Space out PR reviews if needed
- **API key users**: Check your [usage dashboard](https://platform.openai.com/usage)

The reviewer automatically retries with exponential backoff, but sustained rate limiting will cause the review to fail.

### GitHub API Rate Limit

GitHub's API allows 5,000 requests per hour for authenticated apps.

- Large PRs with many files may consume more API calls
- Check your remaining quota: `gh api rate_limit`
- If you hit limits often, consider reducing review scope with `excludePatterns`

## Workflow Issues

### Review Action Doesn't Trigger

The workflow must be triggered by `pull_request` events:

```yaml
on:
  pull_request:
    types: [opened, synchronize]
```

Common causes:
- Workflow file not on the default branch yet (first PR won't trigger itself)
- PR was opened by a GitHub App that can't trigger workflows (use `workflow_dispatch` as fallback)
- Repository has Actions disabled in settings

### Review Posts But Comments Are Empty

This usually means the model returned an empty or malformed response.

- Check the Actions log for the raw API response
- Try switching to a different model (`gpt-4o` is generally reliable)
- Ensure the PR diff isn't too large for the model's context window

### "Could not find reviewer binary"

The `reviewer_source: release` option downloads the binary from GitHub Releases.

- Check that the release exists and contains the correct binary for the runner OS
- If behind a corporate proxy, the download may fail silently
- Alternative: use `reviewer_source: build` to compile from source (requires .NET SDK)

## CLI Issues

### Wizard Fails to Create PR

The CLI creates a PR with the workflow file. If this fails:

1. Check you have push access to the target repository
2. Verify the branch name isn't already taken
3. Ensure the repo allows PRs (not archived or disabled)

### Web UI Doesn't Open

The `intelligencex setup web` command starts a local server.

- Default port is `5000`. If occupied, pass `--port 5001`
- On Windows, check if your firewall is blocking localhost connections
- Try opening `http://localhost:5000` manually in your browser

## Getting Help

If your issue isn't covered here:

1. Search [existing issues](https://github.com/EvotecIT/IntelligenceX/issues) on GitHub
2. Open a [new issue](https://github.com/EvotecIT/IntelligenceX/issues/new) with:
   - IntelligenceX version (`intelligencex --version`)
   - OS and .NET version
   - Relevant log output from GitHub Actions
