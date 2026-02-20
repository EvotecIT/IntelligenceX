# Tool Authoring Playbook

Internal guidance for adding or refactoring tools with minimal duplication and stable contracts.

## Goals
- Keep new tools short, predictable, and easy to review.
- Reuse shared helper paths before adding tool-specific helper code.
- Keep behavior contracts stable (`error_code`, metadata keys, and table view shape).
- Keep safety and governance behavior consistent.

## Build Order
1. Define schema with shared helpers.
2. Attach governance contract if tool can mutate.
3. Attach authentication contract when auth is involved.
4. Execute via package base/shared helper path.
5. Use standard response envelope helpers.
6. Add focused contract tests.

## Shared Reuse Rules
1. Start from package base classes (`ActiveDirectoryToolBase`, `SystemToolBase`, `EventLogToolBase`, `FileSystemToolBase`, etc.).
2. Prefer `ToolBase.RunPipelineAsync(...)` + `ToolPipeline` middleware for bind/precondition/execute orchestration.
3. Use typed binders (`ToolRequestBinder` + `ToolRequestBindingResult<TRequest>`) instead of ad-hoc `arguments?.Get...` parsing in new/refactored tools.
4. Use `ToolBase.BuildAutoTableResponse(...)` for table envelopes instead of inline response shaping.
5. Use `ToolBase.AddMaxResultsMeta(...)` (directly or via package wrappers) for `max_results` metadata.
6. Prefer `ToolQueryHelpers` (`CapRows`, projection filters) over handwritten list-cap/filter plumbing.
7. Add package-local helpers only when behavior is package-specific.

## Schema Rules
- Start with `ToolSchema.Object(...)`.
- Always end with `.NoAdditionalProperties()`.
- For mutating tools, add `.WithWriteGovernanceMetadata()`.
- For auth probe-aware flows, add `.WithAuthenticationProbeReference()`.
- Use canonical argument names from shared constants:
  - `ToolWriteGovernanceArgumentNames`
  - `ToolAuthenticationArgumentNames`

## Governance Rules
- For boolean write intent use `ToolWriteGovernanceConventions.BooleanFlagTrue(...)`.
- For string-based mode intent use `ToolWriteGovernanceConventions.StringEquals(...)`.
- Keep default execution non-mutating when possible.
- Require explicit confirmation for real writes.

## Authentication Rules
- Use `ToolAuthenticationConventions` instead of ad-hoc contracts.
- If tool supports preflight probes:
  - set `supportsConnectivityProbe: true`
  - set `probeToolName`
  - include `auth_probe_id` in schema
- For strict write gating, validate with `ToolAuthenticationProbeValidator`.

## Package-Specific Rules
- Active Directory tools:
  - For required `domain_name` + `max_results`, use `TryReadRequiredDomainQueryRequest(...)`.
  - For policy-attribution shapes, use `TryReadPolicyAttributionToolRequest(...)` (or `ExecutePolicyAttributionTool(...)`).
  - For conventional collection contracts, use `TryExecuteCollectionQuery(...)`.
  - For standard domain table flows, prefer `ExecuteDomainRowsViewTool(...)` so required-domain parsing stays centralized.
  - For metadata, prefer `AddDomainAndMaxResultsMeta(...)` when applicable.
- System/EventLog/FileSystem tools:
  - Keep argument limits option-bounded by default (`ResolveBoundedOptionLimit` / package `ResolveMaxResults`).
  - In EventLog tools, use explicit helpers to avoid semantic drift:
    - `ResolveOptionBoundedMaxResults(...)` for option-bounded `max_results`.
    - `ResolveCappedMaxResults(...)` only when you need an explicit default/cap different from package options.
  - For non-positive semantics, use canonical `ToolArgs.GetOptionBoundedInt32(..., nonPositiveBehavior, defaultValue)` instead of mixing ad-hoc helper variants.
  - Keep error mapping centralized in package base helpers.

## Response Rules
- Prefer `ToolResultV2` helpers for new/refactored tools to keep metadata immutable at call boundaries.
- Use `ToolResultV2.OkModel(...)` for read-style responses.
- Use `ToolResultV2.OkWriteActionModel(...)` for mutating actions.
- Use `ToolResultV2.Error(...)` for failure envelopes.
- Keep error codes stable and machine-readable.

## Required Tests
- Registration/contract tests for schema + auth/governance metadata.
- Focused runtime tests for strict gating logic.
- Response contract tests for dry-run/apply behavior where relevant.
- Internal maintainability checks:
  - `IXTOOL001` for write-schema helper contract.
  - `IXTOOL002` for AD required-domain helper contract.
  - `IXTOOL003` for max-results metadata helper contract (`AddMaxResultsMeta(...)`).
  - `IXTOOL004` for canonical option-bounded max-results helper usage (`ToolArgs.GetOptionBoundedInt32(...)` with explicit non-positive behavior).

## New Tool Checklist
1. Define `ToolDefinition` with strict schema (`Required(...)`, `NoAdditionalProperties()`).
2. Normalize/validate arguments through shared helpers.
3. Execute query path through base helpers (including exception/collection mapping).
4. Build result model/response via shared response helpers.
5. Populate consistent metadata (`count`, `truncated`, and `max_results` when bounded).
6. Add or update tests for helper logic and branching behavior.
7. Run targeted build/tests for changed package(s).

## Refactor Checklist
1. Replace duplicated parse/cap/meta logic with shared helper calls.
2. Avoid output contract changes unless explicitly intended.
3. Add regression tests before removing old helper branches.
4. Keep large formatting/line-ending normalization out of behavior PRs.
