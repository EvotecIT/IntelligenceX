# Analysis Catalog

Rule metadata lives under `Analysis/Catalog/rules`. Each rule is one JSON file with a stable ID, description, and
mapping to the underlying analyzer rule ID.

Rule IDs are the primary key and file name (`CA2000.json`, `PSAvoidUsingWriteHost.json`).
Human-readable names belong in `title`.

Use packs under `Analysis/Packs` to enable curated sets of rules.

Pack files support:
- `rules`: direct rule IDs in the pack
- `includes`: pack IDs to include (resolved before the parent pack)
- `severityOverrides`: optional per-rule/per-tool-rule severity overrides

Validate catalog integrity before CI rollouts:

`intelligencex analyze validate-catalog --workspace <repo-root>`

List built-in rules (inventory):

- Plain text (default): `intelligencex analyze list-rules --workspace <repo-root>`
- Markdown table: `intelligencex analyze list-rules --workspace <repo-root> --format markdown`
- JSON: `intelligencex analyze list-rules --workspace <repo-root> --format json`

Filter inventory by packs (includes applied):

- `intelligencex analyze list-rules --workspace <repo-root> --pack all-50`
- `intelligencex analyze list-rules --workspace <repo-root> --packs csharp-100,powershell-100`

Regenerate built-in C# catalog metadata and C# tier packs from NetAnalyzers:

- `./scripts/update_analysis_catalog.py --repo-root .`
