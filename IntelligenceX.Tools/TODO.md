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
- [x] Extend AD handoff helper output with first-class `ad_scope_discovery` target arguments (`discovery_fallback`, derived domain, DC seeds).
- [x] Keep detailed checklist in `InternalDocs/backlogs/eventlog-ad-correlation-e2e.md`.
- [x] Keep current EventViewerX/ADPlayground coverage map in `InternalDocs/backlogs/eventlog-ad-tool-gap-matrix.md`.

## Remaining Platform Tasks

- [x] Add AD query wrappers for service-account and dormant-account hygiene (`ad_service_account_usage`, `ad_never_logged_in_accounts`) plus gMSA readiness (`ad_kds_root_keys`) with safe caps and structured JSON.
- [x] Add AD GPO diagnostics wrappers for WMI filters and WSUS posture (`ad_wmi_filters`, `ad_wsus_configuration`) with safe caps and structured JSON.
- [ ] Implement remaining `IntelligenceX.Tools.EventLog` EVTX parsing tools (EventViewerX) with safe-by-default limits and structured JSON output.
- [ ] Implement remaining `IntelligenceX.Tools.ADPlayground` query tools (ADPlayground) with safe-by-default limits and structured JSON output (see `InternalDocs/backlogs/eventlog-ad-tool-gap-matrix.md` for current shortlist).
- [ ] Decide packaging split for Windows-only capabilities (separate `net*-windows` pack vs runtime OS checks).
