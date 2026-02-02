# CLI Release Commands

## Release notes (CLI)

```powershell
intelligencex release notes --update-changelog
```

## Release notes (range)

```powershell
intelligencex release notes --from v0.1.0 --to v0.2.0 --version 0.2.0 --commit
```

## Release reviewer assets

```powershell
intelligencex release reviewer --tag reviewer-$(Get-Date -Format yyyyMMddHHmmss)
```

## Notes

- Release notes can run in CI via `.github/workflows/release-notes.yml`.
- Reviewer releases use `release-reviewer.yml` and build multi‑RID assets.
