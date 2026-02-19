---
name: intelligencex-write-tool-authoring
description: Use when adding/refactoring mutating tools with shared write-governance and authentication contracts.
---

# Skill: intelligencex-write-tool-authoring

Use this skill when adding or refactoring write-capable (mutating) tools.

## Goal
- Keep write tools consistent, low-maintenance, and safe by default.
- Reuse shared helpers instead of open-coding governance, schema, and response logic.

## Required Build Pattern
1. Define schema with shared extensions:
   - Start with `ToolSchema.Object(...)`.
   - Prefer `.WithWriteGovernanceDefaults()` for write tools.
   - For probe-aware auth flows prefer `.WithWriteGovernanceAndAuthenticationProbe()`.
   - Use low-level `.WithWriteGovernanceMetadata()` / `.WithAuthenticationProbeReference()` only for advanced custom ordering.
2. Define write contract with shared factories:
   - Boolean intent: `ToolWriteGovernanceConventions.BooleanFlagTrue(...)`.
   - String intent: `ToolWriteGovernanceConventions.StringEquals(...)`.
3. Define authentication contract with shared factories:
   - Host-managed auth: `ToolAuthenticationConventions.HostManaged(...)`.
   - Probe-aware auth: set `supportsConnectivityProbe: true` and `probeToolName`.
4. Return success with standardized write envelope:
   - Use `ToolResponse.OkWriteActionModel(...)` for mutating actions with dry-run/apply mode.

## Write Intent Rules
- Write-intent argument must be explicit (`send`, `apply`, `intent=read_write`, etc.).
- Keep default behavior non-mutating when possible.
- Use explicit confirmation (`allow_write` or same as intent flag) for actual writes.

## Governance Metadata Rules
- Always expose canonical metadata args in schema:
  - `write_execution_id`
  - `write_actor_id`
  - `write_change_reason`
  - `write_rollback_plan_id`
  - `write_rollback_provider_id`
  - `write_audit_correlation_id`
- Runtime policy decides which fields are required at execution time.

## Authentication + Probe Rules
- When tool supports auth preflight probes:
  - set `supportsConnectivityProbe: true` and provide `probeToolName`.
  - expose `auth_probe_id` in schema using `.WithWriteGovernanceAndAuthenticationProbe()` (or `.WithAuthenticationProbeReference()` for custom pipelines).
  - do not hand-roll schema argument names; use `ToolAuthenticationArgumentNames`.
- For strict probe gating before writes:
  - validate probe references through `ToolAuthenticationProbeValidator`.
  - use a stable target fingerprint derived from endpoint/auth context.
  - keep probe max-age configurable in tool options.

## Pack Guidance Rules
- Ensure tool catalog traits expose:
  - mutating action arguments
  - write-governance metadata arguments
- Ensure authentication metadata exposes:
  - `supports_connectivity_probe`
  - `probe_tool_name` (when probe-aware)
- Do not add tool-specific ad-hoc metadata fields when canonical ones fit.

## Test Requirements
- Add or update:
  - schema contract/snapshot tests
  - registry/governance registration tests
  - authentication contract/registry tests (especially probe-aware contracts)
  - response envelope tests for write mode (`dry-run` vs `apply`)
- Prefer focused unit tests over broad end-to-end tests unless behavior spans components.

## Automated Enforcement
- Internal maintainability rule `IXTOOL001` validates write-capable tool schemas under `IntelligenceX.Tools/**`.
- The rule flags `ToolDefinition` entries with non-null `writeGovernance` that do not use:
  - `WithWriteGovernanceDefaults()`
  - `WithWriteGovernanceAndAuthenticationProbe()`
- Local verification command:
  - `dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- analyze run --workspace . --config .intelligencex/reviewer.json --out artifacts --pack intelligencex-maintainability-default --strict true`
  - `dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- analyze gate --workspace . --config .intelligencex/reviewer.json`

## Reusable Scaffold Assets
- Tool template:
  - `templates/write-tool-template.cs.txt`
- Test template:
  - `templates/write-tool-tests-template.cs.txt`
- Pre-PR checklist helper:
  - `scripts/new-write-tool-checklist.ps1`

## Suggested Execution Order
1. Copy the tool template and replace `__PLACEHOLDER__` markers.
2. Register schema + write/auth contracts before implementing business logic.
3. Return mutating responses via `ToolResponse.OkWriteActionModel(...)`.
4. Copy test template and wire tool-specific argument names and expected contracts.
5. Run checklist script and fix all failing checks before opening PR.

## Special Cases (Read + Write Dual-Mode Tools)
- For tools that can run safely in read-only mode and mutating mode (for example shell tools):
  - keep read-only path default
  - require explicit intent switch for write path
  - enforce governance only on write-intent calls

## Runtime Policy Integration (Host/Service)
- Do not hand-wire per-tool runtime guardrails in app code.
- Use centralized runtime policy bootstrap so host/service stay aligned:
  - write mode: `enforced|yolo`
  - audit sink mode/path: `none|file|sqlite`
  - auth runtime preset: `default|strict|lab`
  - run-as profile catalog path
- Ensure tool packs consume shared runtime dependencies through pack options (for example shared auth probe store) instead of creating private per-tool stores in each runtime host.
