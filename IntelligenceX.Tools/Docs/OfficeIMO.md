# OfficeIMO Tool Pack

`IntelligenceX.Tools.OfficeIMO` provides read-only document ingestion through `OfficeIMO.Reader`.

## Included tools

- `officeimo_pack_info`
  - Returns capabilities, limits, and recommended flow.
- `officeimo_read`
  - Reads a single file or a folder and emits normalized chunks for model reasoning.

## Supported formats

- Word: `.docx`
- Excel: `.xlsx`
- PowerPoint: `.pptx`
- Markdown: `.md`

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
- `extensions`: optional extension allowlist.
- `max_files`, `max_total_bytes`, `max_input_bytes`: folder and file caps.
- `max_chars`: per-chunk character cap.
- `max_table_rows`: Excel table row cap.
- `excel_sheet_name`, `excel_a1_range`, `excel_headers_in_first_row`: Excel controls.
- `include_word_footnotes`: include Word footnotes.
- `include_ppt_notes`: include PowerPoint speaker notes.
- `markdown_chunk_by_headings`: chunk markdown by headings when possible.

## Output shape

`officeimo_read` returns a standard tool envelope with:

- `files`: ingested file list
- `chunks`: normalized chunk list (`id`, `kind`, `text`, optional `markdown`, `location`, `tables`, `warnings`)
- `warnings`: per-file warnings (skipped/unsupported/error)
- `truncated`: indicates cap-based truncation
- `meta`: includes `count`, `files`, and cap values
- `summary_markdown`: compact preview of extracted chunks

Use raw `chunks` fields for reasoning; treat `summary_markdown` as preview only.

## Build modes (`OFFICEIMO_ENABLED` vs `OFFICEIMO_DISABLED`)

The project is designed to compile even when `OfficeIMO.Reader` is not present.

- `OFFICEIMO_ENABLED`
  - Active when a local sibling `OfficeIMO.Reader.csproj` is found, or when a NuGet version is configured.
  - `officeimo_read` performs real extraction.
- `OFFICEIMO_DISABLED`
  - Active when no local/project/package reference can be resolved.
  - `officeimo_read` returns no chunks and reports that `OfficeIMO.Reader` is unavailable in this build.

This allows hosts to keep the pack registered while making dependency wiring explicit per environment.
