# Security and Trust Model

## Principles

- No backend service required for onboarding
- Secrets and tokens stay on the user's machine
- GitHub App path is BYO (bring your own) for org trust

## GitHub auth modes

- GitHub App installation token (recommended for orgs)
- OAuth device flow (fastest for single repo)
- Personal access token (policy-driven)

## OpenAI auth

- Uses ChatGPT OAuth (native transport)
- Secret stored in the repo or org as `INTELLIGENCEX_AUTH_B64`
- Optional `INTELLIGENCEX_AUTH_KEY` if you encrypt the local store

## Why local-only?

- GitHub and ChatGPT require explicit user consent.
- Running the wizard locally keeps tokens off any vendor backend.
- All changes are applied via PRs so you can review before merging.

## Manual secret mode

If you prefer not to upload secrets automatically, use manual secret mode:

```powershell
intelligencex setup --manual-secret
```

The CLI prints the base64 auth store for manual paste into GitHub secrets.

## What the tool changes

- Adds or updates `.github/workflows/review-intelligencex.yml`
- Optionally adds `.intelligencex/reviewer.json`

All changes are made via PRs by default.

## Dependabot PR limitation (GitHub Actions secrets)

For Dependabot PRs, GitHub typically does not expose repository secrets to workflows triggered by `pull_request`.
If your reviewer workflow relies on GitHub App secrets (app id/private key) to post as a branded bot identity, those
secrets may be unavailable on Dependabot PR runs. In that case the reviewer will fall back to `GITHUB_TOKEN`, and PR
comments will appear authored by `github-actions`.

## Secret handling options

- Auto-upload secrets via the CLI or wizard (`--set-github-secret`).
- Manual paste flow (`--manual-secret`) for maximum control.
- Explicit secrets block (`--explicit-secrets`) to avoid `secrets: inherit`.
