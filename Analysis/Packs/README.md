# Analysis Packs

Packs are curated rule sets built from rule IDs and optional includes/overrides.

## Conventions

- Keep one concern per file (one pack definition per JSON file).
- Prefer stable, explicit pack IDs (for example `csharp-50`, `all-100`).
- Use `includes` to compose tiers instead of duplicating large `rules` arrays.
- Keep language packs separate from cross-language aggregate packs.

## Built-in packs

- Language defaults:
  - `csharp-default`
  - `powershell-default`
  - `javascript-default`
  - `python-default`
  - `intelligencex-maintainability-default`
- Security defaults:
  - `csharp-security-default`
  - `powershell-security-default`
  - `javascript-security-default`
  - `python-security-default`
  - `all-security-default`
  - `all-security-50`, `all-security-100`, `all-security-500`
- Language tiers:
  - `csharp-50`, `csharp-100`, `csharp-500`
  - `powershell-50`, `powershell-100`, `powershell-500`
  - `javascript-50`, `javascript-100`, `javascript-500`
  - `python-50`, `python-100`, `python-500`
  - `intelligencex-maintainability-50`, `intelligencex-maintainability-100`, `intelligencex-maintainability-500`
- Cross-language tiers:
  - `all-50`, `all-100`, `all-500`
  - `all-multilang-50`, `all-multilang-100`, `all-multilang-500`
  - Note: `all-*` currently targets core tiers (`csharp-*`, `powershell-*`, `intelligencex-maintainability-*`); add `javascript-*` / `python-*` explicitly when desired.
  - Note: `all-multilang-*` extends `all-*` with JavaScript and Python tiers for broader mixed-language repositories.
- Compatibility alias:
  - `all-default` (currently includes `all-50`)
  - `all-security-default` (compatibility baseline; prefer `all-security-50` for tiered onboarding)
  - `all-multilang-default` (currently includes `all-multilang-50`)

## Notes on 50/100/500 tiers

Tier IDs are stable policy tiers.

- `*-50`: baseline onboarding tier
- `*-100`: broader coverage tier
- `*-500`: deep/strict coverage tier (capped at 500 entries)

For C#, tiers are generated from built-in NetAnalyzers metadata:

- `csharp-50` and `csharp-100` are seeded from `analysislevel_9_recommended.globalconfig`
- `csharp-500` extends to the full built-in C# catalog (up to 500)

Regenerate C# catalog + C# tiers with:

`./scripts/update_analysis_catalog.py --repo-root .`

For JavaScript and Python, current `*-50|100|500` tiers are intentionally stable aliases that expand as catalog coverage grows.

## JavaScript/Python catalog

JavaScript and Python built-in rules are curated starter packs intended to map high-signal ESLint and Ruff findings
into stable IntelligenceX rule IDs for gating/reporting.

## PowerShell catalog

PowerShell rules are sourced from `PSScriptAnalyzer` rule metadata. To (re)generate the built-in PowerShell rule catalog:

`pwsh -NoProfile -File ./scripts/sync-pssa-catalog.ps1`
