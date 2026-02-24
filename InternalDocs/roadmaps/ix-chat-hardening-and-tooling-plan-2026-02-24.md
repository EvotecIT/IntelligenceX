# IX Chat + IX Tools Hardening Plan (2026-02-24)

Status: in progress  
Last validated: 2026-02-24

## Scope
- Harden IX Chat for long-running, tool-heavy conversations without partial endings.
- Keep UX conversational by default, with optional debug visibility for draft/thinking/trace channels.
- Keep open-source DNS/domain tooling first-class and reusable when closed-source packs are disabled.

## Validation Snapshot (Current)
- Scenario strict guard rails are implemented and available:
  - `assert_tool_call_output_pairing`
  - `assert_no_duplicate_tool_call_ids`
  - `assert_no_duplicate_tool_output_call_ids`
  - `max_no_tool_execution_retries`
  - `max_duplicate_tool_call_signatures`
  - `assert_clean_completion`
- Scenario-level defaults (`defaults`) are implemented in host scenario parsing.
- Scenario artifact output now emits markdown plus JSON with per-turn transcript and tool ledger.
- Scenario scripts added:
  - `Build/Run-ChatScenarioSuite.ps1`
  - `Build/Run-ChatLiveConversation.ps1`
  - `Build/Run-ChatQualityPreflight.ps1`
- Existing AD scenarios were made strict by default and one new cross-DC continuation scenario was added.
- Added catalog-level strictness test coverage for `ad-*-10-turn.json` to enforce strict defaults and 10-turn shape.
- Scenario suite now supports scenario-tag filtering (`-Tags`) and preflight forwards tags via `-ScenarioTags`.
- Added live-suite runner (`Build/Run-ChatLiveConversationSuite.ps1`) so local live validation can run tag-driven batches without hardcoded single-scenario defaults.
- Preflight now supports `-RunLiveHarnessSuite` for the same tag-driven live validation path.
- App already supports debug toggles for turn trace and draft bubbles with distinct rendering channels.

## Scenario Coverage (Current)
- `ad-reboot-local-10-turn.json`: local DC reboot evidence path.
- `ad-replication-health-10-turn.json`: replication-first troubleshooting flow.
- `ad-cross-dc-followthrough-10-turn.json`: AD0-first then continue all DCs in same turn (anti-stuck guard).
- `ad-identity-correlation-przemyslaw-10-turn.json`: identity-centric AD correlation.
- `ad-ldap-adws-health-10-turn.json`: LDAP/ADWS service health.
- `ad-user-last-logon-przemyslaw-10-turn.json`: cross-DC user last-logon evidence.

## Confirmed Gaps / Risks
- High: missing explicit end-to-end tests for transport break right after tool call plus delayed/replayed tool output across service and app message handling.
- High: no merge gate yet for strict scenario suite plus live-harness smoke run.
- Medium: routing ambiguity for "domain" tasks between AD directory intent and public DNS/domain intent.
- Medium: no explicit per-model tool-candidate/context budget strategy, which risks degraded routing on long runs.
- Medium: tool count is growing and needs profile-aware pack selection plus compaction policy.

## DNS/Domain Tooling Decision
- Decision: keep `DnsClientX` and `DomainDetective` as separate open-source packs.
- Reason: `DomainDetective` is broad/high-level and already depends on `DnsClientX`; `DnsClientX` should still be directly available for low-level DNS queries.
- Closed-source packs (`ADPlayground`, `ComputerX`, `TestimoX`) remain optional and must not be required for open-source DNS/domain workflows.
- Routing contract:
  - AD domain intent routes to AD pack tools (`ad_*`) when user asks about DCs/LDAP/GPO/replication/security posture.
  - Public DNS/domain intent routes to `dnsclientx_*`/`domaindetective_*` when user asks about DNS records, MX/SPF/DMARC, external domain health, public resolution.
  - If ambiguity remains, perform one clarifying turn instead of silent misrouting.

## Workstreams

### WS1: Transport-Break Recovery Hardening
Status: in_progress  
Effort: M  
Risk: High

Acceptance:
- Add tests for drop-after-tool-call, delayed/replayed outputs, and call/output mismatch replay.
- Enforce one recovery path, no duplicate bubbles, no orphan tool outputs, clean final assistant response.

