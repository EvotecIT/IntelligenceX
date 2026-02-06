# FAQ

## Auth

**Q: `auth login` opens a URL but nothing happens.**  
A: Ensure the browser can reach the callback URL and try `--print` to paste the code manually.

**Q: I see `No OpenAI auth bundle found`.**  
A: Run `intelligencex auth login` or `intelligencex auth export --format store-base64` and add it as `INTELLIGENCEX_AUTH_B64`.

## GitHub

**Q: Repos are missing in the wizard.**  
A: Verify the token scope or ensure the GitHub App is installed on those repos.

**Q: PRs were not created.**  
A: Check that the GitHub token has `contents: write` and `pull_requests: write` permissions.

## Reviewer

**Q: Inline comments are missing.**  
A: Ensure `review.mode` is `inline` or `hybrid` and the provider supports inline.

## CLI

**Q: `intelligencex` is not found.**  
A: Use `dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -c Release -- <command>`.
