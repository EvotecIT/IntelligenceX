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
   - Add `.WithWriteGovernanceMetadata()`.
   - For probe-aware auth flows add `.WithAuthenticationProbeReference()`.
   - End with `.NoAdditionalProperties()`.
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
  - expose `auth_probe_id` in schema using `.WithAuthenticationProbeReference()`.
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
