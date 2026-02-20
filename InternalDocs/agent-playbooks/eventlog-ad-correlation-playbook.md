# EventLog to AD Correlation Playbook

This playbook documents reusable EventLog -> AD investigation flows without hardcoded incident templates.

## Goal

- Use EventLog tools to collect evidence.
- Build generic timeline/correlation views.
- Normalize `meta.entity_handoff` via `ad_handoff_prepare`.
- Resolve identities in AD with `ad_object_resolve` and targeted `ad_search`.

## Flow A: Local EVTX Correlation

1. Find EVTX files.

```json
{
  "tool": "eventlog_evtx_find",
  "arguments": {
    "max_results": 20
  }
}
```

2. Run named-event detections from EVTX-derived context and build timeline correlation.

```json
{
  "tool": "eventlog_timeline_query",
  "arguments": {
    "named_events": ["ad_user_logon", "ad_failed_logon"],
    "correlation_profile": "identity",
    "time_period": "last_24_hours",
    "max_events": 300
  }
}
```

3. Prepare AD-ready identities from EventLog handoff metadata.

```json
{
  "tool": "ad_handoff_prepare",
  "arguments": {
    "entity_handoff": "<eventlog_timeline_query.meta.entity_handoff>",
    "include_computers": true,
    "max_identities": 100
  }
}
```

4. Resolve identities in bulk, then drill into specific records.

```json
{
  "tool": "ad_object_resolve",
  "arguments": {
    "identities": "<ad_handoff_prepare.target_arguments.ad_object_resolve.identities>",
    "identity_kind": "auto"
  }
}
```

```json
{
  "tool": "ad_search",
  "arguments": {
    "query": "<ad_handoff_prepare.target_arguments.ad_search.identity>",
    "kind": "any",
    "max_results": 25
  }
}
```

## Flow B: Remote Live Event Correlation

1. Validate channel access on the remote host and triage quickly.

```json
{
  "tool": "eventlog_top_events",
  "arguments": {
    "machine_name": "dc01.contoso.local",
    "log_name": "Security",
    "max_results": 10
  }
}
```

2. Build timeline from remote named events.

```json
{
  "tool": "eventlog_timeline_query",
  "arguments": {
    "machine_name": "dc01.contoso.local",
    "categories": ["security"],
    "correlation_profile": "actor_activity",
    "time_period": "last_24_hours",
    "max_events": 400
  }
}
```

3. Normalize handoff and resolve in AD.

```json
{
  "tool": "ad_handoff_prepare",
  "arguments": {
    "entity_handoff": "<eventlog_timeline_query.meta.entity_handoff>",
    "include_computers": true
  }
}
```

```json
{
  "tool": "ad_object_resolve",
  "arguments": {
    "identities": "<ad_handoff_prepare.identities>",
    "identity_kind": "auto",
    "max_inputs": 200
  }
}
```

## Flow C: Remote Live Fallback (EVTX Export Path)

Use this when remote channel read fails but EVTX export is available.

1. Ask for EVTX export from the target host to an allowed local root.
2. Run local EVTX flow (`eventlog_evtx_find` -> `eventlog_timeline_query` -> `ad_handoff_prepare` -> AD tools).
3. Keep the same correlation profile and key strategy so comparisons stay consistent.

## Correlation Tuning

- Start with `correlation_profile` (`identity`, `actor_activity`, `object_activity`, `host_activity`, `rule_activity`).
- If grouping is noisy, switch to explicit `correlation_keys`.
- Use `eventlog_timeline_explain` between reruns when key coverage changes.

## Notes

- Keep EventLog tools read-only.
- Keep AD lookups bulk-first (`ad_object_resolve`) to avoid N+1 query loops.
- Preserve `meta.entity_handoff` from EventLog outputs; avoid reconstructing candidate lists in prompt text.