Progress:
- Expanded model-phase retry classification for tool call/output pairing reference gaps:
  - `No tool output found for function call ...`
  - `No tool output found for custom tool call ...`
  - `No tool call found for function call output ...`
- Added regression coverage in `ChatSchemaRecoveryFallbackTests` for direct + nested function-call output pairing gaps.
- Added optional local gate in `Build/Run-ChatQualityPreflight.ps1`:
  - `-RunRecoveryUnitTests` runs targeted recovery assertions (`ChatSchemaRecoveryFallbackTests` + `HostScenarioAssertionTests`).
- Refactored tool-round replay input construction into one helper (`BuildToolRoundReplayInput`) and added tests that verify:
  - duplicate replay outputs are deduplicated by normalized `call_id`
  - orphan/unresolvable outputs are skipped
  - indexed fallback works when raw output `call_id` is mismatched
- Hardened delayed-output replay selection:
  - replay now prefers explicit `call_id` matches over earlier index-fallback matches for the same call
  - avoids stale fallback output winning when a delayed correctly-labeled output arrives later
  - covered by `BuildToolRoundReplayInput_PrefersExplicitCallIdMatchOverIndexedFallbackForDelayedOutput`
- Added app-side interim snapshot dedupe guard for reconnect/overlap cases:
  - interim append decision now checks normalized text-equivalence/near-duplicate against latest assistant text
  - prevents duplicate assistant bubbles when reconnect emits restated interim snapshots
  - covered by `MainWindowNoTextWarningHandlingTests`
- Added reconnect/final overlap boundary tests:
  - short suffix-only provisional/final differences stay replace-only (no duplicate bubble)
  - long suffix synthesized finals still append as a new final bubble
- Hardened tool replay output selection for delayed/replayed duplicates:
  - when multiple explicit outputs for the same `call_id` arrive, replay now uses the latest explicit output
  - direct `call_id` matches still outrank indexed fallback matches
- Extended preflight recovery unit tests to include app duplicate-bubble guards:
  - `MainWindowNoTextWarningHandlingTests`

### WS2: Live Scenario Expansion (No Hardcoding)
Status: in_progress  
Effort: M  
Risk: Medium

Acceptance:
- Keep prompts only in scenario files.
- Add at least 3 new 10-turn scenarios:
  - AD0-first then all-DC follow-through under partial failures.
  - Mixed AD + EventLog correlation with retries.
  - Long continuation run that verifies no partial ending.
- Persist and compare JSON artifacts.

Progress:
- Added scenarios:
  - `ad-eventlog-correlation-partial-failures-10-turn.json`
  - `ad-long-continuation-no-partials-10-turn.json`
  - `ad-transport-recovery-no-duplicate-replay-10-turn.json`
- Added JSON artifact diff helper:
  - `Build/Compare-ChatScenarioReports.ps1`
- Added coverage summary helper:
  - `Build/Get-ChatScenarioCoverage.ps1`
- Added scenario tag taxonomy (`ad`, `strict`, `continuation`, `cross-dc`, `eventlog`, etc.) and suite tag filtering.
- Added live harness suite runner (tag-driven, repeatable) for real auth/tool runs without hardcoded single-scenario selection.
- Hardened scenario duplicate-call detection by canonicalizing tool argument JSON before signature comparison
  (so key-order-only differences still count as duplicate signatures).

### WS3: UI Debug Visibility Modes
Status: mostly_done  
Effort: S  
Risk: Low

Acceptance:
- Keep default transcript conversational.
- Keep independent toggles for draft, thinking/tool activity, and turn trace.
- Keep distinct styling per bubble channel.

### WS4: Open-Source DNS/Domain Packs
Status: in_progress  
Effort: L  
Risk: Medium

Acceptance:
- Add dedicated `dnsclientx` pack with focused DNS query/record tools.
- Add dedicated `domaindetective` pack with domain health + network diagnostics (including ping/traceroute).
- Add pack-info routing hints that disambiguate AD-domain vs public-domain tasks.
- Keep pack IDs and metadata normalized and collision-safe.

Progress:
- Added `dnsclientx` and `domaindetective` as first-class known pack IDs in bootstrap metadata (open-source source-kind, reflection-loaded when assemblies are present).
- Added bootstrap/plugin gating support for these IDs through `ToolPackBootstrapOptions`:
  - `EnableDnsClientXPack`
  - `EnableDomainDetectivePack`
