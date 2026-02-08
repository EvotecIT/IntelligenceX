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
- Runs built-in IntelligenceX maintainability checks for selected internal rules (for example `IXLOC001`
  for max file length).
- Applies `configMode` during the run without committing analyzer config files.

Optional flags:
- `--config <path>`: explicit path to `.intelligencex/reviewer.json`.
- `--workspace <path>`: repository root for catalog/config discovery.
- `--out <dir>`: output directory for SARIF/findings JSON (default: `artifacts`).
- `--dotnet-command <path>`: override `dotnet` executable path.
- `--framework <tfm>`: restrict C# analysis build to a target framework (for example `net8.0`).
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

## Gate (CI)

```bash
intelligencex analyze gate --config .intelligencex/reviewer.json --workspace .
```

This command:
- Loads the analysis catalog + policy from `reviewer.json`.
- Loads configured results inputs (SARIF and/or IntelligenceX findings JSON).
- Fails with exit code `2` when policy violations are detected or when results are unavailable (by default).

Optional flags:
- `--config <path>`: explicit path to `.intelligencex/reviewer.json`.
- `--workspace <path>`: repository root for catalog/config discovery.
- `--changed-files <path>`: newline-delimited list of workspace-relative paths to gate on (typically PR changed files).
