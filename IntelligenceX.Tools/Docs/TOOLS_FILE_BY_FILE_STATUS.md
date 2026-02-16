# IntelligenceX.Tools File-by-File Modernization Status

Updated: 2026-02-11

Goal:
- keep tool wrappers thin
- keep engines as source of truth for domain logic
- preserve raw payloads for model reasoning
- keep projection args view-only (`*_view`)
- keep pack guidance (`*_pack_info`) aligned with actual tool registration

Status values:
- `done`: aligned with thin-wrapper model
- `keep`: intentionally remains in Tools layer
- `next`: remaining move/simplification item

## ActiveDirectory
| File | Status | Notes |
|---|---|---|
| `IntelligenceX.Tools.ADPlayground/ActiveDirectoryToolBase.cs` | `keep` | AD wrapper plumbing only. |
| `IntelligenceX.Tools.ADPlayground/ActiveDirectoryToolOptions.cs` | `keep` | AD pack runtime options. |
| `IntelligenceX.Tools.ADPlayground/ActiveDirectoryToolPack.cs` | `done` | Pack descriptor/registration only. |
| `IntelligenceX.Tools.ADPlayground/ToolRegistryActiveDirectoryExtensions.cs` | `done` | Single registration source; exposes tool names and runtime-derived tool catalog metadata. |
| `IntelligenceX.Tools.ADPlayground/AdPackInfoTool.cs` | `done` | Model-facing pack guidance via shared contract (tools + tool catalog + capabilities + flow steps). |
| `IntelligenceX.Tools.ADPlayground/AdDynamicTableView.cs` | `keep` | Thin AD adapter for dynamic LDAP bags; generic projection logic lives in `Tools.Common`. |
| `IntelligenceX.Tools.ADPlayground/AdQueryResultHelpers.cs` | `keep` | AD failure mapping + small AD result helpers. |
| `IntelligenceX.Tools.ADPlayground/AdSearchTool.cs` | `done` | Engine call + dynamic view helper only. |
| `IntelligenceX.Tools.ADPlayground/AdSearchFacetsTool.cs` | `done` | Engine facets result pass-through + facts summary. |
| `IntelligenceX.Tools.ADPlayground/AdObjectGetTool.cs` | `done` | Engine object-get result pass-through + facts summary. |
| `IntelligenceX.Tools.ADPlayground/AdObjectResolveTool.cs` | `done` | Engine resolve pass-through + auto-column projection via shared table envelope. |
| `IntelligenceX.Tools.ADPlayground/AdGroupsListTool.cs` | `done` | Engine list + dynamic view helper only. |
| `IntelligenceX.Tools.ADPlayground/AdGroupMembersTool.cs` | `done` | Engine member listing wrapper. |
| `IntelligenceX.Tools.ADPlayground/AdGroupMembersResolvedTool.cs` | `done` | Engine resolved-members wrapper + dynamic view helper. |
| `IntelligenceX.Tools.ADPlayground/AdDomainControllersTool.cs` | `done` | Engine domain controllers wrapper + dynamic view helper. |
| `IntelligenceX.Tools.ADPlayground/AdDomainInfoTool.cs` | `done` | Engine domain info wrapper + shared facts summary. |
| `IntelligenceX.Tools.ADPlayground/AdWhoAmITool.cs` | `done` | Engine whoami wrapper + shared facts summary. |
| `IntelligenceX.Tools.ADPlayground/AdDelegationAuditTool.cs` | `done` | Engine delegation audit wrapper + auto-column projection via shared table envelope. |
| `IntelligenceX.Tools.ADPlayground/AdReplicationSummaryTool.cs` | `done` | Engine replication wrapper + auto-column projection via shared table envelope. |
| `IntelligenceX.Tools.ADPlayground/AdSpnSearchTool.cs` | `done` | Engine SPN search wrapper + dynamic view helper. |
| `IntelligenceX.Tools.ADPlayground/AdSpnStatsTool.cs` | `done` | Engine SPN stats wrapper + auto-column projection via shared table envelope. |
| `IntelligenceX.Tools.ADPlayground/AdStaleAccountsTool.cs` | `done` | Engine stale-accounts wrapper. |
| `IntelligenceX.Tools.ADPlayground/AdUsersExpiredTool.cs` | `done` | Engine expired-users wrapper. |
| `IntelligenceX.Tools.ADPlayground/AdLdapQueryTool.cs` | `done` | Engine LDAP query wrapper + dynamic view helper. |
| `IntelligenceX.Tools.ADPlayground/AdLdapQueryPagedTool.cs` | `done` | Engine paged LDAP query wrapper + dynamic view helper. |
| `IntelligenceX.Tools.ADPlayground/AdLdapDiagnosticsTool.cs` | `done` | Engine diagnostics wrapper + auto-column projection via shared table envelope. |
| `IntelligenceX.Tools.ADPlayground/AdPrivilegedGroupsSummaryTool.cs` | `done` | Engine summary wrapper. |
| `IntelligenceX.Tools.ADPlayground/AdDomainAdminsSummaryTool.cs` | `done` | Engine summary wrapper. |

