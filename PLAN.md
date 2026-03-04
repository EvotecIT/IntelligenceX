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
- [x] Startup bootstrap telemetry now includes ordered phase timings + slowest-phase summary so UI can explain where startup time is spent.
- [x] `ToolRegistry` now hard-fails registration when `Routing.PackId` or `Routing.Role` is missing (strict-mode toggle still controls explicit-vs-inferred source).
- [x] Startup diagnostics now include per-pack registration progress (`pack_register_progress`) + slow-registration summaries, and Chat app status parsing surfaces registration activity during runtime warmup.
- [x] Turn watchdog now surfaces phase-aware in-flight wait hints before first token (awaiting ack/model/tool output) and mirrors that in header status for long-running turns.
- [x] Service now emits early request/model-phase progress markers before thread binding, weighted tool-subset model calls, and model resolution so users see immediate in-flight status instead of silent 30s+ gaps.
- [x] Turn metrics now include phase timings for `ensure_thread`, weighted subset-selection, and model-resolution, and app debug summary surfaces those phase costs directly.
- [x] Chat app debug summary now highlights the slowest per-turn startup stage (`ensure_thread`, `weighted_subset`, `resolve_model`) to explain first-token latency at a glance.
- [x] Chat app profile persona normalization no longer hardcodes AD-specific role hints (for example `active directory`/`ad engineer`), keeping persona shaping generic.
- [x] Startup telemetry split now exposes `pack_register` and `registry_finalize` phase timings (plus total registry time) so post-load delays are attributable without guesswork.
- [x] Chat app sidecar copy targets now filter missing artifacts before copy, preventing transient `.psd1` MSB3030 failures during app test/build runs.
- [x] Tool health diagnostics now resolve pack ids from explicit routing contracts only (legacy metadata/suffix fallback removed).
- [x] Tool health pack-info discovery now uses routing role contracts only (legacy name-suffix `_pack_info` fallback removed from probe selection/smoke planning).
- [x] Chat service bootstrap no longer depends on `_toolPackIdsByToolName` runtime mapping; orchestration catalog is built directly from registered tool contracts.
- [x] `ToolOrchestrationCatalog` no longer accepts registered pack-id fallback maps; pack identity comes from routing contracts only.
- [x] Chat `list_tools` category shaping now uses declared tool category metadata only (generic normalization); no pack-id fallback/category alias maps remain in Chat service.
- [x] Regression coverage now proves Chat does not auto-switch packs after a failed tool call, even when tools from other packs are available.
- [x] SQLite profile migration now preserves legacy `enable_*_pack` intent by translating into pack-id lists before deprecated columns are dropped.
- [x] Regression coverage added for legacy pack-toggle migration and unknown-required-column insert backfill behavior.
- [x] PR #986 merged: planner prompt no longer emits inferred `pack`/`pack_aliases`; Chat planner context stays generic (`category`/`family`/`tags`) while routing search tokens remain metadata-backed.

## Audit Corrections (2026-03-03)

