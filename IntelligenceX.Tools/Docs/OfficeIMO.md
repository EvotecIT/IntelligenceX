# OfficeIMO Tool Pack

`IntelligenceX.Tools.OfficeIMO` provides read-only document ingestion through `OfficeIMO.Reader`.

## Included tools

- `officeimo_pack_info`
  - Returns capabilities, limits, and recommended flow.
- `officeimo_read`
  - Reads a single file or a folder and emits normalized chunks and/or source-level documents for reasoning/indexing.

## Supported formats

- Word: `.docx`
- Excel: `.xlsx`
- PowerPoint: `.pptx`
- Markdown: `.md`
- PDF: `.pdf`

## Safety model

- All paths are validated against `OfficeImoToolOptions.AllowedRoots`.
- Reads outside allowed roots return `access_denied`.
- Folder ingestion is bounded by:
  - `max_files` (runtime argument, capped by `OfficeImoToolOptions.MaxFiles`)
  - `max_total_bytes` (runtime argument, capped by `OfficeImoToolOptions.MaxTotalBytes`)
  - `max_input_bytes` (runtime argument, capped by `OfficeImoToolOptions.MaxInputBytes`)
- Folder traversal supports:
  - stable ordering (sorted paths)
  - optional recursion (`recurse`)
  - reparse-point skipping (junctions/symlinks) for safer traversal

## `officeimo_read` arguments

- `path` (required): file or folder path.
- `recurse`: recurse into subfolders when `path` is a folder.
- `extensions`: optional extension allowlist. When omitted, defaults to Office-focused formats (`.docx`, `.docm`, `.xlsx`, `.xlsm`, `.pptx`, `.pptm`, `.md`, `.markdown`, `.pdf`).
- `max_files`, `max_total_bytes`, `max_input_bytes`: folder and file caps.
- `max_chunks`: overall output chunk cap across all files.
- `max_chars`: per-chunk character cap.
- `max_table_rows`: Excel table row cap.
- `excel_sheet_name`, `excel_a1_range`, `excel_headers_in_first_row`: Excel controls.
- `include_word_footnotes`: include Word footnotes.
- `include_ppt_notes`: include PowerPoint speaker notes.
- `markdown_chunk_by_headings`: chunk markdown by headings when possible.
- `output_mode`: payload shape selector:
  - `chunks` (default): return flat `chunks`.
  - `documents`: return per-source `documents` payload.
  - `both`: return both flat `chunks` and `documents`.
- `include_document_chunks`: when `output_mode` includes `documents`, controls whether each document includes its `chunks` array (default: `true`).

## Output shape

`officeimo_read` returns a standard tool envelope with:

- `files`: ingested file list
- `chunks`: normalized chunk list (`id`, `kind`, `text`, optional `markdown`, `location`, `tables`, `warnings`, `source_id`, `source_hash`, `chunk_hash`, `source_last_write_utc`, `source_length_bytes`, `token_estimate`)
- `documents`: source-level list (`path`, `source_id`, `source_hash`, `parsed`, `chunks_produced`, `chunks_returned`, token totals, warnings, optional source chunks)
- `warnings`: per-file warnings (skipped/unsupported/error)
- `truncated`: indicates cap-based truncation
- counters: `files_scanned`, `files_parsed`, `files_skipped`, `bytes_read`, `chunks_produced`, `chunks_returned`, `token_estimate_returned`
- `meta`: includes `count`, caps, and output-shape counters
- `summary_markdown`: compact preview of extracted chunks

For incremental indexing, prefer `output_mode=documents` and upsert by `source_id`/`source_hash` + per-chunk `chunk_hash`.
Use `summary_markdown` as preview only.

## Build modes (`OFFICEIMO_ENABLED` vs `OFFICEIMO_DISABLED`)

The project is designed to compile even when `OfficeIMO.Reader` is not present.

- `OFFICEIMO_ENABLED`
  - Active when a local sibling `OfficeIMO.Reader.csproj` is found, or when a NuGet version is configured.
  - `officeimo_read` performs real extraction.
- `OFFICEIMO_DISABLED`
  - Active when no local/project/package reference can be resolved.
  - `officeimo_read` returns no chunks and reports that `OfficeIMO.Reader` is unavailable in this build.

This allows hosts to keep the pack registered while making dependency wiring explicit per environment.