## System
| File | Status | Notes |
|---|---|---|
| `IntelligenceX.Tools.System/SystemToolBase.cs` | `keep` | Shared ComputerX failure mapping/plumbing. |
| `IntelligenceX.Tools.System/SystemToolOptions.cs` | `keep` | Caps/options only. |
| `IntelligenceX.Tools.System/SystemToolPack.cs` | `done` | Pack descriptor only. |
| `IntelligenceX.Tools.System/ToolRegistrySystemExtensions.cs` | `done` | Single registration source; platform-aware tool names and runtime-derived tool catalog metadata. |
| `IntelligenceX.Tools.System/SystemPackInfoTool.cs` | `done` | Model-facing pack guidance via shared contract (tools + tool catalog + capabilities + flow steps). |
| `IntelligenceX.Tools.System/SystemInfoTool.cs` | `done` | ComputerX runtime wrapper + shared facts summary. |
| `IntelligenceX.Tools.System/SystemWhoAmITool.cs` | `done` | ComputerX identity wrapper + shared facts summary. |
| `IntelligenceX.Tools.System/SystemHardwareIdentityTool.cs` | `done` | ComputerX runtime wrapper + shared facts summary. |
| `IntelligenceX.Tools.System/SystemHardwareSummaryTool.cs` | `done` | ComputerX summary wrapper + shared facts summary. |
| `IntelligenceX.Tools.System/SystemProcessListTool.cs` | `done` | Auto-column projection via `ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(...)`. |
| `IntelligenceX.Tools.System/SystemServiceListTool.cs` | `done` | Auto-column projection via `ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(...)`. |
| `IntelligenceX.Tools.System/SystemScheduledTasksListTool.cs` | `done` | Auto-column projection via `ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(...)`. |
| `IntelligenceX.Tools.System/SystemPortsListTool.cs` | `done` | Auto-column projection via `ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(...)`. |
| `IntelligenceX.Tools.System/SystemNetworkAdaptersTool.cs` | `done` | Auto-column projection via `ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(...)`. |
| `IntelligenceX.Tools.System/SystemFirewallRulesTool.cs` | `done` | Auto-column projection via `ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(...)`. |
| `IntelligenceX.Tools.System/SystemFirewallProfilesTool.cs` | `done` | Auto-column projection via `ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(...)`. |
| `IntelligenceX.Tools.System/SystemDisksListTool.cs` | `done` | Auto-column projection via `ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(...)`. |
| `IntelligenceX.Tools.System/SystemLogicalDisksListTool.cs` | `done` | Auto-column projection via `ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(...)`. |
| `IntelligenceX.Tools.System/SystemDevicesSummaryTool.cs` | `done` | Auto-column projection via `ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(...)`. |
| `IntelligenceX.Tools.System/SystemFeaturesListTool.cs` | `done` | Auto-column projection via `ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(...)`. |
| `IntelligenceX.Tools.System/WslStatusTool.cs` | `done` | Auto-column projection via `ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(...)` for tabular path. |

## PowerShell
| File | Status | Notes |
|---|---|---|
| `IntelligenceX.Tools.PowerShell/PowerShellToolOptions.cs` | `keep` | Runtime enablement/caps for dangerous shell execution. |
| `IntelligenceX.Tools.PowerShell/PowerShellToolBase.cs` | `keep` | Shared `IntelligenceX.Engines.PowerShell` failure mapping/plumbing. |
| `IntelligenceX.Tools.PowerShell/PowerShellToolPack.cs` | `done` | Dedicated `IX.PowerShell` descriptor (`DangerousWrite`). |
| `IntelligenceX.Tools.PowerShell/ToolRegistryPowerShellExtensions.cs` | `done` | Single registration source; exposes tool names and runtime-derived tool catalog metadata. |
| `IntelligenceX.Tools.PowerShell/PowerShellPackInfoTool.cs` | `done` | Model-facing dangerous-capability guidance via shared contract. |
| `IntelligenceX.Tools.PowerShell/PowerShellEnvironmentDiscoverTool.cs` | `done` | Policy + runtime autodiscovery wrapper for agent planning (`enabled`, write policy, host availability, limits). |
| `IntelligenceX.Tools.PowerShell/PowerShellHostsTool.cs` | `done` | Engine host discovery wrapper (`pwsh` / `windows_powershell`). |
| `IntelligenceX.Tools.PowerShell/PowerShellRunTool.cs` | `done` | Thin runtime execution wrapper over `IntelligenceX.Engines.PowerShell`. |

