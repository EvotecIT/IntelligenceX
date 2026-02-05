# CLI Reviewer Commands

## Run reviewer (local)

```powershell
$env:INPUT_REPO = "owner/name"
$env:INPUT_PR_NUMBER = "123"
intelligencex reviewer run
```

## Run reviewer (Azure DevOps)

```powershell
intelligencex reviewer run --provider azure --azure-org my-org --azure-project my-project --azure-repo my-repo --azure-token-env SYSTEM_ACCESSTOKEN
```

## Reviewer run options

- `--provider <openai|codex|copilot|azure>`
- `--provider-fallback <openai|codex|copilot>`
- `--code-host <github|azure>`
- `--azure-org <org>`
- `--azure-project <project>`
- `--azure-repo <repo>`
- `--azure-base-url <url>`
- `--azure-token-env <env>`

Notes:
- `--provider azure` (or any `--azure-*` flag) implies `--code-host azure` unless you override it explicitly.
- CLI flags override environment variables and `.intelligencex/reviewer.json`.

## Resolve bot threads

```powershell
intelligencex reviewer resolve-threads --repo owner/name --pr 123 --dry-run
intelligencex reviewer resolve-threads --repo owner/name --pr 123 --github-token $TOKEN
```

## Notes

- `reviewer run` reads inputs from environment variables or `GITHUB_EVENT_PATH`.
- `resolve-threads` supports `--bot` (repeatable), `--include-human`, `--include-current`, and `--api-base-url`.
- Thread auto‑resolve behavior is controlled via reviewer config settings.
