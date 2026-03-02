# IntelligenceX Chat/Tools Decoupling Plan

## Objective

Build a contract-first architecture where:

- Chat is generic orchestration only.
- Tools/packs self-describe routing, handoff, setup, and recovery behavior.
- Adding a new pack/tool does not require Chat hardcoded logic edits.
- Legacy fallback heuristics in Chat are removed.

## Checkpoint Updates (2026-03-01)

- [x] PR #974 merged: replace reflection/object retry-policy test hooks with typed test contract (`RetryProfileSnapshot`).
- [x] PR #975 merged: centralize pack-id normalization in `ToolSelectionMetadata.NormalizePackId(...)` and delegate Chat bootstrap normalization to Tools.
- [x] PR #976 merged: make planner candidate selection core static with explicit `ToolOrchestrationCatalog` input (keep compatibility wrapper for existing test reflection contract).
- [x] Architecture guardrails active in Chat to prevent reintroduction of legacy pack-capability fallback source files/symbols.

## Checkpoint Updates (2026-03-02)

- [x] PR #985 merged: generic runtime pack toggles are fully list-driven (`EnabledPackIds`/`DisabledPackIds`) across app/client/host/profile paths.
- [x] Startup bootstrap visibility landed end-to-end (structured telemetry + status parsing + UI surfacing for runtime/tool-pack loading progress).
- [x] SQLite profile migration now preserves legacy `enable_*_pack` intent by translating into pack-id lists before deprecated columns are dropped.
- [x] Regression coverage added for legacy pack-toggle migration and unknown-required-column insert backfill behavior.

## Hard Decisions (Locked)

- [x] `D1` Remove Chat-owned cross-pack fallback execution logic (no legacy compatibility layer).
- [x] `D2` Keep resilience only inside tools/engines (for example CIM -> WMI), not in Chat routing.
- [ ] `D3` Make routing metadata explicit and contract-validated for every tool.
- [ ] `D4` Prefer typed request/response models in tools; keep JSON/text as transport envelope only.
- [ ] `D5` Treat pack boundaries as strict: DomainDetective != ADPlayground unless contracts declare handoff.

## Current Gaps To Eliminate

- [x] `G1` Chat has hardcoded cross-pack fallback builders in `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.PackCapabilityFallback.cs`.
- [x] `G2` Chat triggers fallback replay in `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ChatRouting.NoExtractedFinalize.cs`.
- [x] `G3` Chat pack preflight no longer depends on `_pack_info`/`_environment_discover` suffix selection.
- [x] `G4` Chat deterministic routing now relies on contract metadata rather than suffix/prefix family-key inference paths.
- [ ] `G5` Tools metadata enrichment still contains hardcoded pack/category inference maps in `IntelligenceX/Tools/ToolSelectionMetadata.cs`.
- [x] `G6` Fallback behavior is partly metadata-driven, partly hardcoded; split must become tool-contract only.

## Phase 0 - Baseline And Guardrails

1. [x] Create ADR in `InternalDocs/architecture/` defining the no-legacy contract-first target.
2. [x] Add temporary architecture test that fails if new `TryBuildCross*Fallback*` methods appear in Chat.
3. [x] Add temporary architecture test that fails if Chat introduces new pack-name switch chains for fallback/routing.
4. [x] Add migration tracker section in `TODO.md` linking this `PLAN.md`.
5. [x] Freeze net-new fallback logic in Chat (explicit policy: no new fallback PRs accepted).

## Phase 1 - Expand Tool Contracts (Tools Layer)

1. [x] Add a dedicated `ToolRecoveryContract` in `IntelligenceX/Tools` (transient retry policy, not cross-pack routing).
2. [x] Add a dedicated `ToolRoleContract` or equivalent role fields in `ToolRoutingContract` (`pack_info`, `environment_discover`, `operational`, `resolver`, `diagnostic`).
3. [x] Add explicit `ToolHandoffContract` types in `IntelligenceX/Tools` for source entity -> target argument mappings.
4. [x] Add explicit `ToolSetupContract` types for prerequisites and user-facing setup hints.
5. [x] Add `RoutingSource` (`explicit|inferred`) and strict-mode gate `ToolRegistry.RequireExplicitRoutingMetadata` to enforce explicit routing metadata during migration.
6. [ ] Extend `ToolDefinition` validation in `IntelligenceX/Tools/ToolRegistry.cs` to require explicit pack/routing role metadata.
7. [ ] Remove implicit pack/category/domain-family inference pathways from `IntelligenceX/Tools/ToolSelectionMetadata.cs` (or gate them behind hard-fail mode that is always enabled).
8. [ ] Enforce one canonical source for domain intent family/action mapping from routing contracts.
9. [ ] Ensure all `_pack_info` tools define explicit routing contracts (not inference).
10. [ ] Keep `ToolPackGuidance` as rich documentation contract, but make routing-critical fields available without calling tools.

## Phase 2 - Build Runtime Orchestration Catalog (Chat Reads Contracts, Not Names)

1. [x] Introduce `ToolOrchestrationCatalog` in Chat bootstrapping built from `ToolRegistry.GetDefinitions()`.
2. [ ] Include in catalog: pack id, role, scope/operation/entity/risk, domain family/action, setup requirements, handoff edges, recovery policy.
3. [ ] Replace direct `_toolPackIdsByToolName` and suffix inference consumers with catalog queries.
4. [x] Keep Chat startup diagnostics but add new contract health metrics (missing role, missing handoff schema, invalid setup contracts).
5. [x] Surface catalog health in existing routing policy UI payloads.
6. [x] Add runtime policy toggle `RequireExplicitRoutingMetadata` (CLI/profile/session policy) to support strict contract-only registration rollout.

