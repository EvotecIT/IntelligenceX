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
  - `intelligencex-maintainability-default`
- Language tiers:
  - `csharp-50`, `csharp-100`, `csharp-500`
  - `powershell-50`, `powershell-100`, `powershell-500`
  - `intelligencex-maintainability-50`, `intelligencex-maintainability-100`, `intelligencex-maintainability-500`
- Cross-language tiers:
  - `all-50`, `all-100`, `all-500`
- Compatibility alias:
  - `all-default` (currently includes `all-50`)

## Notes on 50/100/500 tiers

Tier names define target growth and policy intent, not a hard count today. Current catalogs may contain fewer rules.
As new rules are added, extend language tiers and aggregate tiers via `includes` without breaking existing pack IDs.
