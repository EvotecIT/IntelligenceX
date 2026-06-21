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
pwsh ./Build/Advanced/Build-Reviewer.ps1
```

## Notes

- Release notes can run in CI via `.github/workflows/release-notes.yml`.
- Reviewer releases use `.github/workflows/release-reviewer.yml` and `Build/release.reviewer.json`.
- Published reviewer assets are replaced on the stable `reviewer-latest` release tag.
