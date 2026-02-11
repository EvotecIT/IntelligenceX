# FAQ

## Auth

### `auth login` opens a URL but nothing happens
Ensure the browser can reach the callback URL and try `--print` to paste the code manually.

### I see `No OpenAI auth bundle found`
Run `intelligencex auth login` or `intelligencex auth export --format store-base64` and add it as `INTELLIGENCEX_AUTH_B64`.

## GitHub

### Repos are missing in the wizard
Verify the token scope or ensure the GitHub App is installed on those repos.

### PRs were not created
Check that the GitHub token includes write permissions for `contents` and `pull_requests`.

## Reviewer

### Inline comments are missing
Ensure `review.mode` is `inline` or `hybrid` and the provider supports inline.

## CLI

### `intelligencex` is not found
Use `dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -c Release -- <command>`.
