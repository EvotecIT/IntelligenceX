---
title: Tool Catalog
description: Real IntelligenceX tool packs and representative tools from current source.
---

# Tool Catalog

This catalog is generated from what the codebase currently registers in tool-pack extension methods.

## Runtime Availability Matrix

| Pack | Descriptor ID | Source kind | Tier | Typical availability |
|---|---|---|---|---|
| Event Log (EventViewerX) | `eventlog` | `builtin` | SensitiveRead | OSS + internal |
| File System | `fs` | `builtin` | ReadOnly | OSS + internal |
| Reviewer Setup | `reviewersetup` | `builtin` | ReadOnly | OSS + internal |
| Email (Mailozaurr) | `email` | `builtin` | SensitiveRead | OSS + internal (dependency-gated at runtime) |
| Office Documents (OfficeIMO) | `officeimo` | `open_source` | ReadOnly | OSS + internal (dependency-gated at runtime) |
| PowerShell Runtime | `powershell` | `builtin` | DangerousWrite | OSS + internal (opt-in by policy) |
| ComputerX | `system` | `closed_source` | ReadOnly | Internal/private builds |
| ADPlayground | `ad` | `closed_source` | SensitiveRead | Internal/private builds |
| TestimoX | `testimox` | `closed_source` | SensitiveRead | Internal/private builds |

`Tier` is a pack-summary classification.
Tool-level contracts remain authoritative for whether a given tool is read-only, probe-aware, or write-capable.
Mixed-mode governed-write examples in the current runtime include `eventlog_channel_policy_set` / `eventlog_classic_log_ensure` / `eventlog_classic_log_remove` in `eventlog`, `system_service_lifecycle` / `system_scheduled_task_lifecycle` in `system`, and `ad_user_lifecycle` / `ad_computer_lifecycle` in `active_directory`.

## Builtin / OSS-Oriented Packs

### Event Log (`eventlog`)

Representative tools:

- `eventlog_pack_info`
- `eventlog_channels_list`
- `eventlog_channel_policy_set`
- `eventlog_classic_log_ensure`
- `eventlog_classic_log_remove`
- `eventlog_live_query`
- `eventlog_evtx_find`
- `eventlog_evtx_query`
- `eventlog_evtx_report_user_logons`

### File System (`fs`)

Representative tools:

- `fs_pack_info`
- `fs_list`
- `fs_read`
- `fs_search`

### Reviewer Setup (`reviewersetup`)

Representative tools:

- `reviewer_setup_pack_info`
- `reviewer_setup_contract_verify`

### Email (`email`)

Representative tools:

- `email_pack_info`
- `email_imap_search`
- `email_imap_get`
- `email_smtp_send`

### PowerShell (`powershell`)

Representative tools:

- `powershell_pack_info`
- `powershell_environment_discover`
- `powershell_hosts`
- `powershell_run`

### Office Documents (`officeimo`)

Representative tools:

- `officeimo_pack_info`
- `officeimo_read`

## Closed-Source / Internal Packs

### ADPlayground (`ad`)

Representative tools:

- `ad_pack_info`
- `ad_environment_discover`
- `ad_user_lifecycle`
- `ad_computer_lifecycle`
- `ad_domain_info`
- `ad_group_members`
- `ad_users_expired`
- `ad_search`

### ComputerX (`system`)

Representative tools:

- `system_pack_info`
- `system_info`
- `system_service_lifecycle`
- `system_scheduled_task_lifecycle`
- `system_process_list`
- `system_network_adapters`
- `system_service_list` (Windows)
- `system_firewall_rules` (Windows)

### TestimoX (`testimox`)

Representative tools:

- `testimox_pack_info`
- `testimox_rules_list`
- `testimox_rules_run`

## Notes

- Tool availability depends on host/runtime composition and platform.
- `builtin` packs are OSS-oriented by source model; "optional" indicates runtime policy/dependency gating, not closed-source licensing.
- `sourceKind` classification comes from `ToolPackBootstrap` normalization rules.
- Closed-source packs may be enabled by default in config but still absent in OSS environments.
- Closed-source packs are private/licensed by default for IX Chat usage. External/custom-host usage requires a separate license.

## Related

- [IX Tools Overview](/docs/tools/overview/) - Source model and default bootstrap behavior
- [IX Chat Architecture](/docs/chat/architecture/) - Host process + pack registration flow
- [Tool Calling](/docs/library/tools/) - Using packs in custom .NET applications
