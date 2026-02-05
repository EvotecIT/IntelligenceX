# Analyze Command

The `analyze` command manages static analysis assets and exports analyzer configurations from `reviewer.json`.

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