## TestimoX
| File | Status | Notes |
|---|---|---|
| `IntelligenceX.Tools.TestimoX/TestimoXToolOptions.cs` | `keep` | Runtime caps/options only. |
| `IntelligenceX.Tools.TestimoX/TestimoXToolBase.cs` | `keep` | Shared TestimoX wrapper plumbing. |
| `IntelligenceX.Tools.TestimoX/TestimoXToolPack.cs` | `done` | Dedicated `IX.TestimoX` descriptor (`SensitiveRead`). |
| `IntelligenceX.Tools.TestimoX/ToolRegistryTestimoXExtensions.cs` | `done` | Single registration source; exposes tool names and runtime-derived tool catalog metadata. |
| `IntelligenceX.Tools.TestimoX/TestimoXPackInfoTool.cs` | `done` | Model-facing pack guidance via shared contract. |
| `IntelligenceX.Tools.TestimoX/TestimoXRulesListTool.cs` | `done` | Engine rule-discovery wrapper + auto-column projection via shared table envelope. |
| `IntelligenceX.Tools.TestimoX/TestimoXRulesRunTool.cs` | `done` | Engine rule-run wrapper + typed per-rule outcomes + optional capped raw rule rows. |

## FileSystem
| File | Status | Notes |
|---|---|---|
| `IntelligenceX.Tools.FileSystem/FileSystemToolBase.cs` | `keep` | Allowed-root/path policy only. |
| `IntelligenceX.Tools.FileSystem/FileSystemToolOptions.cs` | `keep` | Caps/options only. |
| `IntelligenceX.Tools.FileSystem/FileSystemToolPack.cs` | `done` | Pack descriptor only. |
| `IntelligenceX.Tools.FileSystem/ToolRegistryFileSystemExtensions.cs` | `done` | Single registration source; exposes tool names and runtime-derived tool catalog metadata. |
| `IntelligenceX.Tools.FileSystem/FileSystemPackInfoTool.cs` | `done` | Model-facing pack guidance via shared contract (tools + tool catalog + capabilities + flow steps). |
| `IntelligenceX.Tools.FileSystem/FsListTool.cs` | `done` | Auto-column projection via `ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(...)`. |
| `IntelligenceX.Tools.FileSystem/FsSearchTool.cs` | `done` | Auto-column projection via `ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(...)`. |
| `IntelligenceX.Tools.FileSystem/FsReadTool.cs` | `done` | Direct content wrapper (no table projection). |

## EventLog
| File | Status | Notes |
|---|---|---|
| `IntelligenceX.Tools.EventLog/EventLogToolBase.cs` | `keep` | Shared EVTX/live error/path plumbing + simple catalog preview helpers. |
| `IntelligenceX.Tools.EventLog/EventLogToolOptions.cs` | `keep` | Caps/options only. |
| `IntelligenceX.Tools.EventLog/EventLogToolPack.cs` | `done` | Pack descriptor only. |
| `IntelligenceX.Tools.EventLog/ToolRegistryEventLogExtensions.cs` | `done` | Single registration source; exposes tool names and runtime-derived tool catalog metadata. |
| `IntelligenceX.Tools.EventLog/EventLogPackInfoTool.cs` | `done` | Model-facing pack guidance via shared contract (tools + tool catalog + capabilities + flow steps), including EventLog→AD correlation flow hints. |
| `IntelligenceX.Tools.EventLog/EventLogEvtxQueryTool.cs` | `done` | Auto-column projection via `ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(...)`. |
| `IntelligenceX.Tools.EventLog/EventLogEvtxStatsTool.cs` | `done` | Auto-column projection via `ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(...)`. |
| `IntelligenceX.Tools.EventLog/EventLogLiveQueryTool.cs` | `done` | Auto-column projection via `ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(...)`. |
| `IntelligenceX.Tools.EventLog/EventLogLiveStatsTool.cs` | `done` | Auto-column projection via `ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(...)`. |
| `IntelligenceX.Tools.EventLog/EventLogChannelListTool.cs` | `done` | Catalog wrapper via base helper. |
| `IntelligenceX.Tools.EventLog/EventLogProviderListTool.cs` | `done` | Catalog wrapper via base helper. |
| `IntelligenceX.Tools.EventLog/EventLogEvtxFailedLogonsReportTool.cs` | `done` | EventViewerX security report wrapper. |
| `IntelligenceX.Tools.EventLog/EventLogEvtxAccountLockoutsReportTool.cs` | `done` | EventViewerX security report wrapper. |
| `IntelligenceX.Tools.EventLog/EventLogEvtxUserLogonsReportTool.cs` | `done` | EventViewerX security report wrapper. |
| `IntelligenceX.Tools.EventLog/EventLogEvtxSecurityReportHelper.cs` | `done` | Response shaping only; request defaults/caps normalization now lives in `EventViewerX` (`SecurityEvtxQueryRequestNormalizer`). Adds `ad_correlation` follow-up hints for security report outputs. |

