# Static Analysis (Draft)

This document proposes how IntelligenceX can offer default static analysis rules and a first-class onboarding flow while keeping user repositories clean. The design keeps decisions centralized in `.intelligencex/reviewer.json` and avoids committing analyzer config files by default.

## Goals
- Provide curated rule packs with hundreds of rules enabled out of the box.
- Keep analysis decisions in one place (`reviewer.json`).
- Avoid modifying user repos unless explicitly requested.
- Allow easy opt-in, opt-out, and per-rule toggles with descriptions.
- Support multiple languages over time (C#, PowerShell, JS/TS, Python).

## User Experience (Onboarding)
- The wizard offers a single toggle: "Enable static analysis (recommended)."
- Users select one or more packs (for example `csharp-default`, `powershell-default`, `intelligencex-maintainability-default`).
- Users can optionally toggle individual rules and change severities.
- The wizard writes the `analysis` section into `.intelligencex/reviewer.json`.

## Source Of Truth
All enablement decisions live in `.intelligencex/reviewer.json`. Analyzer tool configs are generated temporarily during analysis runs and deleted afterward. No config files are committed by default.

## Proposed Configuration (reviewer.json)
```json
{
  "analysis": {
    "enabled": true,
    "packs": ["csharp-default", "powershell-default", "intelligencex-maintainability-default"],
    "disabledRules": ["CA2000"],
    "severityOverrides": { "CA1062": "error" },
    "configMode": "respect",
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

Proposed layout:
- `Analysis/Catalog/rules/csharp/CA2000.json`
- `Analysis/Catalog/rules/powershell/PSAvoidUsingWriteHost.json`
- `Analysis/Catalog/rules/internal/IXLOC001.json`

Example rule file:
```json
{
  "id": "CA2000",
  "language": "csharp",
  "tool": "Microsoft.CodeAnalysis.NetAnalyzers",
  "toolRuleId": "CA2000",
  "title": "Dispose objects before losing scope",
  "description": "Ensures IDisposable instances are disposed to avoid leaks.",
  "category": "Reliability",
  "defaultSeverity": "warning",
  "tags": ["resource-management", "memory"]
}
```

## Rule Packs
Packs are curated lists of rule IDs plus optional severity overrides.

Proposed layout:
- `Analysis/Packs/csharp-default.json`
- `Analysis/Packs/powershell-default.json`
- `Analysis/Packs/intelligencex-maintainability-default.json`
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
  "includes": ["csharp-default", "powershell-default", "intelligencex-maintainability-default"],
  "rules": []
}
```

## Temporary Analyzer Config Generation
During analysis runs, configs are generated to a temporary directory and cleaned up at the end. Examples:
- C#: `.editorconfig` with `dotnet_diagnostic.<rule>.severity` entries.
- PowerShell: `PSScriptAnalyzerSettings.psd1` with per-rule severities.
- JS/TS: `.eslintrc` or flat config with enabled rule IDs.
- Python: `ruff.toml` or `pyproject.toml` with rule selection.

`intelligencex analyze run` executes analysis for configured packs and emits findings artifacts for the reviewer.
`intelligencex analyze export-config` remains available for teams that explicitly want committed analyzer configs for IDE support.
`intelligencex analyze validate-catalog` validates rules/packs integrity (duplicates, bad refs, cycles, invalid severities).

Current built-in runners in `analyze run`:
- C#: Roslyn via `dotnet build` (SARIF output).
- PowerShell: PSScriptAnalyzer via `pwsh` (IntelligenceX findings JSON output).
- Internal: IntelligenceX maintainability checks (for example `IXLOC001`).
  - `IXLOC001` reads `max-lines:<n>` rule tags (default `700`) and supports configurable generated suffix tags (`generated-suffix:<value>`), generated header marker tags (`generated-marker:<value>`), optional generated header scan depth tags (`generated-header-lines:<n>`, `0` disables header scanning), and additional excluded directory segments (`exclude-dir:<segment>`).
  - Generated marker/suffix defaults are defined in the rule catalog tags (`Analysis/Catalog/rules/internal/IXLOC001.json`).
  - Unknown or malformed maintainability tags are ignored with explicit warnings in `analyze run` output.
  - Tag warnings are aggregated per prefix/type to avoid log spam on large tag sets.
  - Generated suffix and marker tags are additive; defaults remain enabled unless you disable the rule.

Review comments now include analysis execution context and outcomes even when no findings are present:

```text
### Static Analysis Policy 🧭
- Config mode: respect
- Packs: C# Default, PowerShell Default, IntelligenceX Maintainability
- Rules: 5 enabled
- Rule list display: up to 10 items per section
- Enabled rules preview: CA2000 (Dispose objects before losing scope), CA1822 (Mark members as static), PSAvoidUsingWriteHost (Avoid using Write-Host) (truncated)
- Result files: 2 input patterns, 2 matched, 2 parsed, 0 failed
- Status: pass ✅
- Rule outcomes: 0 with findings, 5 clean
- Failing rules: none
- Clean rules: CA2000 (Dispose objects before losing scope), CA1822 (Mark members as static), SA1600 (Elements should be documented), PSAvoidUsingWriteHost (Avoid using Write-Host), IXLOC001 (Source files should stay below 700 lines)
- Outside-pack rules: none

### Static Analysis 🔎
- Findings: 0 (no issues at or above configured severity)
```

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

For JS/TS and Python today, teams can still produce SARIF with their preferred tools and include those files in
`analysis.results.inputs`.

## Migration Note
If you enable `intelligencex-maintainability-default` in an existing repository, expect new warnings for large source files.
Use `analysis.disabledRules` or `analysis.severityOverrides` in `.intelligencex/reviewer.json` to phase in enforcement.
IntelligenceX does not push analysis configuration into existing user repositories; policy only changes when the repository configuration is updated explicitly.

## Workflow Integration (Example)
Analysis runs before review and publishes findings as artifacts. The reviewer reads those artifacts and merges findings into the summary and optional inline comments.

```yaml
jobs:
  review:
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
      - name: Run analysis
        run: dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- analyze run --config .intelligencex/reviewer.json --out artifacts --framework net8.0
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