## Phase 3 - Remove Chat Fallback Engine

1. [x] Delete cross-pack fallback builders from `ChatServiceSession.PackCapabilityFallback.cs`.
2. [x] Delete fallback host-hint helpers tied to that flow from `ChatServiceSession.PackCapabilityFallback.HostHints.cs`.
3. [x] Remove `_packCapabilityFallbackContractsByPackId` state from `ChatServiceSession.cs`.
4. [x] Remove `RebuildPackCapabilityFallbackContracts(...)` call in `ChatServiceSession.ProfilesAndModels.cs`.
5. [x] Remove fallback replay branch in `ChatServiceSession.ChatRouting.NoExtractedFinalize.cs`.
6. [x] Keep normal model-driven retries/review loops; do not auto-run substitute tools in Chat.
7. [x] For resilience use-case support, route it into tool internals (engine/tool package), not Chat orchestration.

## Phase 4 - Replace Heuristics With Contracts

1. [x] Replace pack preflight suffix detection in `ChatServiceSession.PackPreflight.cs` with role-based contract selection.
2. [x] Replace deterministic family key heuristics in `ChatServiceSession.ChatRouting.RoutingScoring.cs` with contract pack/role fields.
3. [x] Ensure domain-intent signals in `ChatServiceSession.ToolRouting.DomainIntentSignals.cs` come from registered contract signals only.
4. [ ] Keep Unicode-safe ordinal parsing in `ChatServiceSession.PendingActions.IntentParsing.cs` (this is generic and should remain).
5. [ ] Remove remaining routing paths that depend on tool name prefix assumptions when contract fields exist.

## Phase 5 - Tool Pack Migration (Domain Separation + Easy Additions)

1. [x] Migrate ADPlayground pack tools to explicit role/handoff/setup/recovery contracts.
2. [x] Migrate DomainDetective pack tools with explicit domain/public handoff contracts.
3. [x] Migrate DnsClientX pack tools with explicit resolver-vs-posture role split.
4. [x] Migrate System/EventLog/TestimoX/FileSystem/Email/PowerShell/OfficeIMO packs the same way.
5. [x] Add contract tests per pack proving no Chat changes are needed to register and route new tools.
6. [ ] Add one synthetic sample pack in tests to prove plug-in registration works without Chat edits.

## Phase 6 - Typed Tool Surface (Less Stringly, More Models)

1. [ ] Add optional typed tool interface/adapter pattern in `IntelligenceX.Tools.Common` (request/response model typed; envelope serialization centralized).
2. [ ] Keep `ITool` compatibility adapter for transport, but mark direct raw argument parsing patterns as deprecated.
3. [ ] Enforce typed binders (`ToolRequestBinder`) for all new tools; backfill existing tools incrementally.
4. [ ] Standardize success/error envelope shaping through `ToolResultV2` only.
5. [ ] Add analyzer rule in `IntelligenceX.Tools.Tests` or shared analyzer package to flag ad-hoc `arguments?.Get...` in refactored packs.

## Phase 7 - Test Migration And Coverage

1. [x] Replace reflection-heavy fallback tests in `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceRoutingTrimTests.*PackFallback*.cs` with internal helper exposure tests or remove if behavior deleted.
2. [ ] Add contract-driven routing tests in Chat that use synthetic tools with explicit contracts.
3. [ ] Add regression tests verifying Chat does not auto-switch packs after tool failure.
4. [ ] Add tests ensuring preflight uses role contracts, not suffixes.
5. [ ] Add tests ensuring DomainDetective and ADPlayground remain isolated unless handoff contract explicitly connects them.
6. [ ] Keep existing language-neutral routing/ordinal tests green.

## Phase 8 - Cutover And Cleanup

1. [ ] Remove dead constants/helpers tied to old fallback reason telemetry markers.
2. [ ] Remove stale docs referencing Chat cross-pack fallback behavior.
3. [ ] Publish new "How to add a tool/pack without touching Chat" guide in `InternalDocs/agent-playbooks/`.
4. [ ] Publish contract schema examples for plugin authors.
5. [ ] Run full solution build/test gates and close migration tracker items.

## Parallel Workstreams

1. [ ] `Track A (Contracts)` Tools contract types + validation + pack migration templates.
2. [ ] `Track B (Chat Core)` Orchestration catalog + heuristic removal + fallback deletion.
3. [ ] `Track C (Tests/Quality)` Architecture tests + regression suite updates + docs.
4. [ ] Merge order: Track A foundational contracts first, Track B next, Track C continuously.

## Definition Of Done

- [x] `DoD1` No Chat file contains cross-pack fallback execution methods.
- [x] `DoD2` Chat does not decide substitute tools based on hardcoded pack names.
- [x] `DoD3` Pack preflight/routing relies on contracts, not suffix/prefix naming.
- [ ] `DoD4` Every registered tool has explicit routing role + pack metadata.
- [ ] `DoD5` New synthetic pack/tool can be added in tests without Chat code changes.
- [x] `DoD6` DomainDetective vs ADPlayground separation enforced by contracts/tests.
- [ ] `DoD7` Full build/test suite passes after legacy fallback removal.

## Suggested Session Plan

1. [ ] Session 1: Phase 0 + Phase 1 scaffolding (contracts + validation).
2. [ ] Session 2: Phase 2 catalog + Phase 4 preflight/routing heuristic replacement.
3. [ ] Session 3: Phase 3 fallback deletion + failing tests triage.
4. [ ] Session 4: Phase 5 pack migrations (ADPlayground/DomainDetective/DnsClientX first).
5. [ ] Session 5: Phase 6 typed-surface migration start + analyzer guardrails.
6. [ ] Session 6: Phase 7/8 cleanup, docs, and final quality gate.