- [x] Decoupling cleanup: AD domain guardrail hint text is now capability-based (no hardcoded tool ids in user-facing guidance).
- [x] Stabilization hotfix: finalize-time host structured next-action replay now blocks stale host-target replays when user/assistant host hints conflict.
- [x] Stabilization hotfix: finalize-time single-host scope-shift guard now evaluates raw user intent (not routed rewrite payload), reducing stale AD0-style replay loops on contextual follow-ups.
- [x] Closed: Chat bootstrap now discovers built-in packs generically from tool assemblies/descriptors in `ToolPackBootstrap` (no hardcoded per-pack bootstrap chain).
- [x] Closed: host-hint helpers were re-scoped into `ChatServiceSession.HostHints.cs` (fallback-era file naming removed).
- [x] Hotfix landed: stale structured-next-action carryover replay now suppresses self-loop replays (same tool + equivalent args) and rejects host-hint-conflicting carryover execution.
- [x] Hotfix landed: deferred startup metadata flow no longer skips metadata sync purely because authentication is initially unknown (skip now applies only when interactive login is already in progress).
- [x] Hotfix landed: startup bootstrap status publishing now stays visible while connected startup metadata sync is in progress.
- [x] Closed (mitigated): server-scoped tooling bootstrap cache now reuses prior bootstrap snapshots across reconnect/session churn, avoiding repeated full pack bootstrap on warm path.
- [x] Hotfix landed: carryover structured-next-action replay now accepts compact non-question follow-ups even when continuation expansion is unavailable, while still rejecting contextual-anchor and question turns.
- [x] Hotfix landed: carryover structured-next-action replay now treats compact contextual scope-shift follow-ups (for example "other DCs") as fresh planning turns, preventing stale single-host auto-replay loops.
- [x] Hotfix landed: Chat service now suppresses duplicate final `ChatResultMessage` publishes for the same request/thread/text to prevent repeated assistant finals.
- [x] Hotfix landed: session header status now keeps startup-pending messaging while metadata/tool-pack readiness is still unresolved (no premature "Ready" flip).
- [x] Regression coverage added: two-turn carryover scenario now proves `go ahead` follow-up replays queued structured next-action tool calls (host carryover call-id path).
- [x] Hotfix landed: compact follow-up question turns no longer force execution-blocker/cached-evidence rewrite at finalize (tool-capability questions keep direct conversational handling).
- [x] Hotfix landed: cached evidence fallback now requires explicit tool-name match when the user references a specific tool id, preventing unrelated stale evidence reuse.
- [x] Hotfix landed: deferred startup metadata sync now waits for authenticated runtime state and is re-queued after successful login completion, avoiding premature metadata churn during sign-in.
- [x] Hotfix landed: host structured next-action replay now skips same-tool/same-arguments loops (`next_action_self_loop`) to avoid repeated AD0-style replay churn.
- [x] Hotfix landed: carryover structured-next-action auto-replay now blocks repeated identical tool+argument replays until fresh context is provided (for example explicit host pin), reducing AD0-style replay churn across turns.
- [x] Hotfix landed: carryover structured-next-action auto-replay now also blocks same-tool single-host replay loops when argument payload drifts but host scope remains unchanged, unless fresh host context is provided.
- [x] Hotfix landed: carryover replay host-hint gating now incorporates assistant draft host targets (not just user text), preventing stale single-host (`AD0`) replay after multi-host follow-up plans.
- [x] Hotfix landed: carryover host-hint gating now treats multi-host follow-up hints as incompatible with single-host auto-replay, preventing mixed-hint (`AD0` + `AD1/AD2`) stale carryover execution loops.
- [x] Hotfix landed: carryover replay freshness/scope guards now evaluate raw user request text even when assistant draft host hints are appended for mismatch detection, preventing AD0-only replay loops from assistant-draft host echo.
- [x] Hardening landed: carryover replay now passes raw replay-intent text and assistant host-hint context as separate inputs (no in-band marker protocol), reducing marker-collision risk while preserving multi-host mismatch blocking.
- [x] Startup stabilization: transient runtime reconnects now preserve interactive auth state when prior state is authenticated or login is in-flight (unless explicit unauthenticated probe exists), reducing sign-in/connect-disconnect churn.
- [x] Startup visibility: connect flow now surfaces per-attempt pipe connect/retry timeout and retry-delay progress in status text (instead of generic "starting runtime" only).
- [x] Validation checkpoint: `dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- analyze validate-catalog --workspace .` passes with `0 error(s), 0 warning(s)` on this branch.
- [x] Hotfix landed: contextual follow-up detection now evaluates the `Follow-up:` tail from legacy continuation expansion when deciding carryover replay eligibility.
- [x] Closed: standalone lowercase `ad` alias auto-routing was removed from domain-intent signal resolution to keep Chat lexical routing generic.
- [x] Hotfix landed: continuation subset reuse now exits when follow-up text explicitly names a tool outside the remembered subset (for example `eventlog_live_query`), forcing fresh candidate routing.
- [x] Startup visibility hotfix: header status now keeps a bounded runtime lifecycle timeline (status tooltip + debug panel) so long connect/auth/bootstrap phases are traceable instead of collapsing into a single generic chip.
- [x] Startup/turn diagnostics hotfix: routing-meta activity timeline labels now include selected strategy and tool counts (`strategy`, `selected/total`) instead of a generic `route strategy` marker.
- [x] Stabilization hotfix: finalize-time execution blocker now skips cached-evidence substitution for explicit tool-capability questions (for example `eventlog_evtx_query?`), preserving direct conversational/tool-availability answers.
- [x] Startup perf hotfix: plugin duplicate detection now has a loaded-assembly fast-path (skip before dependency preload/reflection), reducing first-session tool bootstrap stalls and preventing avoidable reconnect churn during deferred metadata sync.
- [x] Stabilization hotfix: explicit tool-id follow-ups now suppress pending-action/carryover auto-replay rewrites, and escaped Markdown tool ids (for example `eventlog\_evtx\_query`) are recognized by cached-evidence gating.
- [x] Startup resilience hotfix: deferred startup metadata phases (`hello`, `list_tools`, `auth_refresh`) now retry once on transient disconnect errors to reduce "connected but packs/catalog missing" startup failures.
- [x] Startup/turn UX hotfix: assistant final-message replacement now reuses the most-recent assistant bubble when only `System/Tools` rows followed (no intervening user), preventing duplicate assistant finals during retry/reconnect churn.
- [x] Stabilization hotfix: carryover structured-next-action replay eligibility now evaluates compact follow-up intent from raw user text (not routed payload rewrite text), restoring `go ahead` follow-up auto-execution after pending-action routing hints.
- [x] Stabilization hotfix: domain-intent payload parsing now handles invalid UTF-16 input safely (catches `ArgumentException` in addition to `JsonException`) to keep compact follow-up expansion Unicode-safe.
- [x] Contract-alignment cleanup: routing/output lifecycle tests now reflect strict routing-contract enforcement and single-meaningful-final result policy for the same request/thread pair.
- [x] Startup stability hotfix: deferred startup metadata sync now supports rerun requests when login succeeds during an in-flight sync, preventing dropped post-login `hello/list_tools/auth_refresh` refreshes and stale tool-catalog visibility.
- [x] Stabilization hotfix: continuation subset reuse now recognizes escaped Markdown tool ids (for example ``eventlog\_live\_query``) as explicit tool references, forcing fresh routing when follow-ups switch tools across packs.
- [x] Regression coverage expanded: deferred startup metadata rerun path now has explicit dispatch-safety tests (rerun requested + connected + not shutting down) alongside busy-sync rerun-request checks.
- [x] Stabilization hotfix: explicit quoted tool-descriptor follow-ups (including multiline/backticked catalog snippets and invisible format chars inside tool ids) now bypass finalize-time cached-evidence fallback rewrites and stay on direct tool-capability answer paths.
- [x] Startup/dispatch stabilization hotfix: `SendPromptAsync` now claims startup/send lifecycle state atomically behind the active-turn lock, preventing manual-send vs auto-dispatch races that produced duplicate assistant replies.
- [x] Carryover stabilization hotfix: contextual compact follow-up questions now block stale single-host structured replay when thread evidence is multi-host, while short acknowledgement questions remain replay-eligible.
- [x] Startup UX hotfix: login-completed status updates now queue deferred startup metadata sync before publishing connected status, avoiding transient "ready" flips while tool-pack startup is still pending.
- [x] Startup UX hotfix: startup pending/status overlay now includes browser sign-in-in-progress states, and startup-time reconnect churn surfaces explicit reconnect-sync status (instead of generic disconnected text).
- [x] Startup/send hotfix: queued-after-login prompt deduplication now treats one-sided empty conversation ids as equivalent for normalized prompt text, preventing duplicate dispatch after startup/sign-in transitions.
- [x] Contract guardrail expanded: bootstrap metadata tests now assert all canonical built-in tools register with explicit routing source + pack id + role under strict registration (not only `_pack_info` tools).
- [x] Stabilization regression coverage: finalize host scope-shift guard now has explicit precedence tests proving raw user intent is used ahead of routed rewrite text when deciding stale single-host replay blocking.
- [x] Catalog contract projection now includes setup requirements/hints, normalized handoff edges, and recovery-policy details (`retryable_error_codes`, alternate engines) in `ToolOrchestrationCatalog`.
- [x] Contract-first domain intent alignment: runtime `/act` resolution now requires catalog-mapped action ids (no undeclared default-action fallback when custom routing action ids are registered), and domain host guardrail candidate detection no longer infers AD scope from tool-name patterns.
- [x] Regression coverage now asserts cross-pack isolation in orchestration catalog: no ADPlayground -> DomainDetective handoff is inferred without explicit `ToolHandoffContract` edges.
- [x] Live strict scenario validation: `ad-ad0-then-all-dcs-followthrough-10-turn` passes end-to-end in host runtime with cross-DC fanout (`machine_name>=2`) and no duplicate tool-call/output ids.
- [x] Live strict scenario validation: `ad-eventlog-tool-capability-followthrough-10-turn` now guards explicit `eventlog_evtx_query` capability follow-ups against cached-evidence fallback regressions and passes end-to-end (`10/10` turns) in host runtime.

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
- [x] `G5` Tools metadata enrichment hardcoded pack/category inference maps removed from `IntelligenceX/Tools/ToolSelectionMetadata.cs` in favor of contract/tag metadata.
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
6. [x] Extend `ToolDefinition` validation in `IntelligenceX/Tools/ToolRegistry.cs` to require explicit pack/routing role metadata.
7. [x] Remove implicit pack/category/domain-family inference pathways from `IntelligenceX/Tools/ToolSelectionMetadata.cs` (or gate them behind hard-fail mode that is always enabled).
8. [x] Enforce one canonical source for domain intent family/action mapping from routing contracts.
9. [x] Ensure all `_pack_info` tools define explicit routing contracts (not inference).
10. [ ] Keep `ToolPackGuidance` as rich documentation contract, but make routing-critical fields available without calling tools.

