# TODO

## EventLog + AD Correlation (No Hardcoded Incident Templates)

- [x] EventLog pack ships live and EVTX query/stats/top-event tools with safe limits and structured JSON.
- [x] EventLog pack ships reusable correlation/timeline tools (`eventlog_timeline_explain`, `eventlog_timeline_query`).
- [x] EventLog timeline and named-events outputs emit structured `meta.entity_handoff` candidates.
- [x] EventLog live tools and catalog/listing paths emit explicit remote-vs-local error envelopes with actionable hints.
- [x] Add EVTX security-summary parsing tool (`eventlog_evtx_security_summary`) with safe caps and `meta.entity_handoff`.
- [x] Add end-to-end tests: `eventlog_named_events_query` -> `meta.entity_handoff` -> AD helper input normalization.
- [x] Add end-to-end tests: `eventlog_timeline_query` -> `meta.entity_handoff` -> AD helper input normalization.
- [x] Add a small AD-side helper tool so EventLog handoff candidates are first-class AD inputs (less prompt glue).
- [x] Add AD helper contract tests (schema snapshot, caps/truncation, invalid-input envelopes).
- [x] Update EventLog and AD pack guidance to advertise a single reusable EventLog -> AD flow.
- [x] Add AD scope-discovery helper (`ad_scope_discovery`) with naming contexts, DC/domain scope, and probe receipt output.
- [x] Keep detailed checklist in `InternalDocs/backlogs/eventlog-ad-correlation-e2e.md`.

## Remaining Platform Tasks

- [ ] Implement remaining `IntelligenceX.Tools.EventLog` EVTX parsing tools (EventViewerX) with safe-by-default limits and structured JSON output.
- [ ] Implement remaining `IntelligenceX.Tools.ADPlayground` query tools (ADPlayground) with safe-by-default limits and structured JSON output.
- [ ] Decide packaging split for Windows-only capabilities (separate `net*-windows` pack vs runtime OS checks).
