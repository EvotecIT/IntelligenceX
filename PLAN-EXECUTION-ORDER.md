# Chat/Tools Decoupling Execution Order

## Goal

Execute `PLAN.md` in small, merge-safe increments with clear dependencies, parallel work, and explicit stop points.

## Progress Update (2026-03-02)

- [x] PR #985 merged (`2f649d62d164185755c19e33906a7064ae4ff132`): contract-first pack toggles, startup bootstrap visibility, and migration hardening are now on `master`.
- [x] PR #986 merged (`4db3931ac8cbb753da7ff160311a4f8e0d6904a3`): removed planner prompt pack-hint inference (`pack`/`pack_aliases`) to keep Chat planner context generic.

## Audit Update (2026-03-03)

- [x] Stabilization hotfix: structured-next-action carryover replay now blocks stale self-loop replays and host-hint-conflicting replays.
- [x] Stabilization hotfix: startup deferred metadata no longer skips metadata sync solely due to initial unauthenticated state.
- [x] Stabilization hotfix: bootstrap progress status can publish while connected startup metadata sync is still active.
- [x] Closed migration gap: `ToolPackBootstrap` now performs descriptor-driven built-in pack discovery (no hardcoded per-pack bootstrap chain).
- [x] Closed migration gap: host-hint helpers moved to `ChatServiceSession.HostHints.cs` (fallback-era naming removed).
- [x] Closed startup perf gap (warm path): server-scoped tooling bootstrap snapshot cache now avoids repeated full bootstrap during reconnect/session churn.

## Rules For This Migration

- [ ] Keep each PR focused to one objective and one rollback boundary.
- [ ] No net-new Chat hardcoded fallback logic during migration.
- [ ] Prefer additive contracts first, then Chat rewiring, then deletion.
- [ ] Every deletion PR must include equivalent or stricter tests.

## PR Sequence

### PR 0 - Baseline Governance (Docs + Tracker)

Files:

- `PLAN.md`
- `PLAN-EXECUTION-ORDER.md`
- `TODO.md`
- `InternalDocs/architecture/adr-0001-chat-tools-contract-boundary.md`

Checklist:

- [x] Add ADR for contract-first boundary.
- [x] Add migration tracker entries to `TODO.md`.
- [x] Confirm maintainers accept no-legacy direction (drop Chat fallback engine target).

### PR 1 - Architecture Guardrails (Prevent Re-growth)

Files:

- `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatFallbackArchitectureGuardrailTests.cs`

Checklist:

- [x] Add test to freeze current `TryBuildCross*Fallback*` method set (only allowed to shrink).
- [x] Add test to block `PackIdMatches(...)` usage outside legacy fallback file.
- [x] Add test to freeze hardcoded pack-id set in legacy fallback file.

### PR 2 - Contract Surface Expansion (Tools Core)

Files (expected):

- `IntelligenceX/Tools/ToolRoutingContract.cs`
- `IntelligenceX/Tools/ToolDefinition.cs`
- `IntelligenceX/Tools/ToolRegistry.cs`
- New contract files in `IntelligenceX/Tools` and `IntelligenceX.Tools.Common`

Checklist:

- [x] Add role/setup/handoff/recovery contract types.
- [x] Wire validation in `ToolRegistry`.
- [x] Keep backward-compatible defaults temporarily only if needed for staged migration.

Dependency: PR 1

### PR 3 - Contract Diagnostics + UI Transparency

Files (expected):

