# Troubleshooting

Common IntelligenceX issues and fixes.

## Authentication Errors

### 401 Unauthorized (OpenAI)

Your OpenAI session token is expired or invalid.

1. Run `intelligencex auth login`
2. Update repository secret (`INTELLIGENCEX_AUTH_B64`)
3. Confirm workflow reads the expected secret name

### 403 Forbidden (GitHub API)

Token or app permissions are insufficient.

- Ensure app permission `Pull requests: Read & write`
- For org repos, confirm app install scope includes target repo
- Re-check token scopes if using PAT

### Copilot Authentication Fails

1. Verify Copilot access at [github.com/settings/copilot](https://github.com/settings/copilot)
2. Install GitHub Copilot CLI (for example `brew install copilot-cli` or `winget install GitHub.Copilot`)
3. Run `copilot` and use the `/login` slash command (first run), then retry the reviewer

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
