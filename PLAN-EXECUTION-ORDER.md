# Chat/Tools Decoupling Execution Order

## Goal

Execute `PLAN.md` in small, merge-safe increments with clear dependencies, parallel work, and explicit stop points.

## Progress Update (2026-03-02)

- [x] PR #985 merged (`2f649d62d164185755c19e33906a7064ae4ff132`): contract-first pack toggles, startup bootstrap visibility, and migration hardening are now on `master`.
- [x] PR #986 merged (`4db3931ac8cbb753da7ff160311a4f8e0d6904a3`): removed planner prompt pack-hint inference (`pack`/`pack_aliases`) to keep Chat planner context generic.

## Audit Update (2026-03-03)

- [x] Decoupling cleanup: AD domain guardrail user hint text no longer hardcodes tool ids (`ad_scope_discovery`/`ad_domain_controllers`).
- [x] Stabilization hotfix: finalize-time host structured next-action replay now rejects stale single-host replays when user/assistant host hints indicate different or multi-host scope.
- [x] Stabilization hotfix: finalize-time scope-shift guard now evaluates raw user intent (instead of routed rewrite payload), reducing stale AD0 replay on contextual compact follow-ups.
- [x] Stabilization hotfix: structured-next-action carryover replay now blocks stale self-loop replays and host-hint-conflicting replays.
- [x] Stabilization hotfix: startup deferred metadata no longer skips metadata sync solely due to initial unauthenticated state.
- [x] Stabilization hotfix: bootstrap progress status can publish while connected startup metadata sync is still active.
- [x] Closed migration gap: `ToolPackBootstrap` now performs descriptor-driven built-in pack discovery (no hardcoded per-pack bootstrap chain).
- [x] Closed migration gap: host-hint helpers moved to `ChatServiceSession.HostHints.cs` (fallback-era naming removed).
- [x] Closed startup perf gap (warm path): server-scoped tooling bootstrap snapshot cache now avoids repeated full bootstrap during reconnect/session churn.
- [x] Stabilization hotfix: carryover structured-next-action replay now accepts compact non-question follow-ups without requiring continuation-expansion text rewrites.
- [x] Stabilization hotfix: duplicate final `chat_result` publishes are suppressed per request/thread/text at the service writer boundary.
- [x] Stabilization hotfix: connected session status now stays in startup-pending mode until metadata/tool-pack readiness settles.
- [x] Stabilization regression: added end-to-end two-turn `go ahead` carryover replay test to prevent follow-up execution stalls.
- [x] Stabilization hotfix: compact follow-up question turns no longer force blocker/cached-evidence finalize rewrites, preserving direct tool-capability answers.
- [x] Stabilization hotfix: cached evidence fallback now requires explicit tool-name match when request text references a concrete tool id.
- [x] Stabilization hotfix: deferred startup metadata sync now waits for authenticated state and is re-queued after login completion.
- [x] Stabilization hotfix: host structured next-action auto-replay now blocks same-tool/same-arguments self-loops (`next_action_self_loop`) to prevent repeated AD0-style churn.
- [x] Stabilization hotfix: carryover structured-next-action auto-replay now suppresses repeated identical tool+args replays until fresh context is provided (or explicit host pin matches).
- [x] Stabilization hotfix: carryover host-hint mismatch detection now consumes assistant-draft host targets as contextual hints, blocking stale single-host replay after multi-host continuation guidance.
- [x] Stabilization hotfix: carryover host-hint gating now rejects single-host auto-replay whenever follow-up context contains multi-host hints (including mixed stale/fresh host mentions).
- [x] Stabilization hotfix: carryover replay freshness/scope-shift guards now read raw user request text (assistant draft host hints remain mismatch-only), preventing repeated AD0 replay caused by assistant-draft host echo.
- [x] Stabilization hardening: carryover replay now uses separate replay-intent and host-hint inputs (no in-band text marker coupling), reducing marker-collision risk while keeping assistant-draft mismatch safeguards.
- [x] Startup stabilization hotfix: transient reconnects now preserve interactive auth state when appropriate (authenticated or login-in-progress without explicit unauthenticated probe), reducing sign-in/connect churn.
- [x] Startup visibility hotfix: connect stage now publishes per-attempt retry/timeout/delay progress status during pipe-connect retries.
- [x] Validation checkpoint: `analyze validate-catalog` currently reports `pass (0 error(s), 0 warning(s))` on this branch.
- [x] Stabilization hotfix: contextual follow-up detection now reads the `Follow-up:` tail from legacy continuation expansion before carryover replay decisions.
- [x] Stabilization cleanup: removed standalone lowercase `ad` lexical alias auto-routing from domain-intent signal resolution.
- [x] Stabilization hotfix: continuation subset reuse now skips when follow-up explicitly references a tool outside the remembered subset, enabling fresh cross-pack tool routing.
- [x] Startup visibility hotfix: header chip diagnostics now maintain a bounded runtime lifecycle timeline (tooltip + debug panel) across connect/auth/bootstrap transitions.
- [x] Stabilization hotfix: routing-meta activity timeline labels now include strategy + selected/total tool counts for explicit route-stage observability.
- [x] Stabilization hotfix: explicit tool-capability questions now bypass finalize-time execution-blocker cached-evidence substitution, preventing stale evidence fallbacks on `tool_name?` clarification turns.
- [x] Stabilization hotfix: carryover structured-next-action auto-replay now skips compact contextual scope-shift follow-ups (for example "other DCs"), forcing fresh routing instead of stale single-host replay.
- [x] Startup perf hotfix: plugin duplicate packs are now skipped via loaded-assembly fast-path before dependency preload/reflection, cutting avoidable bootstrap latency on first metadata sync.
- [x] Stabilization hotfix: explicit tool-id follow-ups now bypass pending-action/carryover auto-replay rewrites, and escaped Markdown tool ids (for example `eventlog\_evtx\_query`) are honored by cached-evidence gating.
- [x] Startup resilience hotfix: deferred metadata sync phases now retry once on transient disconnects (`hello/list_tools/auth_refresh`), reducing cold-start states where runtime is connected but catalog/policy sync is missing.
- [x] Startup/turn UX hotfix: final assistant replacement now targets the latest assistant row when only `System/Tools` rows followed (no newer user turn), reducing duplicate assistant finals across reconnect/retry churn.
- [x] Stabilization hotfix: carryover structured-next-action replay now evaluates compact follow-up eligibility from raw user text (instead of routed payload rewrite text), restoring queued `go ahead` follow-up execution.
- [x] Stabilization hotfix: domain-intent payload parsing now safely handles invalid UTF-16 input (`ArgumentException` + `JsonException`) to avoid compact follow-up expansion crashes.
- [x] Contract-alignment cleanup: updated routing/output lifecycle tests to match strict routing metadata requirements and one-meaningful-final-per-request output policy.
- [x] Startup stability hotfix: deferred startup metadata sync now reruns when login success arrives during an in-flight metadata sync, preventing dropped post-login `hello/list_tools/auth_refresh` refreshes and stale tool visibility.
- [x] Stabilization hotfix: continuation subset follow-ups now treat escaped Markdown tool ids as explicit tool references, preventing stale subset reuse when users switch to tools outside the remembered subset.
- [x] Stabilization regression coverage: startup metadata rerun scheduling now includes dedicated dispatch-gating tests for shutdown/connectivity safety.
- [x] Stabilization hotfix: quoted/multiline tool-descriptor references now keep explicit tool-capability routing (no finalize-time cached-evidence rewrite), and explicit tool-id extraction now strips invisible Unicode format chars to keep descriptor parsing robust.
- [x] Startup/dispatch stabilization hotfix: app turn dispatch startup/send transitions now claim lifecycle state atomically under lock, preventing sign-in/manual-send double-dispatch races and duplicate assistant finals.
- [x] Stabilization hotfix: contextual compact follow-up questions now block stale single-host carryover replay when thread evidence is multi-host, while short acknowledgement questions stay replay-eligible.
- [x] Startup UX hotfix: login-completed status now queues deferred startup metadata sync before publishing connected status, avoiding transient ready-state flicker while startup sync is still pending.
- [x] Startup UX hotfix: bootstrap progress emitted while already connected now keeps `Runtime connected...` phrasing (plus `cause metadata_sync`) instead of regressing to `Starting runtime...` wording.
- [x] Stabilization regression coverage: finalize host scope-shift user-request resolution now has explicit tests proving raw user intent takes precedence over routed rewrite text.
- [x] Live strict scenario validation: `ad-ad0-then-all-dcs-followthrough-10-turn` passes end-to-end with cross-DC fanout and strict call/output pairing.
- [x] Live strict scenario validation: `ad-eventlog-tool-capability-followthrough-10-turn` passes end-to-end and explicitly blocks cached-evidence fallback responses for direct `eventlog_evtx_query` capability questions.
- [x] Stabilization hotfix: domain-intent action catalog now preserves all declared same-family action ids as valid `/act` aliases independent of definition order; canonical family action ids are deterministic and ambiguous cross-family ids do not use first-wins suppression.
- [x] Live strict scenario validation: transcript-derived `ad-other-dcs-go-ahead-followthrough-10-turn` passes end-to-end, covering continuation-style `go ahead` execution across multiple DC hosts and explicit `eventlog_evtx_query` capability follow-ups.
- [x] Scenario-contract hardening: host scenario contracts now support forbidden tool-input values (`forbid_tool_input_values` / `forbidden_tool_inputs`) and enforce them during retry repair, fallback host patching, and assertion evaluation.
- [x] Transcript guardrail hardening: `ad-other-dcs-go-ahead-followthrough-10-turn` continuation turns now include explicit non-AD0 host exclusions, and catalog strictness tests lock those exclusions.
- [x] Transcript-derived strict scenario seed added: `ad-domainwide-reboot-followthrough-10-turn` (AD0 reboot baseline -> non-AD0 domain-wide continuation + explicit `eventlog_evtx_query` capability question + DNS cross-pack turn).
- [x] Forbidden-input equivalence hardening: scenario contract enforcement now treats short-host and FQDN forms as equivalent when applying forbidden host targets (for example `AD0` blocks `AD0.ad.evotec.xyz`) across repair/fallback/assertion paths.
- [x] Live strict rerun passed: `ad-domainwide-reboot-followthrough-10-turn` now completes `10/10` turns with non-AD0 continuation turns preserving host exclusions after input-repair fallback.
- [x] Startup/send race hardening: manual resend now skips enqueue when an equivalent queued-after-login prompt is already in-flight, reducing duplicate assistant replies after switch-account recovery.
- [x] Transcript phrase lock-in: strict cross-DC follow-through scenarios now include "`those are correct DCs, go ahead`" continuation wording to exercise replay suppression under real-world follow-up phrasing.
- [x] Stabilization hotfix: domain host-scope guardrail now blocks stale single-host AD-scope replay on compact scope-shift follow-ups when thread evidence is multi-host, unless a single host is explicitly pinned by the user.
- [x] Stabilization regression coverage: domain host-scope guardrail now has explicit compact scope-shift replay tests (block stale replay, allow explicit host pin, allow short acknowledgement question).
- [x] Live strict validation rerun: transcript-derived follow-through scenarios (`ad-other-dcs-go-ahead-followthrough-10-turn`, `ad-domainwide-reboot-followthrough-10-turn`, `ad-ad0-then-all-dcs-followthrough-10-turn`) pass end-to-end (`10/10`) after this hardening.
- [x] Stabilization hotfix: `ad_monitoring_probe_run` ADWS port normalization now keeps default `9389` when non-positive `port` is supplied, preventing false endpoint probes on `:1`.
- [x] Transcript-derived strict scenario added: `ad-ldap-go-ahead-followthrough-8-turn` to lock continuation execution from scope confirmation into explicit LDAP diagnostics after compact `go ahead`.
- [x] Live strict validation: `ad-ldap-go-ahead-followthrough-8-turn` passes end-to-end (`8/8`) and asserts ADWS endpoint probes do not regress to `:1/ActiveDirectoryWebServices`.
- [x] Startup/send dedupe hardening: queued-after-login suppression now treats both-missing-conversation-id startup prompts as equivalent when normalized text matches, reducing duplicate post-login greeting replies.
- [x] Startup/send regression coverage expanded: queue dedupe tests now include explicit both-missing-conversation-id cases for in-flight queued-after-login manual-resend suppression.
- [x] Stabilization hotfix: weighted/planner subset routing now retains explicitly requested tool ids (including escaped markdown ids) in candidate selection, preventing false "tool inactive" responses for registered tools during follow-up turns.
- [x] Regression coverage expanded: planner/routing tests now lock explicit escaped tool-id retention in weighted subset selection and ensure planner minimum-selection backfill replaces non-explicit tools at limit when needed.
- [x] Live strict validation rerun: `ad-eventlog-tool-capability-followthrough-10-turn` passes end-to-end (`10/10`) after explicit tool-id subset retention hardening.
- [x] Scenario-contract clarity hardening: host scenario execution contracts/retry prompts now emit forbidden input directives as `not-in [..]` (parser remains backward-compatible with legacy `!=` syntax) to avoid non-AD0 constraint inversion during repair.
- [x] Transcript replay guardrail scenario added and validated: `ad-other-dcs-transcript-replay-guardrail-10-turn` passes `10/10`, proving cross-DC continuation execution and explicit non-AD0 follow-up behavior under transcript wording.
- [x] Transcript fanout guardrail scenario added and validated: `ad-c400-transcript-cross-dc-fanout-10-turn` passes `10/10`, proving explicit non-AD0 4-host fanout after continuation wording that previously regressed into AD0-only replay loops.
- [x] Startup visibility hardening: startup/connect/reconnect status text now emits structured context tokens (`phase startup_*`, `cause ...`) and connected bootstrap rewrites legacy cause-only suffixes into phase+cause form, so "runtime connected" no longer hides in-flight startup work.
- [x] Stabilization hotfix: no-text tool-output turns now run one direct no-tools synthesis retry before fallback (service + host), reducing stalled follow-through when tool rounds return empty assistant text.
- [x] Follow-through quality hardening: no-text synthesis prompts now include compact executed tool-argument context (generic key/value summaries) to keep target/scope details available during retry synthesis.
- [x] Startup UX hardening: header status chip fallback now consumes structured startup phase/cause context to render compact in-progress labels (`Loading tool packs`, `Sign in to continue loading tool packs`, `Starting runtime (retrying connection)`).
- [x] Live strict rerun checkpoint after this batch: `ad-c400-transcript-cross-dc-fanout-10-turn` (`10/10`), `ad-eventlog-tool-capability-followthrough-10-turn` (`10/10`), `ad-ldap-go-ahead-followthrough-8-turn` (`8/8`) all pass.
- [x] Host fallback decoupling cleanup: removed host runtime hardcoded tool-specific retry transforms (`ApplyAdDiscoveryRootDseFallback`, `ApplyAdReplicationProbeFallback`, `ApplyDomainDetectiveSummaryTimeoutFallback`) and added architecture guardrail coverage to block reintroduction.
- [x] Typed-surface guardrail expansion: `SourceGuardrailTests` now scans typed-pipeline tool wrappers pack-wide and fails if refactored tools reintroduce ad-hoc `arguments?.Get...`/`arguments.Get...` parsing.
- [x] Typed-envelope increment: `ad_scope_discovery` migrated to `ToolResultV2` success/error envelope path and included in typed-wrapper guardrail enforcement list.
- [x] Typed-envelope base hardening: `ActiveDirectoryToolBase*` shared helpers now emit `ToolResultV2` envelopes and are protected by guardrail coverage preventing direct `ToolResponse` regressions.
- [x] Startup runtime-connect visibility increment: service emits `[startup] provider_connect_progress` phase/status/elapsed telemetry for runtime-provider connect attempts, and app status parsing publishes those lines (including send-time override) so first-turn connect stalls are diagnosable.
- [x] Decoupling guardrail increment: Chat architecture tests now block hardcoded tool-pack ids from reappearing in runtime app/service source (`testimox`, `active_directory`, `adplayground`, `domaindetective`, `dnsclientx`, `reviewer_setup`).

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
- [x] Add guardrail to flag ad-hoc direct argument parsing in target packs.
- [x] Standardize on `ToolResultV2` for migrated paths.
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
- [x] Keep startup status truthful: do not surface "ready" semantics until metadata/auth probes settle or explicitly fail-open with reason.
- [x] Replace hardcoded known-pack bootstrap chain with descriptor/manifest-driven registration.
- [x] Remove/rename fallback-era host-hint file so architecture guardrails match current source layout.
- [x] Add regression tests for reconnect warm path and multi-turn follow-up carryover against host scope changes.

### PR 13 - Follow-Up Execution Reliability + Startup Churn Visibility

Files (expected):

- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ToolRouting.DomainIntentAffinity.cs`
- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ChatRouting.NoExtractedFinalize.cs`
- `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.Messaging.Connection.cs`
- `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.StartupReadiness.cs`
- `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceRoutingTrimTests.*`

Checklist:

- [x] Escape continuation subset reuse for language-neutral tool-capability question turns (even without explicit tool-id literals) to restore cross-pack follow-up awareness.
- [x] Extend startup status/debug timeline with churn cause labels (`auth_wait`, `pipe_retry`, `metadata_retry`, `runtime_disconnect`) so reconnect loops are diagnosable from UI alone.
- [x] Add finalize-path regression that proves contextual follow-up scope shifts are evaluated from raw user intent and cannot replay stale single-host next actions.
- [x] Validate with targeted chat tests + catalog validation before PR open.

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