- `IntelligenceX.Chat/IntelligenceX.Chat.Tooling/ToolRoutingCatalogDiagnostics.cs`
- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.PolicyAndTypes.cs`
- `IntelligenceX.Chat/IntelligenceX.Chat.App/Ui/Shell.10.core.js`

Checklist:

- [x] Extend diagnostics with new contract health fields.
- [x] Expose diagnostics in session policy payload.
- [x] Add runtime policy toggle (`--require-explicit-routing-metadata`) with profile persistence and session policy exposure.
- [x] Render new health signals in UI policy panel.

Dependency: PR 2

### PR 4 - Orchestration Catalog In Chat

Files (expected):

- New catalog builder in `IntelligenceX.Chat.Tooling` or `IntelligenceX.Chat.Service`
- `ChatServiceSession.ProfilesAndModels.cs`
- `ChatServiceSession.cs`

Checklist:

- [x] Build runtime orchestration catalog from registry definitions/contracts.
- [x] Replace direct suffix/prefix pack derivation consumers with catalog lookups where possible.
- [x] Keep behavior equivalent before deleting legacy code.

Dependency: PR 2

### PR 5 - Preflight Contractization

Files:

- `ChatServiceSession.PackPreflight.cs`
- Related tests in `IntelligenceX.Chat.Tests`

Checklist:

- [x] Replace `_pack_info` / `_environment_discover` suffix selection with role metadata selection.
- [x] Keep existing semantics for required-arg checks and remembered successful preflight calls.

Dependency: PR 4

### PR 6 - Routing Heuristic Contractization

Files:

- `ChatServiceSession.ChatRouting.RoutingScoring.cs`
- `ChatServiceSession.ToolRouting.Secondary.cs`
- `ChatServiceSession.ToolRouting.DomainIntentSignals.cs`
- Related tests

Checklist:

- [x] Replace name-based family keys with contract pack/role/family metadata.
- [x] Keep Unicode/language-neutral behavior and existing pending-action ordinal support.

Dependency: PR 4

### PR 7 - Pack Migration Wave 1 (Highest Ambiguity Packs)

Files:

- `IntelligenceX.Tools.ADPlayground/*`
- `IntelligenceX.Tools.DomainDetective/*`
- `IntelligenceX.Tools.DnsClientX/*`
- `IntelligenceX.Tools.Tests/*`

Checklist:

- [x] Add explicit role/handoff/setup/recovery contracts.
- [x] Add tests proving AD vs public-domain separation unless explicit handoff contract exists.

Dependency: PR 2

### PR 8 - Remove Chat Fallback Engine

Files:

- `ChatServiceSession.PackCapabilityFallback.cs`
- `ChatServiceSession.HostHints.cs`
- `ChatServiceSession.ChatRouting.NoExtractedFinalize.cs`
- `ChatServiceSession.cs`
- `ChatServiceSession.ProfilesAndModels.cs`
- Heavy test cleanup in `ChatServiceRoutingTrimTests.ToolNudge.*PackFallback*.cs`

Checklist:

- [x] Remove fallback replay path and fallback contract cache.
- [x] Remove cross-pack builder methods and helper methods.
- [x] Delete or rewrite fallback-specific tests.
- [x] Keep model retry/review loops intact.

Dependency: PR 5, PR 6, PR 7

### PR 9 - Pack Migration Wave 2 (Remaining Packs)

Files:

- `IntelligenceX.Tools.System/*`
- `IntelligenceX.Tools.EventLog/*`
- `IntelligenceX.Tools.TestimoX/*`
- `IntelligenceX.Tools.FileSystem/*`
- `IntelligenceX.Tools.Email/*`
- `IntelligenceX.Tools.PowerShell/*`
- `IntelligenceX.Tools.OfficeIMO/*`

Checklist:

- [x] Complete explicit contracts for all packs/tools.
- [x] Ensure tool wrappers remain thin and engine-first.

Dependency: PR 2

### PR 10 - Typed Tool Surface Enforcement

Files:

- `IntelligenceX.Tools.Common/*` (typed adapters/helpers)
- Analyzer/guardrail tests in `IntelligenceX.Tools.Tests` or shared analyzer project

Checklist:

- [ ] Prefer typed binders for all migrated/refactored tools.
- [ ] Add guardrail to flag ad-hoc direct argument parsing in target packs.
- [ ] Standardize on `ToolResultV2` for migrated paths.
- [x] Wave-2 typed migration batch completed for: pack/discovery tools, `SystemDevicesSummary`, `SystemHardwareIdentity`, `SystemHardwareSummary`, `SystemInfo`, `SystemBiosSummary`, `SystemSecurityOptions`, `SystemBootConfiguration`, `SystemRdpPosture`, `SystemSmbPosture`, `SystemFeaturesList`, `SystemUpdatesInstalled`, `SystemPatchDetails`, `SystemDisksList`, `SystemLogicalDisksList`, `SystemPortsList`, `SystemProcessList`, `SystemFirewallProfiles`, `SystemFirewallRules`, `SystemServiceList`, `SystemScheduledTasksList`, `SystemTimeSync`, `SystemWhoAmI`, `WslStatus`, `FsList/FsRead/FsSearch`, `EventLogNamedEventsCatalog`, `EventLogNamedEventsQuery`, `EventLogLiveQuery`, `EventLogTopEvents`, `EventLogLiveStats`, `EventLogEvtxFind`, `EventLogEvtxQuery`, `EventLogEvtxStats`, `EventLogEvtxSecuritySummary`, `TestimoXRulesList`, `TestimoXRulesRun`, `PowerShellRun`, `EmailImapSearch`, `EmailImapGet`, `OfficeImoRead`, plus Wave-1/AD carryover typed migrations: `DomainDetectivePackInfo`, `DomainDetectiveNetworkProbe`, `DomainDetectiveDomainSummary`, `DnsClientXPackInfo`, `DnsClientXQuery`, `DnsClientXPing`, `AdPackInfo`, `AdMonitoringProbeCatalog`, `AdRecycleBinLifetime`, `AdGroupMembers`, `AdGroupMembersResolved`, `AdGroupsList`, `AdAdminCountReport`, `AdGpoChanges`, `AdGpoList`, `AdGpoInventoryHealth`, `AdGpoHealth`, `AdGpoPermissionReport`, `AdGpoPermissionConsistency`, `AdLdapQuery`, `AdLdapQueryPaged`, `AdObjectGet`, `AdObjectResolve`, `AdSearch`, `AdSpnSearch`, `AdDomainAdminsSummary`, `AdPrivilegedGroupsSummary`, `AdStaleAccounts`, `AdUsersExpired`, `AdWhoAmI`, `AdDomainInfo`, `AdLdapDiagnostics`, `AdSearchFacets`, `AdReplicationSummary`, and `AdReplicationConnections`.

Dependency: PR 7, PR 9

### PR 11 - Final Cleanup + Hardening

Files:

- Remaining stale constants/docs/tests across Chat/Tools.

Checklist:

- [ ] Remove obsolete telemetry markers/constants tied to deleted fallback engine.
- [ ] Close migration tracker entries.
- [ ] Validate final DoD from `PLAN.md`.

Dependency: PR 8, PR 10

### PR 12 - Startup Perf And Decoupling Reality-Close

Files (expected):

- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceServer.cs`
- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession*.cs`
- `IntelligenceX.Chat/IntelligenceX.Chat.Tooling/ToolPackBootstrap*.cs`
- `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow*.cs`
- `PLAN.md`
- `PLAN-EXECUTION-ORDER.md`

Checklist:

- [x] Move tooling bootstrap out of per-connection session constructor into reusable runtime cache/lifecycle.
- [ ] Keep startup status truthful: do not surface "ready" semantics until metadata/auth probes settle or explicitly fail-open with reason.
- [x] Replace hardcoded known-pack bootstrap chain with descriptor/manifest-driven registration.
- [x] Remove/rename fallback-era host-hint file so architecture guardrails match current source layout.
- [ ] Add regression tests for reconnect warm path and multi-turn follow-up carryover against host scope changes.

## Parallelization Map

- [ ] Track A can run PR 2, PR 7, PR 9, PR 10.
- [ ] Track B can run PR 3, PR 4, PR 5, PR 6.
- [ ] Track C can continuously update tests/docs in PR 1 and PR 11.
- [ ] Critical merge gate before fallback deletion: PR 5 + PR 6 + PR 7 must be merged.

## Recommended Branch Names

- `chore/chat-tools-contract-adr`
- `test/chat-fallback-guardrails`
- `feat/tool-contract-role-setup-handoff-recovery`
- `feat/chat-orchestration-catalog`
- `refactor/chat-preflight-contract-role`
- `refactor/chat-routing-contract-taxonomy`
- `feat/tools-wave1-ad-dd-dnsx-contracts`
- `refactor/chat-remove-pack-fallback-engine`
- `feat/tools-wave2-pack-contracts`
- `refactor/tools-typed-surface-enforcement`
- `chore/chat-tools-decoupling-cleanup`

## Release Safety Checkpoints

1. [x] Checkpoint A (after PR 4): catalog live, no behavior deletion yet.
2. [x] Checkpoint B (after PR 6 + PR 7): contract-driven selection/preflight proven in tests.
3. [x] Checkpoint C (after PR 8): Chat fallback engine removed.
4. [ ] Checkpoint D (after PR 11): all DoD checks complete.
