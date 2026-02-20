# EventLog + AD Tool Gap Matrix

Last updated: 2026-02-20

## EventViewerX Report Coverage (IntelligenceX.Tools.EventLog)

| EventViewerX report/API surface | IntelligenceX tool coverage | Status |
| --- | --- | --- |
| `Reports.Evtx.EvtxEventReportBuilder` | `eventlog_evtx_query` | Covered |
| `Reports.Stats.EvtxStatsReportBuilder` | `eventlog_evtx_stats` | Covered |
| `Reports.Security.SecurityEvtxQueryExecutor` (`user_logons`, `failed_logons`, `account_lockouts`) | `eventlog_evtx_security_summary` (`report_kind`) | Covered |
| `Reports.Inventory.EventCatalogQueryExecutor` channels | `eventlog_channels_list` | Covered |
| `Reports.Inventory.EventCatalogQueryExecutor` providers | `eventlog_providers_list` | Covered |
| `Reports.Live.LiveEventQueryExecutor` | `eventlog_live_query` | Covered |
| `Reports.Live.LiveStatsQueryExecutor` | `eventlog_live_stats` | Covered |
| `Reports.Correlation.NamedEventsTimelineQueryExecutor` | `eventlog_timeline_query`, `eventlog_timeline_explain` | Covered |
| Named-event rules (`Enums.NamedEvents`) | `eventlog_named_events_catalog`, `eventlog_named_events_query` | Covered |

EventLog note: report-surface coverage is complete; remaining gaps are feature-parity and operator ergonomics on top of existing wrappers.

## ADPlayground Query Coverage (Recent Additions)

| ADPlayground surface | IntelligenceX tool | Status |
| --- | --- | --- |
| `ManagedServiceAccountUsageAnalyzer.GetUsage(domain)` | `ad_service_account_usage` | Added |
| `InactiveUserDetector.GetNeverLoggedInAccounts(domain, grace)` | `ad_never_logged_in_accounts` | Added |
| `KdsRootKeyChecker.GetRootKeys()` | `ad_kds_root_keys` | Added |
| `Gpo/WmiFilterService.EnumerateFilters(domain)` | `ad_wmi_filters` | Added |
| `Gpo/WsusConfigurationService.Get(domain)` | `ad_wsus_configuration` | Added |
| `Admin/AdminCountReporter.GetReport()` | `ad_admin_count_report` | Added |
| EventLog handoff normalization | `ad_handoff_prepare` | Added |
| Scope/discovery receipt | `ad_scope_discovery` | Added |

## Remaining Read-Tool Candidates (Prioritized)

| Priority | Candidate tool | Upstream source | Why |
| --- | --- | --- | --- |
| P1 | Extend `eventlog_live_query` and `eventlog_evtx_query` with first-class advanced filters | `SearchEvents.QueryLog` / `SearchEvents.QueryLogFile` | expose structured filters (`keywords`, `level`, `user_id`, `event_record_id`, `named_data_filter`, `named_data_exclude_filter`) without forcing XPath-only workflows |
| P2 | Evaluate `eventlog_watcher_*` read-only wrappers | `WatcherManager` / `WatchEvents` | optional long-running watch capability for operations workflows; requires lifecycle/governance contract |

## Write-Tool Candidates (Require Governance Contracts)

| Priority | Candidate tool | Upstream source | Risk class |
| --- | --- | --- | --- |
| P1 | `ad_orphaned_account_disable` | `OrphanedAccountRemediator.DisableAccount` | Mutating identity state |
| P1 | `ad_orphaned_account_delete` | `OrphanedAccountRemediator.DeleteAccount` | Destructive |
| P1 | `ad_wmi_filter_create` | `WmiFilterService.CreateFilter` | Mutating GPO/WMI config |
| P1 | `ad_wmi_filter_update` | `WmiFilterService.UpdateFilter` | Mutating GPO/WMI config |
| P1 | `ad_wmi_filter_remove` | `WmiFilterService.RemoveFilter` | Destructive |
| P1 | `ad_wmi_filter_link` | `WmiFilterService.LinkFilterToGpo` | Mutating policy linkage |

Write-tool guardrails:
- Require standard write-governance metadata.
- Require explicit action intent and reversible plan where possible.
- Prefer "dry_run"/preview support before apply/delete actions.