- Added host/service runtime CLI toggles to control these packs per run:
  - `--enable/--disable-dnsclientx-pack`
  - `--enable/--disable-domaindetective-pack`
- Added runner-script pass-through switches for scenario/live/preflight harnesses:
  - `-Enable/-DisableDnsClientXPack`
  - `-Enable/-DisableDomainDetectivePack`
- Added metadata regression test coverage for disabled-by-configuration behavior and source-kind classification.

### WS5: Tool Count, Context Budget, Compaction
Status: pending  
Effort: L  
Risk: High

Acceptance:
- Add per-model profile budgets:
  - max candidate tools per turn
  - max replay transcript/tool payload
  - max retained tool rounds before compaction
- Add deterministic compaction preserving `call_id`, failure/error envelopes, and evidence receipts.
- Add long-run tests validating no partial endings under compaction.

### WS6: Merge Gates
Status: in_progress  
Effort: S-M  
Risk: Low

Acceptance:
- Wire strict scenario suite + one live harness smoke into PR validation or required local preflight.
- Fail merges on scenario quality regressions.

Progress:
- Added `Build/Run-ChatQualityPreflight.ps1` to run strict scenario suite in one command with optional live harness smoke.
- Added optional transport-recovery profile in preflight:
  - `-RunTransportRecoveryProfile` runs an additional strict tag-gated pass (`ad,strict,transport-recovery`).
- Expanded preflight recovery-unit test filter to include replay helper coverage (`BuildToolRoundReplayInput_*`).

## Assignable Backlog (Parallel Branch Ready)
- `A1` (WS1, `ix-chat-transport-recovery-<id>`, Effort M, Risk High): add transport-break E2E tests for drop-after-tool-call, delayed output, replay mismatch; assert one recovery path, no duplicate bubbles, no orphan outputs, clean final answer.
- `A2` (WS2, `ix-chat-live-scenarios-<id>`, Effort M, Risk Medium): add 3 more 10-turn live scenarios for AD0-first follow-through, mixed AD+EventLog retry recovery, and long continuation with strict no-partials.
- `A3` (WS4, `ix-tools-dns-open-packs-<id>`, Effort L, Risk Medium): onboard `DnsClientX` pack as standalone OSS DNS tools and `DomainDetective` pack as standalone OSS domain diagnostics with explicit pack-info hints.
- `A4` (WS4, `ix-tools-dns-open-packs-<id>`, Effort S-M, Risk Medium): add router disambiguation contract for "domain" ambiguity (AD domain vs public DNS/domain) with one clarifying turn on ambiguous intent.
- `A5` (WS5, `ix-chat-context-compaction-<id>`, Effort L, Risk High): implement per-model context/tool budgets and deterministic compaction preserving `call_id` evidence chains.
- `A6` (WS6, `ix-chat-merge-gates-<id>`, Effort S, Risk Low): wire `Run-ChatQualityPreflight.ps1` into PR validation and fail on strict scenario regressions.
- `A7` (WS3, `ix-chat-ui-debug-modes-<id>`, Effort S, Risk Low): validate distinct bubble styling and hide/show behavior for thinking, draft, and trace channels on desktop + mobile-size windows.

## Parallel Branch Plan
- Branch A: `ix-chat-transport-recovery-<id>`
  - Ownership: service/app recovery contracts, replay/transport-break tests.
- Branch B: `ix-chat-live-scenarios-<id>`
  - Ownership: scenario additions, harness checks, artifact diff helper.
- Branch C: `ix-tools-dns-open-packs-<id>`
  - Ownership: `dnsclientx` + `domaindetective` pack onboarding and routing metadata.
- Branch D: `ix-chat-context-compaction-<id>`
  - Ownership: tool budget, compaction policy, long-run resilience tests.

## Immediate TODO (Execution Order)
- 1. Land strict scenario + harness branch changes (already prepared).
- 2. Add transport-break E2E coverage and duplicate/orphan bubble prevention assertions.
- 3. Add DNS/domain open-source packs and routing disambiguation metadata.
- 4. Add context budget + compaction controls with long-run tests.
- 5. Add merge gate wiring for scenario suite + live smoke.

## Tracking
- Use `pending`, `in_progress`, `done` markers in each workstream.
