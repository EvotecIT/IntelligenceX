# CLI Auth Commands

## Login (ChatGPT OAuth)

```powershell
intelligencex auth login
```

Print the URL (useful in CI or when the browser doesn’t open):

```powershell
intelligencex auth login --print
```

Export after login:

```powershell
intelligencex auth login --export store-base64 --print
```

Upload to GitHub Secrets (repo):

```powershell
intelligencex auth login --set-github-secret --repo owner/name --github-token $TOKEN
```

Upload to org secrets (all repos):

```powershell
intelligencex auth login --set-github-secret --org my-org --visibility all --github-token $TOKEN
```

Set a custom secret name:

```powershell
intelligencex auth login --set-github-secret MY_AUTH --repo owner/name --github-token $TOKEN
```

## Export (manual secret flow)

```powershell
intelligencex auth export --format store-base64
```

Filter to a specific account id (when multiple logins exist in the store):

```powershell
intelligencex auth export --format store-base64 --provider openai-codex --account-id <accountId>
```

## List saved logins

```powershell
intelligencex auth list
```

## Sync for Codex CLI

```powershell
intelligencex auth sync-codex
```

## Notes

- Auth store path: `~/.intelligencex/auth.json` (override with `INTELLIGENCEX_AUTH_PATH`)
- Encryption key: `INTELLIGENCEX_AUTH_KEY` (base64, 32 bytes, .NET 8+)
- `auth login` uses OAuth PKCE and a loopback redirect by default.
- GitHub token can come from `--github-token`, `INTELLIGENCEX_GITHUB_TOKEN`, `GITHUB_TOKEN`, or `GH_TOKEN`.
- Repo/org can be inferred from `GITHUB_REPOSITORY`, `INTELLIGENCEX_GITHUB_REPO`, or git remotes.
