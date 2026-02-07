# CLI Setup Commands

## Wizard (recommended)

```powershell
intelligencex setup wizard
```

## Web UI (local preview)

```powershell
intelligencex setup web
```

## Single repo (non‑interactive)

```powershell
intelligencex setup --repo owner/name --with-config
```

## Managed workflow (release assets)

```powershell
intelligencex setup --repo owner/name --reviewer-source release --reviewer-release-repo EvotecIT/github-actions --reviewer-release-tag latest
```

## Update secret only

```powershell
intelligencex setup --repo owner/name --update-secret
```

## Manual secret mode

```powershell
intelligencex setup --repo owner/name --manual-secret
```

## Explicit secrets block

```powershell
intelligencex setup --repo owner/name --explicit-secrets
```

## Cleanup

```powershell
intelligencex setup --repo owner/name --cleanup --keep-secret
```

## Notes

- Default workflow path: `.github/workflows/review-intelligencex.yml`
- Default reviewer config path: `.intelligencex/reviewer.json`
- Setup uses PRs by default (safe review before merge).
- Use `--reviewer-source local` if you want to run the repo-local reviewer binary.
