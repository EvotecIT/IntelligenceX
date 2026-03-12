---
title: Reviewer Static Analysis
description: See how IntelligenceX static analysis is configured, onboarded, and gated through reviewer.json and curated rule packs.
---

# Static Analysis

This document describes the current IntelligenceX static analysis model and onboarding flow. Decisions stay centralized in `.intelligencex/reviewer.json`, and analyzer config files are not committed by default.

## Goals
- Provide curated rule packs with hundreds of rules enabled out of the box.
- Keep analysis decisions in one place (`reviewer.json`).
- Avoid modifying user repos unless explicitly requested.
- Allow easy opt-in, opt-out, and per-rule toggles with descriptions.
- Support multiple languages over time (C#, PowerShell, JS/TS, Python), with internal maintainability checks also covering Shell and YAML source files.

## User Experience (Onboarding)
- The wizard offers a single toggle: "Enable static analysis (recommended)."
- Users choose pack defaults from curated options (default `all-50`) or enter custom pack IDs.
- Users can optionally enable analysis gating ("Fail CI on static analysis findings?").
- Users can optionally enable strict runner behavior (`--analysis-run-strict true`) to fail on analyzer runner/tool execution errors.
- Users can optionally set an analyzer export path for IDE support.
- The wizard writes the `analysis` section into `.intelligencex/reviewer.json`.
- CLI parity: `intelligencex setup --analysis-*` flags are accepted only for preset generation (`--with-config` without `--config-json/--config-path` override), and gate/strict/packs/export require `--analysis-enabled true`.

## Source Of Truth
All enablement decisions live in `.intelligencex/reviewer.json`. Analyzer tool configs are generated temporarily during analysis runs and deleted afterward. No config files are committed by default.

## Configuration (reviewer.json)
```json
{
  "analysis": {
    "enabled": true,
    "packs": ["all-50"],
    "disabledRules": ["CA2000"],
    "severityOverrides": { "CA1062": "error" },
    "configMode": "respect",
    "run": {
      "strict": false
    },
    "hotspots": {
      "show": true,
      "maxItems": 10,
      "statePath": ".intelligencex/hotspots.json",
      "showStateSummary": true,
      "alwaysRender": false
    },
    "results": {
      "inputs": ["artifacts/**/*.sarif", "artifacts/intelligencex.findings.json"],
      "minSeverity": "warning",
      "maxInline": 20,
      "summary": true,
      "showPolicy": true,
      "policyRulePreviewItems": 10
    }
  }
}
```

## Config Mode (Respect Existing Repo Rules)
`configMode` defines how the analysis runner interacts with any existing analyzer configs in the repo.

- `respect` (default): If the repo already has analyzer configs, do not override them. Use them as-is and only filter findings based on `reviewer.json`.
- `overlay`: Merge pack rules on top of existing repo configs during the analysis run. No files are committed.
- `replace`: Ignore repo configs and use only pack rules during the analysis run. No files are committed.

## Rule Catalog (One File Per Rule)
Each rule has a metadata file with descriptions and mapping to the underlying analyzer rule ID. This powers the GUI/CLI toggles.

Catalog layout:
- `Analysis/Catalog/rules/csharp/CA2000.json`
- `Analysis/Catalog/rules/powershell/PSAvoidUsingWriteHost.json`
- `Analysis/Catalog/rules/internal/IXLOC001.json`
- `Analysis/Catalog/rules/internal/IXTOOL001.json`
- `Analysis/Catalog/rules/internal/IXTOOL002.json`
- `Analysis/Catalog/rules/internal/IXTOOL003.json`
- `Analysis/Catalog/rules/internal/IXTOOL004.json`
- `Analysis/Catalog/rules/internal/IXTOOL005.json`
- `Analysis/Catalog/overrides/csharp/CA5350.json` (optional, IntelligenceX-specific overlay)

Example rule file:
```json
{
  "id": "CA2000",
  "language": "csharp",
  "tool": "Microsoft.CodeAnalysis.NetAnalyzers",
  "toolRuleId": "CA2000",
  "type": "bug",
  "title": "Dispose objects before losing scope",
  "description": "Ensures IDisposable instances are disposed to avoid leaks.",
  "category": "Reliability",
  "defaultSeverity": "warning",
  "tags": ["resource-management", "memory"]
}
```

### Rule Overrides (Our Style Layer)
Some catalogs (notably NetAnalyzers `CA*`) are regenerated from upstream metadata. To keep those files machine-owned while
still allowing IntelligenceX-specific classification, we support rule overrides under `Analysis/Catalog/overrides`.

Overrides can add/replace fields like `type`, `tags`, `docs`, and optional internal descriptions without changing the
generated rule file.

### Rule Types (Sonar-Style)
To align with how Sonar structures rule triage, we classify rules into one of these types:

- `bug`: likely correctness issue
- `vulnerability`: likely security issue with direct exploitability risk
- `code-smell`: maintainability/performance/style issues
- `security-hotspot`: security-sensitive pattern that requires human review/context

If `type` is not explicitly set, IntelligenceX infers a default based on `category` (for example `Security` -> `vulnerability`,
`Reliability` -> `bug`, `Maintainability` -> `code-smell`), and you can override this per-rule in `Analysis/Catalog/overrides`.

## Rule Packs
Packs are curated lists of rule IDs plus optional severity overrides.

Pack layout:
- `Analysis/Packs/csharp-default.json`
- `Analysis/Packs/powershell-default.json`
- `Analysis/Packs/javascript-default.json`
- `Analysis/Packs/python-default.json`
- `Analysis/Packs/intelligencex-maintainability-default.json`
- `Analysis/Packs/javascript-security-default.json`
- `Analysis/Packs/python-security-default.json`
- `Analysis/Packs/csharp-50.json`
- `Analysis/Packs/csharp-100.json`
- `Analysis/Packs/csharp-500.json`
- `Analysis/Packs/powershell-50.json`
- `Analysis/Packs/powershell-100.json`
- `Analysis/Packs/powershell-500.json`
- `Analysis/Packs/javascript-50.json`
- `Analysis/Packs/javascript-100.json`
- `Analysis/Packs/javascript-500.json`
- `Analysis/Packs/python-50.json`
- `Analysis/Packs/python-100.json`
- `Analysis/Packs/python-500.json`
- `Analysis/Packs/intelligencex-maintainability-50.json`
- `Analysis/Packs/intelligencex-maintainability-100.json`
- `Analysis/Packs/intelligencex-maintainability-500.json`
- `Analysis/Packs/all-50.json`
- `Analysis/Packs/all-100.json`
- `Analysis/Packs/all-500.json`
- `Analysis/Packs/all-security-50.json`
- `Analysis/Packs/all-security-100.json`
- `Analysis/Packs/all-security-500.json`
- `Analysis/Packs/all-multilang-50.json`
- `Analysis/Packs/all-multilang-100.json`
- `Analysis/Packs/all-multilang-500.json`
- `Analysis/Packs/all-multilang-default.json`
- `Analysis/Packs/all-default.json`

Example pack:
```json
{
  "id": "csharp-default",
  "label": "C# Default",
  "includes": [],
  "rules": ["CA2000", "CA1062", "SA1600"],
  "severityOverrides": { "CA1062": "error" }
}
```

Packs can include other packs to build tiers without duplicating rule lists:

```json
{
  "id": "all-default",
  "label": "All Default",
  "includes": ["all-50"],
  "rules": []
}
```

Recommended tier selection:
- `all-50`: baseline/default onboarding tier.
- `all-security-50|100|500`: security-focused cross-language tiers (`all-security-default` remains a compatibility alias; prefer `all-security-50` for onboarding). Tier IDs are stable and higher tiers may initially resolve to the same rule set until catalog coverage expands.
- `all-multilang-50`: baseline for mixed-language repositories that include JavaScript/TypeScript and Python in addition to C#/PowerShell.
- `all-100`: broader coverage with higher review noise.
- `all-500`: strict tier for mature repositories and dedicated cleanup cycles.
- `all-multilang-100|500`: broader/strict mixed-language tiers.
- For language-specific rollouts, you can still add `javascript-50|100|500` and/or `python-50|100|500` explicitly to `analysis.packs`.

The built-in catalog now contains hundreds of C# rules plus PowerShell, JavaScript, Python, and internal rules, and
tier IDs remain stable for policy compatibility as coverage evolves.

### Internal Maintainability Language Coverage
Internal maintainability rules (`IXLOC001`, `IXDUP001`) scan tracked source extensions directly and currently include:
- C# (`.cs`)
- PowerShell (`.ps1`, `.psm1`, `.psd1`)
- JavaScript/TypeScript (`.js`, `.jsx`, `.mjs`, `.cjs`, `.ts`, `.tsx`, `.mts`, `.cts`)
- Python (`.py`, `.pyi`)
- Shell (`.sh`, `.bash`, `.zsh`)
- YAML (`.yml`, `.yaml`)

This means oversized-file and duplication checks apply to Shell/YAML repositories even when no external analyzer runner is configured for those languages.

## Temporary Analyzer Config Generation
During analysis runs, configs are generated or synthesized at runtime and cleaned up at the end. Examples:
- C#: `.editorconfig` with `dotnet_diagnostic.<rule>.severity` entries.
- PowerShell: `PSScriptAnalyzerSettings.psd1` with per-rule severities.
- JS/TS: ESLint CLI `--rule <toolRuleId>:<severity>` arguments are built from selected catalog rules.
- Python: Ruff `--select <toolRuleId,...>` is built from selected catalog rules.

`intelligencex analyze run` executes analysis for configured packs and emits findings artifacts for the reviewer.
Set `analysis.run.strict=true` in `.intelligencex/reviewer.json` to fail the command on tool runner errors.
You can force strict behavior per run with `intelligencex analyze run --strict` (or `--strict true`),
and force non-strict behavior with `intelligencex analyze run --strict false`.
You can override configured packs per run with `intelligencex analyze run --pack <id>` or `--packs <id1,id2>`.
`intelligencex analyze export-config` remains available for teams that explicitly want committed analyzer configs for IDE support.
`intelligencex analyze validate-catalog` validates rules/packs integrity (duplicates, bad refs, cycles, invalid severities).
`intelligencex analyze list-rules` prints the built-in rule inventory (`--format text|markdown|json`) and supports pack-scoped views (`--pack` / `--packs`).

Current built-in runners in `analyze run`:
- C#: Roslyn via `dotnet build` (SARIF output).
- PowerShell: PSScriptAnalyzer via `pwsh` (IntelligenceX findings JSON output).
- JS/TS: ESLint via `npx` when JavaScript/TypeScript rules are selected (SARIF output).
  - ESLint severity mapping is normalized to ESLint's 3-level model: `critical|error|high -> error`, `warning|warn|medium|info|information|low|suggestion -> warn`, `none -> off`.
- Python: Ruff via `ruff` when Python rules are selected (SARIF output).
- External runners are source-aware: if a language has no matching source files in the workspace, that runner is skipped with a warning.
- When a runner command is unavailable, `analyze run` reports explicit override guidance (`--dotnet-command`, `--pwsh-command`, `--npx-command`, `--ruff-command`).
- Internal: IntelligenceX maintainability checks (for example `IXLOC001`).
  - `IXLOC001` reads `max-lines:<n>` rule tags (default `700`) and supports configurable generated suffix tags (`generated-suffix:<value>`), generated header marker tags (`generated-marker:<value>`), optional generated header scan depth tags (`generated-header-lines:<n>`, `0` disables header scanning), additional excluded directory segments (`exclude-dir:<segment>`), and explicit exact-file relative path exclusions (`exclude-path:<relative/file/path>`).
  - `IXDUP001` measures per-file duplicated significant-line percentage and supports `max-duplication-percent:<0-100>` (default `25`), `dup-window-lines:<n>` (default `8`), and optional language-specific thresholds `max-duplication-percent-<language>:<0-100>` (`language`: `csharp|powershell|javascript|typescript|python|shell|yaml` plus short aliases `cs|ps|js|ts|py|sh|bash|zsh|yml`; canonical key is `yaml` with `yml` as alias).
    - Example tags: `["dup-window-lines:6", "max-duplication-percent-shell:20", "max-duplication-percent-yml:15"]`
  - Shell and YAML tokenization strips shebang/comment-only noise before computing significant lines to reduce false-positive duplication from shared headers.
  - `IXTOOL001` checks write-capable `ToolDefinition` registrations under `IntelligenceX.Tools/**` and flags schemas that do not use `WithWriteGovernanceDefaults()` or `WithWriteGovernanceAndAuthenticationProbe()`.

Advanced environment knobs:
- `INTELLIGENCEX_ANALYSIS_SOURCE_SCAN_MAX_FILES` sets shared source-inventory scan cap before per-language fallback (default `200000`).
- `INTELLIGENCEX_ANALYSIS_COMMAND_UNAVAILABLE_MARKERS` and `INTELLIGENCEX_ANALYSIS_COMMAND_UNAVAILABLE_MARKERS_<TOOL>` add custom unavailable-command markers (comma/semicolon/newline-separated).
  - `IXTOOL002` checks AD `ToolDefinition` registrations with required `domain_name` and flags tools that do not use canonical required-domain helper paths.
  - `IXTOOL003` checks tool source files under `IntelligenceX.Tools/**` and flags direct `max_results` metadata writes (for example `meta.Add("max_results", ...)` or `meta["max_results"] = ...`) instead of `AddMaxResultsMeta(...)`.
  - `IXTOOL004` checks tool source files under `IntelligenceX.Tools/**` (excluding `IntelligenceX.Tools/IntelligenceX.Tools.Tests/**` and `IntelligenceX.Tools/IntelligenceX.Tools.Common/ToolArgs.cs`) and flags legacy `ToolArgs.GetPositiveOptionBoundedInt32OrDefault(...)` usage instead of the canonical `ToolArgs.GetOptionBoundedInt32(...)` overload with explicit non-positive behavior.
  - `IXTOOL005` checks EventLog tool source files under `IntelligenceX.Tools/IntelligenceX.Tools.EventLog/**` and flags ambiguous `max_results` helper paths (`ResolveBoundedOptionLimit(..., "max_results", ...)` and `ResolveMaxResults(...)`) instead of explicit `ResolveOptionBoundedMaxResults(...)` or `ResolveCappedMaxResults(...)`.
  - Internal maintainability checks support `include-ext:<extension>` tags to scope analyzed file extensions per rule (default: `.cs`, `.ps1`, `.psm1`, `.psd1`, `.js`, `.jsx`, `.mjs`, `.cjs`, `.ts`, `.tsx`, `.mts`, `.cts`, `.py`, `.pyi`, `.sh`, `.bash`, `.zsh`, `.yml`, `.yaml`).
  - Generated marker/suffix defaults are defined in rule catalog tags (for example `Analysis/Catalog/rules/internal/IXLOC001.json`, `Analysis/Catalog/rules/internal/IXDUP001.json`, `Analysis/Catalog/rules/internal/IXTOOL001.json`, `Analysis/Catalog/rules/internal/IXTOOL002.json`, `Analysis/Catalog/rules/internal/IXTOOL003.json`, `Analysis/Catalog/rules/internal/IXTOOL004.json`, and `Analysis/Catalog/rules/internal/IXTOOL005.json`).
  - Unknown or malformed maintainability tags are ignored with explicit warnings in `analyze run` output.
  - Tag warnings are aggregated per prefix/type to avoid log spam on large tag sets.
  - Generated suffix and marker tags are additive; defaults remain enabled unless you disable the rule.

Review comments now include analysis execution context and outcomes even when no findings are present:

```text
### Static Analysis Policy 🧭
- Config mode: respect
- Packs: All Essentials (50)
- Rules: 52 enabled
- Rule list display: up to 10 items per section
- Enabled rules preview: CA2000 (Dispose objects before losing scope), CA1062 (Validate arguments of public methods), SA1600 (Elements should be documented), CA1000 (Do not declare static members on generic types), CA1001 (Types that own disposable fields should be disposable), CA1010 (Generic interface should also be implemented), CA1016 (Mark assemblies with assembly version), CA1018 (Mark attributes with AttributeUsageAttribute), CA1036 (Override methods on comparable types), CA1041 (Provide ObsoleteAttribute message), ... (truncated)
- Result files: 2 input patterns, 2 matched, 2 parsed, 0 failed
- Status: pass
- Rule outcomes: 0 with findings, 52 clean
- Failing rules: none
- Clean rules: CA2000 (Dispose objects before losing scope), CA1062 (Validate arguments of public methods), SA1600 (Elements should be documented), CA1000 (Do not declare static members on generic types), CA1001 (Types that own disposable fields should be disposable), CA1010 (Generic interface should also be implemented), CA1016 (Mark assemblies with assembly version), CA1018 (Mark attributes with AttributeUsageAttribute), CA1036 (Override methods on comparable types), CA1041 (Provide ObsoleteAttribute message), ... (truncated)
- Outside-pack rules: none

### Static Analysis 🔎
- Findings: 0 (no issues at or above configured severity)
```

For manual `workflow_dispatch` runs, `.github/workflows/review-intelligencex.yml` supports an `analysis_run_strict` input:
- `true`: runs `analyze run --strict`
- `false`: runs `analyze run --strict false`
- empty: uses `analysis.run.strict` from config
It also supports `analysis_packs` (CSV), which maps to `analyze run --packs <value>`.

`Result files` counters are defined as:
- `matched`: unique files resolved from `analysis.results.inputs`.
- `parsed`: non-empty matched files that were successfully parsed as findings/SARIF payloads (including valid payloads that produce zero findings).
- `failed`: matched files that failed during read/load/parse (including access-denied and malformed payload cases).

If static-analysis load fails at review time, the reviewer renders an unavailable block instead of silently omitting analysis output.
- If `analysis.results.showPolicy` is enabled, policy includes `Status: unavailable`.
- If `analysis.results.summary` is enabled, summary includes `Findings: unavailable`.

Rule preview lines (`Enabled rules preview`, `Failing rules`, `Clean rules`, `Outside-pack rules`) are deterministic and
show up to `analysis.results.policyRulePreviewItems` items (default `10`, max `500`), appending `(truncated)` when more rules exist.
Set `policyRulePreviewItems` to `0` to hide per-rule lists and keep counts only.
If your enabled rule count is less than or equal to the configured limit, the policy effectively shows all enabled rules.
When `analysis.gate.ruleIds` is configured, the policy also includes:
- `Gate rule IDs`: explicit gate-targeted rule IDs.
- `Gate rule outcomes`: per-rule finding counts for those gate-targeted rule IDs.

Teams can still produce SARIF with their preferred external tools and include those files in `analysis.results.inputs`.

## Migration Note
If you enable `intelligencex-maintainability-default` in an existing repository, expect new warnings for large source files.
`IXDUP001` defaults to `info` severity, so it is visible in findings but does not fail warning-level gates unless you raise severity.
`IXTOOL001` defaults to `warning` severity and is intended to keep new write-capable tools aligned with canonical schema helpers.
`IXTOOL002` defaults to `warning` severity and is intended to keep required-domain AD tools aligned with canonical request helper paths.
`IXTOOL003` defaults to `warning` severity and is intended to keep `max_results` metadata writes centralized through `AddMaxResultsMeta(...)`.
`IXTOOL004` defaults to `warning` severity and is intended to keep option-bounded max-results normalization on the canonical helper path.
`IXTOOL005` defaults to `warning` severity and is intended to keep EventLog `max_results` helper semantics explicit and stable.
To gate specific contract rules without widening gate types, set `analysis.gate.ruleIds` (for example `["IXTOOL001","IXTOOL002","IXTOOL003","IXTOOL004","IXTOOL005"]`).
Explicit gate rule IDs are still evaluated even when `analysis.gate.includeOutsidePackRules` is `false`.
When gate output reports `Outside-pack findings: ... (included/ignored)`, those included/ignored counts are scoped to findings that remain in gate scope after type/ruleId filtering.
Use `analysis.disabledRules` or `analysis.severityOverrides` in `.intelligencex/reviewer.json` to phase in enforcement.
IntelligenceX does not push analysis configuration into existing user repositories; policy only changes when the repository configuration is updated explicitly.

## Duplication Gate
`intelligencex analyze run` writes `artifacts/intelligencex.duplication.json` (schema `intelligencex.duplication.v2`) alongside findings.
`intelligencex analyze gate` can enforce duplication thresholds via `analysis.gate.duplication`:
- `enabled`: enable duplication gate checks.
- `metricsPath`: duplication metrics file path (default `artifacts/intelligencex.duplication.json`).
- `ruleIds`: duplication rule IDs to evaluate (default `IXDUP001`).
- `maxFilePercent`: optional per-file threshold override (else each rule's configured max is used).
- `maxFilePercentIncrease`: optional allowed per-file increase in percentage points compared to baseline snapshots (requires baseline file).
- `maxOverallPercent`: optional overall threshold for each evaluated rule.
- `maxOverallPercentIncrease`: optional allowed overall increase in percentage points compared to baseline snapshots (requires baseline file).
- `scope`: `changed-files` (default) or `all` for duplication gate evaluation scope.
- `newIssuesOnly`: apply baseline suppression to duplication violations.
- `failOnUnavailable`: fail or skip when duplication metrics are unavailable.

Example:

```json
{
  "analysis": {
    "gate": {
      "enabled": true,
      "baselinePath": ".intelligencex/analysis-baseline.json",
      "duplication": {
        "enabled": true,
        "metricsPath": "artifacts/intelligencex.duplication.json",
        "ruleIds": ["IXDUP001"],
        "maxFilePercent": 30,
        "maxFilePercentIncrease": 5,
        "maxOverallPercent": 25,
        "maxOverallPercentIncrease": 2,
        "scope": "changed-files",
        "newIssuesOnly": true,
        "failOnUnavailable": true
      }
    }
  }
}
```

## Workflow Integration (Example)
Analysis runs before review and publishes findings as artifacts. The reviewer reads those artifacts and merges findings into the summary and optional inline comments.

```yaml
jobs:
  analysis:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - name: Validate analysis catalog
        run: dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- analyze validate-catalog --workspace .
      - name: Run static analysis
        run: dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- analyze run --config .intelligencex/reviewer.json --out artifacts --framework net8.0
      - name: Compute changed files
        run: dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- ci changed-files --workspace . --out artifacts/changed-files.txt
      - name: Static analysis gate
        # Gate reads findings from analysis.results.inputs in reviewer.json
        # (defaults include artifacts/**/*.sarif and artifacts/intelligencex.findings.json).
        run: dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- analyze gate --config .intelligencex/reviewer.json --workspace . --changed-files artifacts/changed-files.txt
      - name: Upload analysis artifacts
        uses: actions/upload-artifact@v4
        with:
          name: ix-analysis
          path: artifacts

  review:
    needs: [analysis]
    runs-on: ubuntu-latest
    permissions:
      contents: read
      pull-requests: write
      issues: write
      id-token: write
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - name: Download analysis artifacts
        uses: actions/download-artifact@v4
        with:
          name: ix-analysis
          path: artifacts
      - name: Run reviewer
        run: dotnet run --project IntelligenceX.Reviewer/IntelligenceX.Reviewer.csproj -c Release -f net8.0
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
          INTELLIGENCEX_AUTH_B64: ${{ secrets.INTELLIGENCEX_AUTH_B64 }}
          INPUT_REPO: ${{ github.repository }}
          INPUT_PR_NUMBER: ${{ github.event.pull_request.number }}
```

## Reviewer Behavior
- The reviewer reads `analysis.results.inputs` for JSON or SARIF findings.
- Findings are merged into the existing summary format and structured findings block.
- Inline comments follow `maxInline` and severity thresholds.
 - When `showPolicy` is enabled, a policy block lists packs, counts, and overrides.

## Future Extensions
- Language detection and auto-pack selection.
- Rule catalogs synced from upstream analyzer metadata.
- UI for per-rule toggles and severity edits.
- Per-repo pack overrides without local config files.
- Security hotspots with first-class output:
  - Separate `### Security Hotspots 🔥` section (distinct from findings severities).
  - Persistent triage state keyed by (ruleId + fingerprint) stored under `.intelligencex/` (for example `ToReview`, `Safe`, `Fixed`).
  - Reviewer output surfaces hotspots even when `minSeverity` filters out `info`.
  - Manage state via CLI helpers:
    - `intelligencex analyze hotspots sync-state` to add missing keys (deterministic merge).
    - `intelligencex analyze hotspots set` to mark hotspots as `safe`, `fixed`, `accepted-risk`, `wont-fix`, or `suppress`.
- Rule fixability metadata (AI + deterministic fixes):
  - Mark rules as `fixable` (safe mechanical change) vs `ai-fixable` (LLM-assisted suggestion).
  - Use rule tags and/or override metadata to drive “suggested fix” text in inline comments.
