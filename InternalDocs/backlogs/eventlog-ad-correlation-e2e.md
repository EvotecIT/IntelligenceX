# EventLog to AD Correlation E2E TODO

## Scope

- Build a reusable EventLog -> AD correlation flow with no hardcoded incident templates.
- Keep EventLog tools read-only and correlation-driven.
- Make AD-side consumption of EventLog handoff data first-class.

## Shipped Baseline

- [x] `eventlog_named_events_catalog` and `eventlog_named_events_query` are available.
- [x] `eventlog_timeline_explain` and `eventlog_timeline_query` are available.
- [x] `meta.entity_handoff` is emitted from EventLog timeline/named-event outputs.
- [x] Correlation profiles are generic (`identity`, `actor_activity`, `object_activity`, `host_activity`, `rule_activity`).

## Definition Of Done

- [ ] End-to-end test proves `eventlog_named_events_query` output handoff can be consumed by AD helper input without prompt glue.
- [ ] End-to-end test proves `eventlog_timeline_query` output handoff can be consumed by AD helper input without prompt glue.
- [ ] AD helper output is deterministic, capped, and structured for direct AD tool calls.
- [ ] EventLog pack guidance and AD pack guidance both document the same handoff flow.
- [ ] Backlogs/TODOs are synced with only true remaining work.

## PR Batch Plan

- [ ] Batch 1: add contract tests for `eventlog_named_events_query` `meta.entity_handoff` shape, caps, and required metadata.
- [ ] Batch 2: add contract tests for `eventlog_timeline_query` `meta.entity_handoff` shape, caps, and required metadata.
- [ ] Batch 3: add an AD helper tool that accepts EventLog handoff candidates and returns normalized AD-ready identities/computers.
- [ ] Batch 4: add EventLog -> AD helper -> AD query argument end-to-end tests.
- [ ] Batch 5: add docs/pack-guidance examples for local and remote EventLog -> AD correlation runs.

## Open Decisions

- [ ] Pick helper tool name: `ad_handoff_prepare` or `ad_identity_candidates_prepare`.
- [ ] Pick helper location: `IntelligenceX.Tools.ADPlayground` or shared utility namespace.
- [ ] Decide helper input contract: require `meta.entity_handoff` only or allow raw EventLog rows fallback.
