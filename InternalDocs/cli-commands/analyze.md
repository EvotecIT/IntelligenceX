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
- Runs JS/TS analysis through ESLint (`npx eslint`) when JS/TS rules are selected and emits SARIF
  (`artifacts/intelligencex.eslint.sarif`).
- Runs Python analysis through Ruff (`ruff check`) when Python rules are selected and emits SARIF
  (`artifacts/intelligencex.ruff.sarif`).
- Runs built-in IntelligenceX maintainability checks for selected internal rules (for example `IXLOC001`
  for max file length, `IXDUP001` for duplicated significant-line percentage, and `IXTOOL001` for
  write-tool schema helper contract enforcement).
  - Internal maintainability checks can be scoped via `include-ext:<extension>` tags (default extensions:
    `.cs`, `.ps1`, `.psm1`, `.psd1`, `.js`, `.jsx`, `.mjs`, `.cjs`, `.ts`, `.tsx`, `.py`) and are applied per rule.
  - `IXDUP001` additionally supports `max-duplication-percent-<language>:<0-100>` tags
    (`csharp|powershell|javascript|typescript|python`, aliases `cs|ps|js|ts|py`) for language-specific duplication thresholds.
  - `IXTOOL001` flags write-capable `ToolDefinition` schemas under `IntelligenceX.Tools/**` that do not use
    `WithWriteGovernanceDefaults()` or `WithWriteGovernanceAndAuthenticationProbe()`.
- Emits duplication metrics sidecar JSON (`artifacts/intelligencex.duplication.json`, schema `intelligencex.duplication.v2`) for duplication gate checks.
- Applies `configMode` during the run without committing analyzer config files.

Optional flags:
- `--config <path>`: explicit path to `.intelligencex/reviewer.json`.
- `--workspace <path>`: repository root for catalog/config discovery.
- `--out <dir>`: output directory for SARIF/findings JSON (default: `artifacts`).
- `--dotnet-command <path>`: override `dotnet` executable path.
- `--framework <tfm>`: restrict C# analysis build to a target framework (for example `net8.0`).
- `--pwsh-command <path>`: override `pwsh` executable path.
- `--npx-command <path>`: override `npx` executable path used by ESLint runner.
- `--ruff-command <path>`: override `ruff` executable path.
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

Gate behavior is configured by `analysis.gate` in `.intelligencex/reviewer.json`, including:
- `minSeverity`: minimum severity to consider for gating.
- `types`: optional type filter (for example `vulnerability`, `bug`).
- `ruleIds`: optional explicit rule-ID filter (for example `IXTOOL001`). When both `types` and `ruleIds` are set, a finding is in-scope if it matches either filter.
- `includeOutsidePackRules`: when true, findings outside enabled packs can fail the gate.
- `failOnHotspotsToReview`: when true, security hotspots in `to-review` state can fail the gate.
- `newIssuesOnly` + `baselinePath`: baseline-aware finding gate mode.
- `duplication.enabled`: enables duplication gate checks from `artifacts/intelligencex.duplication.json`.
- `duplication.ruleIds`: duplication rule IDs to evaluate (default: `IXDUP001`).
- `duplication.maxFilePercent`: optional per-file duplication threshold override.
- `duplication.maxOverallPercent`: optional overall duplication threshold.
- `duplication.scope`: duplication evaluation scope (`changed-files` or `all`).
- `duplication.newIssuesOnly`: apply baseline/new-only suppression to duplication violations.
- `duplication.failOnUnavailable`: fail/passthrough behavior when duplication metrics are unavailable.

Optional flags:
- `--config <path>`: explicit path to `.intelligencex/reviewer.json`.
- `--workspace <path>`: repository root for catalog/config discovery.
- `--changed-files <path>`: newline-delimited list of workspace-relative paths to gate on (typically PR changed files).

## Duplication Benchmark (Maintainer Utility)

For repeatable duplication performance checks on synthetic repositories, use:

```bash
.agents/skills/intelligencex-analysis-gate/scripts/benchmark-duplication.sh
```

Optional environment variables:
- `FILES`: number of generated files (default `200`).
- `LINES`: repeated line count per file (default `120`).
- `LANGUAGE`: `csharp|powershell|javascript|typescript|python` (default `csharp`).
- `FRAMEWORK`: `dotnet run` framework for CLI (default `net8.0`).
- `KEEP_WORKDIR`: keep generated workspace (`1`) instead of deleting it.
- `WORKDIR`: custom workspace path (by default a temp directory is created).
