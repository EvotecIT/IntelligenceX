# Rule Overrides

Upstream-generated rule metadata (for example NetAnalyzers `CA*`) is stored under `Analysis/Catalog/rules`.

To keep those files machine-regeneratable while still allowing "IntelligenceX style" metadata, you can add rule
overrides under `Analysis/Catalog/overrides`.

## Layout

Mirror the `rules/` directory structure:

- `Analysis/Catalog/overrides/csharp/CA5350.json`
- `Analysis/Catalog/overrides/powershell/PSAvoidUsingWriteHost.json`
- `Analysis/Catalog/overrides/internal/IXLOC001.json`

## Format

Override files must include:

- `id`: rule id that exists in the base catalog.

Supported override fields (all optional):

- `type`: `bug` | `vulnerability` | `code-smell` | `security-hotspot`
- `tags`: list of strings; merged (union) with base rule tags
- `category`, `defaultSeverity`, `title`, `description`, `docs`: replace base values when provided

Notes:

- Overrides are intended to add classification, standards mapping (via tags), and internal docs, without modifying
  generated rule files.
