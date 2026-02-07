# Analysis Catalog

Rule metadata lives under `Analysis/Catalog/rules`. Each rule is one JSON file with a stable ID, description, and
mapping to the underlying analyzer rule ID.

Use packs under `Analysis/Packs` to enable curated sets of rules.

Pack files support:
- `rules`: direct rule IDs in the pack
- `includes`: pack IDs to include (resolved before the parent pack)
- `severityOverrides`: optional per-rule/per-tool-rule severity overrides

Validate catalog integrity before CI rollouts:

`intelligencex analyze validate-catalog --workspace <repo-root>`