## Phase 2 - Build Runtime Orchestration Catalog (Chat Reads Contracts, Not Names)

1. [x] Introduce `ToolOrchestrationCatalog` in Chat bootstrapping built from `ToolRegistry.GetDefinitions()`.
2. [x] Include in catalog: pack id, role, scope/operation/entity/risk, domain family/action, setup requirements, handoff edges, recovery policy.
3. [x] Replace direct `_toolPackIdsByToolName` and suffix inference consumers with catalog queries.
4. [x] Keep Chat startup diagnostics but add new contract health metrics (missing role, missing handoff schema, invalid setup contracts).
5. [x] Surface catalog health in existing routing policy UI payloads.
6. [x] Add runtime policy toggle `RequireExplicitRoutingMetadata` (CLI/profile/session policy) to support strict contract-only registration rollout.

## Phase 3 - Remove Chat Fallback Engine

1. [x] Delete cross-pack fallback builders from `ChatServiceSession.PackCapabilityFallback.cs`.
2. [x] Delete fallback host-hint helpers tied to that flow from `ChatServiceSession.HostHints.cs`.
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
6. [x] Add one synthetic sample pack in tests to prove plug-in registration works without Chat edits.

## Phase 6 - Typed Tool Surface (Less Stringly, More Models)

1. [ ] Add optional typed tool interface/adapter pattern in `IntelligenceX.Tools.Common` (request/response model typed; envelope serialization centralized).
2. [ ] Keep `ITool` compatibility adapter for transport, but mark direct raw argument parsing patterns as deprecated.
3. [ ] Enforce typed binders (`ToolRequestBinder`) for all new tools; backfill existing tools incrementally.
4. [ ] Standardize success/error envelope shaping through `ToolResultV2` only.
5. [ ] Add analyzer rule in `IntelligenceX.Tools.Tests` or shared analyzer package to flag ad-hoc `arguments?.Get...` in refactored packs.

