# Tool Output Contract (Draft)

This repo standardizes tool outputs so `.Chat` can render them consistently (Markdown, tables, diagrams).

## Envelope

Every tool returns a JSON object (string) using an envelope:

- `ok`: boolean
- On success (`ok=true`):
  - tool-specific payload fields at the root (preferred today)
  - optional:
    - `meta`: object (counts/caps/UI helpers)
    - `render`: object (UI render hints)
    - `summary_markdown`: string (CommonMark/GFM-friendly)
- On error (`ok=false`):
  - `error_code`: string
  - `error`: string
  - optional:
    - `hints`: array of strings
    - `is_transient`: boolean

### Success Example

```json
{
  "ok": true,
  "count": 2,
  "truncated": false,
  "items": [
    { "name": "A" },
    { "name": "B" }
  ],
  "meta": {
    "schema_version": 1,
    "content_type": "application/vnd.intelligencex.tooloutput+json",
    "count": 2,
    "truncated": false,
    "preview_count": 2
  },
  "render": {
    "kind": "table",
    "rows_path": "items",
    "columns": [
      { "key": "name", "label": "Name", "type": "string" }
    ]
  },
  "summary_markdown": "..."
}
```

## Raw vs View Data

Use this rule across all packs:

- Preserve raw engine payload fields (arrays/objects) for model reasoning and cross-tool correlation.
- Treat projection arguments (`columns`, `sort_by`, `sort_direction`, `top`) as optional view-only shaping.
- Projection columns may be explicitly curated or auto-derived from typed engine row models.
- Emit projected display rows in dedicated `*_view` fields and point `render.rows_path` there.

Example:

- Raw data: `results`
- View data: `results_view`
- Render path: `render.rows_path = "results_view"`

### Error Example

```json
{
  "ok": false,
  "error_code": "access_denied",
  "error": "Path is outside AllowedRoots.",
  "hints": [
    "Adjust AllowedRoots to include the requested path.",
    "Use an absolute path inside an allowed root."
  ],
  "is_transient": false
}
```

## `meta` (UI + caps)

When a tool returns a list-like result, include `meta` using `IntelligenceX.Tools.Common.ToolOutputHints.Meta(...)`:

- `schema_version` (currently `1`)
- `content_type` (currently `application/vnd.intelligencex.tooloutput+json`)
- `count`: returned item count
- `truncated`: whether caps truncated results
- optional `scanned`: scanned/considered count
- optional `preview_count`: how many rows were included in `summary_markdown` preview

## `render` (UI hints)

Use `IntelligenceX.Tools.Common.ToolOutputHints.*` helpers:

- Table:
  - `kind=table`
  - `rows_path` points to an array of objects
  - `columns[]` describe keys, labels and types
- Code:
  - `kind=code`
  - `language` and `content_path`

Mermaid should be surfaced as either:
- `summary_markdown` with a fenced Mermaid block, or
- `render.kind=code` with `language=mermaid` pointing to Mermaid source.

## Pack Guidance Tools

Each pack should expose a `*_pack_info` tool with:

- engine/source-of-truth information
- setup expectations and limits
- recommended tool-call sequences (`recommended_flow`, `recommended_flow_steps`)
- capability catalog (`capabilities`)
- runtime-derived tool catalog (`tool_catalog`) with descriptions, required args, argument hints, and structured `traits` (projection/paging/time-range/dynamic-attributes/scoping/action flags)
- output-contract guidance (raw payload vs view projection)

Recommended startup pattern for agents:

1. Call the relevant `*_pack_info` tool first.
2. Plan tool sequence using pack guidance.
3. Use raw payload fields for reasoning.
4. Use `*_view` only when formatting output for the user.

## `summary_markdown` (OfficeIMO.MarkdownRenderer-friendly)

Keep summaries CommonMark/GFM-friendly:

- Headings: `#..######`
- Tables: pipe tables (GFM)
- Code blocks: fenced with language tags
- Mermaid: fenced blocks named `mermaid`
- Charts: fenced blocks named `chart` containing JSON (Chart.js), if enabled by the host renderer

Avoid:

- nested bullets (harder to render consistently across hosts)
- raw HTML (renderer defaults should treat chat output as untrusted)

## Helpers

Preferred helpers in `IntelligenceX.Tools.Common`:

- `ToolResponse.Ok(...)` / `ToolResponse.Error(...)`
- `ToolResponse.OkTablePreview(...)`
- `ToolOutputHints.Meta(...)`
- `ToolOutputHints.RenderTable(...)` / `RenderCode(...)` / `RenderMermaid(...)`
- `ToolMarkdown.*` and `MarkdownTable.Table(...)`
- `ToolMarkdownContract.Create()` / `ToolMarkdownDocument` for composable summaries
- `ToolTableViewEnvelope.TryBuildModelResponse(...)` for shared projection/view wrapper flow
- `ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(...)` to avoid per-tool `ViewColumns` boilerplate
- `ToolDynamicTableViewEnvelope.TryBuildModelResponseFromBags(...)` for dynamic dictionary/bag row projection
