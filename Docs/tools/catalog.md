---
title: Tool Catalog
description: Real IntelligenceX tool packs and representative tools from current source.
---

# Tool Catalog

This catalog is generated from what the codebase currently registers in tool-pack extension methods.

## Runtime Availability Matrix

| Pack | Descriptor ID | Source kind | Tier | Typical availability |
|---|---|---|---|---|
| Event Log | `eventlog` | `builtin` | SensitiveRead | OSS + internal |
| File System | `fs` | `builtin` | ReadOnly | OSS + internal |
| Reviewer Setup | `reviewer_setup` | `builtin` | ReadOnly | OSS + internal |
| Email | `email` | `builtin` | SensitiveRead | Optional (assembly-dependent) |
| IX.PowerShell | `powershell` | `builtin` | DangerousWrite | Optional (disabled by default) |
| System | `system` | `closed_source` | ReadOnly | Internal/private builds |
| Active Directory | `ad` | `closed_source` | SensitiveRead | Internal/private builds |
| IX.TestimoX | `testimox` | `closed_source` | SensitiveRead | Internal/private builds |

## Builtin / OSS-Oriented Packs

### Event Log (`eventlog`)

Representative tools:

- `eventlog_pack_info`
- `eventlog_channels_list`
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

### Reviewer Setup (`reviewer_setup`)

Representative tools:

- `reviewer_setup_pack_info`
- `reviewer_setup_contract_verify`

### Email (`email`)

Representative tools:

- `email_pack_info`
- `email_imap_search`
- `email_imap_get`
- `email_smtp_send`

### IX.PowerShell (`powershell`)

Representative tools:

- `powershell_pack_info`
- `powershell_environment_discover`
- `powershell_hosts`
- `powershell_run`

## Closed-Source / Internal Packs

### Active Directory (`ad`)

Representative tools:

- `ad_pack_info`
- `ad_environment_discover`
- `ad_domain_info`
- `ad_group_members`
- `ad_users_expired`
- `ad_search`

### System (`system`)

Representative tools:

- `system_pack_info`
- `system_info`
- `system_process_list`
- `system_network_adapters`
- `system_service_list` (Windows)
- `system_firewall_rules` (Windows)

### IX.TestimoX (`testimox`)

Representative tools:

- `testimox_pack_info`
- `testimox_rules_list`
- `testimox_rules_run`

## Notes

- Tool availability depends on host/runtime composition and platform.
- `sourceKind` classification comes from `ToolPackBootstrap` normalization rules.
- Closed-source packs may be enabled by default in config but still absent in OSS environments.

## Related

- [IX Tools Overview](/docs/tools/overview/) - Source model and default bootstrap behavior
- [IX Chat Architecture](/docs/chat/architecture/) - Host process + pack registration flow
- [Tool Calling](/docs/library/tools/) - Using packs in custom .NET applications
