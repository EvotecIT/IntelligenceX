# CLI Reviewer Commands

## Run reviewer (local)

```powershell
$env:INPUT_REPO = "owner/name"
$env:INPUT_PR_NUMBER = "123"
intelligencex reviewer run
```

## Resolve bot threads

```powershell
intelligencex reviewer resolve-threads --repo owner/name --pr 123 --dry-run
intelligencex reviewer resolve-threads --repo owner/name --pr 123 --github-token $TOKEN
```

## Notes

- `reviewer run` reads inputs from environment variables or `GITHUB_EVENT_PATH`.
- `resolve-threads` supports `--bot` (repeatable), `--include-human`, `--include-current`, and `--api-base-url`.
- Thread auto‑resolve behavior is controlled via reviewer config settings.
