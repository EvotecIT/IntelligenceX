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
- Optionally adds `.intelligencex/config.json`

All changes are made via PRs by default.
