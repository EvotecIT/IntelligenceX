# Tool Authoring Playbook

Use this playbook when adding or refactoring tools in `IntelligenceX.Tools/**`.

## Goals
- Keep new tools short, predictable, and easy to review.
- Reuse shared helper paths before adding tool-specific helper code.
- Keep behavior contracts stable (`error_code`, metadata keys, table view shape).

## Shared Reuse Rules
1. Start from the package base class (`ActiveDirectoryToolBase`, `SystemToolBase`, `EventLogToolBase`, `FileSystemToolBase`, etc.).
2. Use `ToolBase.BuildAutoTableResponse(...)` for table envelopes instead of inline response shaping.
3. Use `ToolBase.AddMaxResultsMeta(...)` (directly or via package helper wrappers) for `max_results` metadata.
4. Prefer `ToolQueryHelpers` helpers (`CapRows`, projection filters) over handwritten list-cap/filter plumbing.
5. Add package-local helpers only when behavior is package-specific (for example AD domain parsing, collection contract mapping).

## Package-Specific Rules
- Active Directory tools:
  - Parse domains with `TryReadRequiredDomainName(...)`.
  - For conventional collection contracts, use `TryExecuteCollectionQuery(...)`.
  - For standard domain table flows, prefer `ExecuteDomainRowsViewTool(...)`.
  - For metadata, prefer `AddDomainAndMaxResultsMeta(...)` when applicable.
- System/EventLog/FileSystem tools:
  - Keep argument limits option-bounded (`ResolveBoundedOptionLimit`/`ResolveMaxResults`).
  - Keep error mapping centralized in package base helpers.

## New Tool Checklist
1. Define `ToolDefinition` with strict schema (`Required(...)`, `NoAdditionalProperties()`).
2. Normalize/validate arguments through shared helpers.
3. Execute query path through base helpers (including exception/collection mapping).
4. Build result model and response via `BuildAutoTableResponse(...)`.
5. Populate consistent metadata (at minimum: `count/truncated`; include `max_results` when bounded).
6. Add or update tests for helper logic and any new branching behavior.
7. Run targeted build/tests for changed package(s).

## Refactor Checklist
1. Replace duplicated parse/cap/meta logic with shared helper calls.
2. Avoid changing output contracts unless explicitly intended.
3. Add regression tests before removing old helper branches.
4. Keep large formatting/line-ending normalization out of behavior PRs; run as a separate one-shot cleanup PR.