## Email
| File | Status | Notes |
|---|---|---|
| `IntelligenceX.Tools.Email/EmailToolBase.cs` | `keep` | Shared IMAP/SMTP config plumbing. |
| `IntelligenceX.Tools.Email/EmailToolOptions.cs` | `keep` | Caps/options only. |
| `IntelligenceX.Tools.Email/ImapClientFactory.cs` | `keep` | Connection utility. |
| `IntelligenceX.Tools.Email/EmailToolPack.cs` | `done` | Pack descriptor only. |
| `IntelligenceX.Tools.Email/ToolRegistryEmailExtensions.cs` | `done` | Single registration source; exposes tool names and runtime-derived tool catalog metadata. |
| `IntelligenceX.Tools.Email/EmailPackInfoTool.cs` | `done` | Model-facing pack guidance via shared contract (tools + tool catalog + capabilities + flow steps). |
| `IntelligenceX.Tools.Email/EmailImapSearchTool.cs` | `done` | Auto-column projection via `ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(...)`; model-first payload preserved. |
| `IntelligenceX.Tools.Email/EmailImapGetTool.cs` | `done` | Typed message + attachment wrapper; model-first payload (no manual JSON builders). |
| `IntelligenceX.Tools.Email/EmailSmtpSendTool.cs` | `done` | Send wrapper; model-first payload (no manual JSON builders). |

## Common
| File | Status | Notes |
|---|---|---|
| `IntelligenceX.Tools.Common/ToolResponse.cs` | `done` | Canonical envelope + facts/table helpers. |
| `IntelligenceX.Tools.Common/ToolArgs.cs` | `done` | Shared argument parsing binders/caps. |
| `IntelligenceX.Tools.Common/ToolEnumBinders.cs` | `done` | Shared enum parse/name binders. |
| `IntelligenceX.Tools.Common/ToolFailureMapper.cs` | `done` | Shared failure-to-error mapping. |
| `IntelligenceX.Tools.Common/ToolTableView.cs` | `done` | Generic table projection engine. |
| `IntelligenceX.Tools.Common/ToolTableViewEnvelope.cs` | `done` | Shared projection-to-envelope pipeline for thin wrappers. |
| `IntelligenceX.Tools.Common/ToolAutoTableColumns.cs` | `done` | Reflection-based typed row projection catalog used to remove per-tool `ViewColumns` boilerplate. |
| `IntelligenceX.Tools.Common/ToolDynamicTableViewEnvelope.cs` | `done` | Shared dynamic dictionary/bag projection + envelope shaping for packs that expose attribute bags. |
| `IntelligenceX.Tools.Common/ToolPackGuidance.cs` | `done` | Shared typed pack-guidance contract + runtime catalog builder (`required_arguments`, argument hints, and structured usage traits for projection/paging/time-range/dynamic-attributes/scoping/actions). |
| `IntelligenceX.Tools.Common/ToolMarkdownContract.cs` | `keep` | Markdown/table composition contract. |
| `IntelligenceX.Tools.Common/ToolOutputHints.cs` | `keep` | UI render and metadata hints. |
| `IntelligenceX.Tools.Common/ToolMarkdown.cs` | `done` | Shared `SummaryFacts` + `SummaryText` helpers reduce repeated summary assembly boilerplate. |

## Tests + Docs
| File | Status | Notes |
|---|---|---|
| `IntelligenceX.Tools.Tests/SourceGuardrailTests.cs` | `done` | Guardrails for removed local wrappers/parser clones. |
| `IntelligenceX.Tools.Tests/ToolDefinitionContractTests.cs` | `done` | Stable-name coverage includes pack-info tools. |
| `IntelligenceX.Tools.Tests/ToolPackGuidanceTests.cs` | `done` | Shared pack-guidance model/factory behavior + catalog argument-hint coverage. |
| `IntelligenceX.Tools.Tests/ToolPackInfoContractTests.cs` | `done` | Enforces runtime pack-info contract fields, structured flow/capabilities, and tool-catalog argument-hint parity. |
| `IntelligenceX.Tools.Tests/ToolMarkdownTests.cs` | `done` | Covers shared summary markdown helper used by multiple non-pack tools. |
| `IntelligenceX.Tools.Tests/ToolSchemaSnapshotTests.cs` | `done` | Snapshot coverage updated for `ad_pack_info`. |
| `Docs/ToolOutputContract.md` | `done` | Raw-vs-view and pack-guidance contract documented. |
| `README.md` | `done` | Pack-level guidance tool contract documented. |

## Remaining Moves (Focused)
No focused wrapper-engine move items currently open.
