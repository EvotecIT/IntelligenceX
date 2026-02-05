# Analyze Command

The `analyze` command manages static analysis assets and exports analyzer configurations from `reviewer.json`.

## Run analysis

```bash
intelligencex analyze run --config .intelligencex/reviewer.json --out artifacts
```

This command:
- Loads packs and overrides from `reviewer.json`.
- Runs C# analysis through `dotnet build` and emits SARIF (`artifacts/intelligencex.roslyn.sarif`).
- Runs PowerShell analysis through PSScriptAnalyzer (if available) and emits IntelligenceX findings JSON
  (`artifacts/intelligencex.findings.json`).
- Applies `configMode` during the run without committing analyzer config files.

Optional flags:
- `--config <path>`: explicit path to `.intelligencex/reviewer.json`.
- `--workspace <path>`: repository root for catalog/config discovery.
- `--out <dir>`: output directory for SARIF/findings JSON (default: `artifacts`).
- `--dotnet-command <path>`: override `dotnet` executable path.
- `--pwsh-command <path>`: override `pwsh` executable path.
- `--strict`: return non-zero exit code if any analyzer runner fails.

## Export analyzer configs

```bash
intelligencex analyze export-config --out artifacts/analysis-config
```

Optional flags:
- `--config <path>`: explicit path to `.intelligencex/reviewer.json`.
- `--workspace <path>`: repository root for catalog discovery.

## List packs

```bash
intelligencex analyze list-packs
```

## List rules

```bash
intelligencex analyze list-rules
```