## Phase 7 - Test Migration And Coverage

1. [x] Replace reflection-heavy fallback tests in `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceRoutingTrimTests.*PackFallback*.cs` with internal helper exposure tests or remove if behavior deleted.
2. [x] Add contract-driven routing tests in Chat that use synthetic tools with explicit contracts.
3. [x] Add regression tests verifying Chat does not auto-switch packs after tool failure.
4. [x] Add tests ensuring preflight uses role contracts, not suffixes.
5. [x] Add tests ensuring DomainDetective and ADPlayground remain isolated unless handoff contract explicitly connects them.
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

## Immediate Next Steps (Post-Audit)

1. [x] Add continuation-subset escape for tool-capability question turns that do not reference explicit tool ids, so follow-up capability questions do not get trapped in stale subset visibility.
2. [x] Add startup churn diagnostics classification in app status/debug (for example `auth_wait`, `pipe_retry`, `metadata_retry`, `runtime_disconnect`) so connect/disconnect loops are attributable without log digging.
3. [x] Add end-to-end regression covering contextual follow-up scope-shift + host structured next-action finalize path using routed rewrite text to prevent stale single-host replay regressions.
4. [x] Run full release preflight (`dotnet build`, `dotnet test`, harness net8/net10) before PR merge.

## Definition Of Done

- [x] `DoD1` No Chat file contains cross-pack fallback execution methods.
- [x] `DoD2` Chat does not decide substitute tools based on hardcoded pack names.
- [x] `DoD3` Pack preflight/routing relies on contracts, not suffix/prefix naming.
- [ ] `DoD4` Every registered tool has explicit routing role + pack metadata.
- [x] `DoD5` New synthetic pack/tool can be added in tests without Chat code changes.
- [x] `DoD6` DomainDetective vs ADPlayground separation enforced by contracts/tests.
- [x] `DoD7` Full build/test suite passes after legacy fallback removal.

## Suggested Session Plan

1. [ ] Session 1: Phase 0 + Phase 1 scaffolding (contracts + validation).
2. [ ] Session 2: Phase 2 catalog + Phase 4 preflight/routing heuristic replacement.
3. [ ] Session 3: Phase 3 fallback deletion + failing tests triage.
4. [ ] Session 4: Phase 5 pack migrations (ADPlayground/DomainDetective/DnsClientX first).
5. [ ] Session 5: Phase 6 typed-surface migration start + analyzer guardrails.
6. [ ] Session 6: Phase 7/8 cleanup, docs, and final quality gate.
