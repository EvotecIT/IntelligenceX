# IntelligenceX.Tools Engine-First Big Sweep TODO

## Goal
Reduce all tool projects to thin wrappers:
- tools own schema, caps/policy, and markdown/render hints
- engines own query logic, parsing, aggregation, and typed result models
- common reusable binder/error mapping lives in `IntelligenceX.Tools.Common`

## Parallel Lanes
| Lane | Scope | Primary Repo | Output |
|---|---|---|---|
| `L1` | AD extraction (query + aggregation + typed DTOs) | `TestimoX/ADPlayground` | new/expanded typed query executors consumed by AD tools |
| `L2` | Event extraction (aggregates + report DTO contracts) | `PSEventViewer/EventViewerX` | canonical report DTOs + aggregate helpers in engine |
| `L3` | System contract normalization | `TestimoX/ComputerX` | wrappers consume engine DTOs directly, no local row/result clones |
| `L4` | Common consolidation | `IntelligenceX.Tools.Common` | shared enum binders, failure mappers, table/preview helpers |
| `L5` | Wrapper simplification pass | `IntelligenceX.Tools.*` | one executor call + output hints per tool |
| `L6` | Guardrails/tests/docs | `IntelligenceX.Tools.Tests` + `Docs` | source guardrails enforcing thin-wrapper policy |

## Execution Order
1. Run `L1` + `L2` + `L3` in parallel (engine contracts first).
2. Run `L4` in parallel with late engine PR review.
3. Run `L5` after required engine contracts land.
4. Run `L6` last and block merges on guardrails.

## Status Legend
| Value | Meaning |
|---|---|
| `todo` | not started |
| `in_progress` | active |
| `blocked` | waiting for dependency |
| `done` | completed |

## Root Files
| File | Lane | Action | Target | Status |
|---|---|---|---|---|
| `Directory.Build.props` | `L6` | Keep; ensure engine root resolution stays deterministic and compatible | `IntelligenceX.Tools` | `done` |
| `README.md` | `L6` | Update architecture section to “thin wrappers + engine-owned DTOs” | `IntelligenceX.Tools` | `todo` |
| `TODO.md` | `L6` | Sync with this sweep plan milestones | `IntelligenceX.Tools` | `todo` |
| `IntelligenceX.Tools.sln` | `L6` | Keep; ensure all new guardrail tests included | `IntelligenceX.Tools` | `todo` |
| `IntelligenceX.Tools.slnx` | `L6` | Keep; keep in sync with `.sln` | `IntelligenceX.Tools` | `todo` |

## ActiveDirectory Project
| File | Lane | Action | Target | Status |
|---|---|---|---|---|
| `IntelligenceX.Tools.ActiveDirectory/IntelligenceX.Tools.ActiveDirectory.csproj` | `L5` | Keep; update refs only if new ADPlayground contracts added | `IntelligenceX.Tools.ActiveDirectory` | `todo` |
| `IntelligenceX.Tools.ActiveDirectory/ActiveDirectoryToolPack.cs` | `L5` | Keep | `IntelligenceX.Tools.ActiveDirectory` | `todo` |
| `IntelligenceX.Tools.ActiveDirectory/ToolRegistryActiveDirectoryExtensions.cs` | `L5` | Keep; reorder/adjust registrations only if tools merged/split | `IntelligenceX.Tools.ActiveDirectory` | `todo` |
| `IntelligenceX.Tools.ActiveDirectory/ActiveDirectoryToolOptions.cs` | `L5` | Keep | `IntelligenceX.Tools.ActiveDirectory` | `todo` |
| `IntelligenceX.Tools.ActiveDirectory/ActiveDirectoryToolBase.cs` | `L4` | Keep; only generic wrapper plumbing, no domain parsing additions | `IntelligenceX.Tools.ActiveDirectory` | `todo` |
| `IntelligenceX.Tools.ActiveDirectory/AdSearchRow.cs` | `L5` | Removed; replaced with engine-owned `LdapToolOutputRow` contract in AD wrappers | `ADPlayground` | `done` |
| `IntelligenceX.Tools.ActiveDirectory/AdQueryResultHelpers.cs` | `L4` | Keep; move generic parts to `Tools.Common` if reused outside AD | `IntelligenceX.Tools.Common` | `todo` |
| `IntelligenceX.Tools.ActiveDirectory/AdLdapDiagnosticsTool.cs` | `L5` | Wrapper simplified; diagnostics execution remains engine-owned via `LdapDiagnosticsReportBuilder` and projection now uses `TryBuildModelResponseAutoColumns` | `ADPlayground` | `done` |
| `IntelligenceX.Tools.ActiveDirectory/AdUsersExpiredTool.cs` | `L5` | Keep wrapper; pass through `ExpiredUserEntry` engine rows directly | `ADPlayground` | `done` |
| `IntelligenceX.Tools.ActiveDirectory/AdStaleAccountsTool.cs` | `L1` | Direct pass-through of `StaleAccountsQueryResult` and `StaleAccountEntry` engine types (no local result/row wrappers) | `ADPlayground` | `done` |
| `IntelligenceX.Tools.ActiveDirectory/AdObjectResolveTool.cs` | `L1` | Batched DN/SID resolve execution and row DTOs moved to `LdapToolObjectResolveService`; wrapper keeps args and uses auto-column projection for preview | `ADPlayground` | `done` |
| `IntelligenceX.Tools.ActiveDirectory/AdSearchTool.cs` | `L1` | Filter/query execution + attribute policy moved to `LdapToolSearchService` + `LdapToolSearchPolicy`; wrapper keeps preview + output contract shaping | `ADPlayground` | `done` |
| `IntelligenceX.Tools.ActiveDirectory/AdLdapQueryTool.cs` | `L1` | Query validation/scope/attribute policy/sensitive checks and execution moved to `LdapToolAdLdapQueryService`; wrapper keeps caps/context + preview shaping | `ADPlayground` | `done` |
| `IntelligenceX.Tools.ActiveDirectory/AdLdapQueryPagedTool.cs` | `L1` | Paged query validation/attribute policy/cursor handling/paging math moved to `LdapToolAdLdapQueryPagedService` + `LdapToolOffsetCursor`; wrapper keeps caps/context + preview shaping | `ADPlayground` | `done` |
| `IntelligenceX.Tools.ActiveDirectory/AdGroupsListTool.cs` | `L1` | Query/filter/paging/list shaping moved to `AdGroupsListService`; wrapper is pass-through + preview | `ADPlayground` | `done` |
| `IntelligenceX.Tools.ActiveDirectory/AdGroupMembersTool.cs` | `L1` | Group lookup and direct member enumeration moved to `AdGroupMembersService` | `ADPlayground` | `done` |
| `IntelligenceX.Tools.ActiveDirectory/AdGroupMembersResolvedTool.cs` | `L1` | Resolve pipeline + ambiguity handling moved to `AdGroupMembersResolvedService` | `ADPlayground` | `done` |
| `IntelligenceX.Tools.ActiveDirectory/AdSpnSearchTool.cs` | `L1` | SPN search execution + attribute policy moved to `LdapToolSpnSearchService` + `LdapToolSpnSearchPolicy`; wrapper keeps preview + output contract shaping | `ADPlayground` | `done` |
| `IntelligenceX.Tools.ActiveDirectory/AdObjectGetTool.cs` | `L1` | Object-get decision model and attribute selection policy moved to `LdapToolObjectGetService` + `LdapToolObjectGetPolicy`; wrapper keeps output compatibility shaping | `ADPlayground` | `done` |
| `IntelligenceX.Tools.ActiveDirectory/AdDomainInfoTool.cs` | `L1` | Root/domain-info composition moved to `DomainInfoService`; local `DomainInfoResult` removed from wrapper | `ADPlayground` | `done` |
| `IntelligenceX.Tools.ActiveDirectory/AdDomainControllersTool.cs` | `L1` | Domain-controller query/fallback execution moved to `AdDomainControllersService`; wrapper keeps preview + output contract shaping | `ADPlayground` | `done` |
| `IntelligenceX.Tools.ActiveDirectory/AdDelegationAuditTool.cs` | `L1` | Delegation/UAC analysis and row DTOs moved to `LdapToolDelegationAuditService`; tool is pass-through wrapper with auto-column projection | `ADPlayground` | `done` |
| `IntelligenceX.Tools.ActiveDirectory/AdSearchFacetsTool.cs` | `L1` | Facets/UAC/password-age aggregation + sample shaping moved to `LdapToolSearchFacetsService`; tool is pass-through wrapper | `ADPlayground` | `done` |
| `IntelligenceX.Tools.ActiveDirectory/AdSpnStatsTool.cs` | `L1` | Direct pass-through of `ADPlayground.Kerberos.SpnStatsResult` (no local SPN result/row wrappers) with auto-column projection | `ADPlayground` | `done` |
| `IntelligenceX.Tools.ActiveDirectory/AdPrivilegedGroupsSummaryTool.cs` | `L1` | Summary aggregation + typed result rows moved to `PrivilegedGroupsSummaryService`; wrapper is pass-through | `ADPlayground` | `done` |
| `IntelligenceX.Tools.ActiveDirectory/AdDomainAdminsSummaryTool.cs` | `L1` | Summary aggregation + typed result/member rows moved to `DomainAdminsSummaryService`; wrapper is pass-through | `ADPlayground` | `done` |
| `IntelligenceX.Tools.ActiveDirectory/AdReplicationSummaryTool.cs` | `L1` | Replication summary aggregation + typed result/detail rows moved to `ReplicationSummaryQueryService`; wrapper keeps meta rendering and uses auto-column projection for preview | `ADPlayground` | `done` |
| `IntelligenceX.Tools.ActiveDirectory/AdWhoAmITool.cs` | `L1` | Runtime identity + RootDSE context moved to `AdWhoAmIService`; wrapper keeps markdown/output shaping | `ADPlayground` | `done` |

## System Project
| File | Lane | Action | Target | Status |
|---|---|---|---|---|
| `IntelligenceX.Tools.System/IntelligenceX.Tools.System.csproj` | `L5` | Keep; ensure only engine/common references needed by thin wrappers | `IntelligenceX.Tools.System` | `todo` |
| `IntelligenceX.Tools.System/SystemToolBase.cs` | `L4` | Keep minimal; add shared typed failure mapping helper for ComputerX wrappers | `IntelligenceX.Tools.System` | `done` |
| `IntelligenceX.Tools.System/SystemToolOptions.cs` | `L4` | Keep minimal caps | `IntelligenceX.Tools.System` | `todo` |
| `IntelligenceX.Tools.System/SystemToolPack.cs` | `L5` | Keep | `IntelligenceX.Tools.System` | `todo` |
| `IntelligenceX.Tools.System/ToolRegistrySystemExtensions.cs` | `L5` | Keep; registration only | `IntelligenceX.Tools.System` | `todo` |
| `IntelligenceX.Tools.System/SystemHardwareSummaryTool.cs` | `L5` | Keep; already close to target | `ComputerX` | `done` |
| `IntelligenceX.Tools.System/SystemDevicesSummaryTool.cs` | `L5` | Keep; already close to target | `ComputerX` | `done` |
| `IntelligenceX.Tools.System/WslStatusTool.cs` | `L5` | Thin wrapper + auto-column projection via shared envelope helper | `ComputerX` | `done` |
| `IntelligenceX.Tools.System/SystemInfoTool.cs` | `L3` | Removed local output DTOs; direct pass-through of `SystemRuntimeQueryResult` | `ComputerX` | `done` |
| `IntelligenceX.Tools.System/SystemHardwareIdentityTool.cs` | `L3` | Removed local output DTOs; direct pass-through of `SystemRuntimeQueryResult` | `ComputerX` | `done` |
| `IntelligenceX.Tools.System/SystemWhoAmITool.cs` | `L3` | Removed local output DTO; direct pass-through of `CurrentIdentityInfo` | `ComputerX` | `done` |
| `IntelligenceX.Tools.System/SystemProcessListTool.cs` | `L3` | Engine result pass-through + auto-column projection (`TryBuildModelResponseAutoColumns`) | `ComputerX` | `done` |
| `IntelligenceX.Tools.System/SystemServiceListTool.cs` | `L3` | Engine result pass-through + auto-column projection (`TryBuildModelResponseAutoColumns`) | `ComputerX` | `done` |
| `IntelligenceX.Tools.System/SystemScheduledTasksListTool.cs` | `L3` | Engine result pass-through + auto-column projection (`TryBuildModelResponseAutoColumns`) | `ComputerX` | `done` |
| `IntelligenceX.Tools.System/SystemPortsListTool.cs` | `L4` | Shared enum binders + auto-column projection (`TryBuildModelResponseAutoColumns`) | `IntelligenceX.Tools.Common` | `done` |
| `IntelligenceX.Tools.System/SystemNetworkAdaptersTool.cs` | `L3` | Engine result pass-through + auto-column projection (`TryBuildModelResponseAutoColumns`) | `ComputerX` | `done` |
| `IntelligenceX.Tools.System/SystemFeaturesListTool.cs` | `L3` | Remove `FeatureRow/FeatureListResult` duplication; consume engine result | `ComputerX` | `done` |
| `IntelligenceX.Tools.System/SystemDisksListTool.cs` | `L3` | Engine result pass-through + auto-column projection (`TryBuildModelResponseAutoColumns`) | `ComputerX` | `done` |
| `IntelligenceX.Tools.System/SystemLogicalDisksListTool.cs` | `L3` | Engine result pass-through + auto-column projection (`TryBuildModelResponseAutoColumns`) | `ComputerX` | `done` |
| `IntelligenceX.Tools.System/SystemFirewallRulesTool.cs` | `L4` | Shared enum binders + engine pass-through + auto-column projection | `IntelligenceX.Tools.Common` | `done` |
| `IntelligenceX.Tools.System/SystemFirewallProfilesTool.cs` | `L4` | Shared enum binders + engine pass-through + auto-column projection | `IntelligenceX.Tools.Common` | `done` |

## EventLog Project
| File | Lane | Action | Target | Status |
|---|---|---|---|---|
| `IntelligenceX.Tools.EventLog/IntelligenceX.Tools.EventLog.csproj` | `L5` | Keep; update references when engine contracts move | `IntelligenceX.Tools.EventLog` | `todo` |
| `IntelligenceX.Tools.EventLog/EventLogToolPack.cs` | `L5` | Keep | `IntelligenceX.Tools.EventLog` | `todo` |
| `IntelligenceX.Tools.EventLog/ToolRegistryEventLogExtensions.cs` | `L5` | Keep | `IntelligenceX.Tools.EventLog` | `todo` |
| `IntelligenceX.Tools.EventLog/EventLogToolOptions.cs` | `L5` | Keep | `IntelligenceX.Tools.EventLog` | `todo` |
| `IntelligenceX.Tools.EventLog/EventLogToolBase.cs` | `L4` | Keep; central error/path mapping | `IntelligenceX.Tools.EventLog` | `done` |
| `IntelligenceX.Tools.EventLog/EventLogArgs.cs` | `L4` | Removed; generic UTC/event-id/top parsing consolidated into `ToolTime` + `ToolArgs` | `IntelligenceX.Tools.Common` | `done` |
| `IntelligenceX.Tools.EventLog/EventLogEvtxSecurityReportHelper.cs` | `L5` | Keep for preview/meta/render shaping; now uses auto-column projection for top rows | `IntelligenceX.Tools.EventLog` | `done` |
| `IntelligenceX.Tools.EventLog/EventLogChannelListTool.cs` | `L5` | Keep thin wrapper | `EventViewerX` | `todo` |
| `IntelligenceX.Tools.EventLog/EventLogProviderListTool.cs` | `L5` | Keep thin wrapper | `EventViewerX` | `todo` |
| `IntelligenceX.Tools.EventLog/EventLogLiveQueryTool.cs` | `L5` | Thin wrapper + auto-column projection (`TryBuildModelResponseAutoColumns`) | `EventViewerX` | `done` |
| `IntelligenceX.Tools.EventLog/EventLogLiveStatsTool.cs` | `L5` | Thin wrapper + auto-column projection (`TryBuildModelResponseAutoColumns`) | `EventViewerX` | `done` |
| `IntelligenceX.Tools.EventLog/EventLogEvtxQueryTool.cs` | `L5` | Thin wrapper + auto-column projection (`TryBuildModelResponseAutoColumns`) | `EventViewerX` | `done` |
| `IntelligenceX.Tools.EventLog/EventLogEvtxStatsTool.cs` | `L5` | Thin wrapper + auto-column projection (`TryBuildModelResponseAutoColumns`) | `EventViewerX` | `done` |
| `IntelligenceX.Tools.EventLog/EventLogPackInfoTool.cs` | `L5` | Keep thin wrapper with direct typed payload (no local `PackInfoResult`) | `EventViewerX` | `done` |
| `IntelligenceX.Tools.EventLog/EventLogAggregates.cs` | `L2` | Removed tool-local aggregate wrapper; tools rely on engine aggregate contracts | `EventViewerX` | `done` |
| `IntelligenceX.Tools.EventLog/EventLogTopRow.cs` | `L2` | Removed local top-row model; use `EventViewerX.Reports.ReportTopRow` | `EventViewerX` | `done` |
| `IntelligenceX.Tools.EventLog/EventLogEvtxFailedLogonsReportTool.cs` | `L2` | Removed local `FailedLogonsReportResult`; consumes engine report DTO directly | `EventViewerX` | `done` |
| `IntelligenceX.Tools.EventLog/EventLogEvtxUserLogonsReportTool.cs` | `L2` | Removed local `UserLogonsReportResult`; consumes engine report DTO directly | `EventViewerX` | `done` |
| `IntelligenceX.Tools.EventLog/EventLogEvtxAccountLockoutsReportTool.cs` | `L2` | Removed local `AccountLockoutsReportResult`; consumes engine report DTO directly | `EventViewerX` | `done` |

## FileSystem Project
| File | Lane | Action | Target | Status |
|---|---|---|---|---|
| `IntelligenceX.Tools.FileSystem/IntelligenceX.Tools.FileSystem.csproj` | `L5` | Keep | `IntelligenceX.Tools.FileSystem` | `todo` |
| `IntelligenceX.Tools.FileSystem/FileSystemToolPack.cs` | `L5` | Keep | `IntelligenceX.Tools.FileSystem` | `todo` |
| `IntelligenceX.Tools.FileSystem/ToolRegistryFileSystemExtensions.cs` | `L5` | Keep | `IntelligenceX.Tools.FileSystem` | `todo` |
| `IntelligenceX.Tools.FileSystem/FileSystemToolOptions.cs` | `L5` | Keep | `IntelligenceX.Tools.FileSystem` | `todo` |
| `IntelligenceX.Tools.FileSystem/FileSystemToolBase.cs` | `L4` | Keep; centralize any duplicated path error mapping here only | `IntelligenceX.Tools.FileSystem` | `todo` |
| `IntelligenceX.Tools.FileSystem/FsListTool.cs` | `L5` | Thin wrapper over `IntelligenceX.Engines.FileSystem` + auto-column projection | `IntelligenceX.Engines.FileSystem` | `done` |
| `IntelligenceX.Tools.FileSystem/FsReadTool.cs` | `L5` | Keep thin wrapper over `IntelligenceX.Engines.FileSystem` | `IntelligenceX.Engines.FileSystem` | `todo` |
| `IntelligenceX.Tools.FileSystem/FsSearchTool.cs` | `L5` | Thin wrapper over `IntelligenceX.Engines.FileSystem` + auto-column projection | `IntelligenceX.Engines.FileSystem` | `done` |

## Email Project
| File | Lane | Action | Target | Status |
|---|---|---|---|---|
| `IntelligenceX.Tools.Email/IntelligenceX.Tools.Email.csproj` | `L5` | Keep | `IntelligenceX.Tools.Email` | `todo` |
| `IntelligenceX.Tools.Email/EmailToolBase.cs` | `L4` | Keep; ensure shared argument/failure helpers consumed | `IntelligenceX.Tools.Email` | `todo` |
| `IntelligenceX.Tools.Email/EmailToolOptions.cs` | `L5` | Keep | `IntelligenceX.Tools.Email` | `todo` |
| `IntelligenceX.Tools.Email/ImapClientFactory.cs` | `L5` | Keep | `IntelligenceX.Tools.Email` | `todo` |
| `IntelligenceX.Tools.Email/EmailImapSearchTool.cs` | `L5` | Thin wrapper + auto-column projection (`TryBuildModelResponseAutoColumns`) | `IntelligenceX.Tools.Email` | `done` |
| `IntelligenceX.Tools.Email/EmailImapGetTool.cs` | `L5` | Keep thin wrapper | `IntelligenceX.Tools.Email` | `todo` |
| `IntelligenceX.Tools.Email/EmailSmtpSendTool.cs` | `L4` | Local array parsing removed; now uses shared binders in `ToolArgs` and remains a thin wrapper | `IntelligenceX.Tools.Common` | `done` |

## Common Project
| File | Lane | Action | Target | Status |
|---|---|---|---|---|
| `IntelligenceX.Tools.Common/IntelligenceX.Tools.Common.csproj` | `L4` | Keep | `IntelligenceX.Tools.Common` | `todo` |
| `IntelligenceX.Tools.Common/IToolPack.cs` | `L4` | Keep | `IntelligenceX.Tools.Common` | `todo` |
| `IntelligenceX.Tools.Common/ToolPackDescriptor.cs` | `L4` | Keep | `IntelligenceX.Tools.Common` | `todo` |
| `IntelligenceX.Tools.Common/ToolCapabilityTier.cs` | `L6` | Keep | `IntelligenceX.Tools.Common` | `todo` |
| `IntelligenceX.Tools.Common/ToolBase.cs` | `L4` | Keep | `IntelligenceX.Tools.Common` | `todo` |
| `IntelligenceX.Tools.Common/ToolInvoker.cs` | `L4` | Keep | `IntelligenceX.Tools.Common` | `todo` |
| `IntelligenceX.Tools.Common/ToolResponse.cs` | `L4` | Keep; typed failure-to-error mapping helper now in place and consumed by wrappers | `IntelligenceX.Tools.Common` | `done` |
| `IntelligenceX.Tools.Common/ToolArgs.cs` | `L4` | Keep; reusable enum/string-array/int-array/top-capped binders added and consumed by wrappers | `IntelligenceX.Tools.Common` | `done` |
| `IntelligenceX.Tools.Common/ToolOutputHints.cs` | `L4` | Keep | `IntelligenceX.Tools.Common` | `todo` |
| `IntelligenceX.Tools.Common/ToolPreview.cs` | `L4` | Keep | `IntelligenceX.Tools.Common` | `todo` |
| `IntelligenceX.Tools.Common/ToolSchema.cs` | `L4` | Keep | `IntelligenceX.Tools.Common` | `todo` |
| `IntelligenceX.Tools.Common/ToolJson.cs` | `L4` | Keep | `IntelligenceX.Tools.Common` | `todo` |
| `IntelligenceX.Tools.Common/ToolMarkdown.cs` | `L4` | Keep | `IntelligenceX.Tools.Common` | `todo` |
| `IntelligenceX.Tools.Common/ToolMarkdownContract.cs` | `L4` | Keep | `IntelligenceX.Tools.Common` | `todo` |
| `IntelligenceX.Tools.Common/MarkdownTable.cs` | `L4` | Keep | `IntelligenceX.Tools.Common` | `todo` |
| `IntelligenceX.Tools.Common/ToolPaths.cs` | `L4` | Keep | `IntelligenceX.Tools.Common` | `todo` |
| `IntelligenceX.Tools.Common/PathResolver.cs` | `L4` | Keep | `IntelligenceX.Tools.Common` | `todo` |
| `IntelligenceX.Tools.Common/OffsetCursor.cs` | `L4` | Keep | `IntelligenceX.Tools.Common` | `todo` |
| `IntelligenceX.Tools.Common/ToolReportEnvelopes.cs` | `L4` | Keep | `IntelligenceX.Tools.Common` | `todo` |
| `IntelligenceX.Tools.Common/ToolSafe.cs` | `L4` | Keep | `IntelligenceX.Tools.Common` | `todo` |
| `IntelligenceX.Tools.Common/ToolText.cs` | `L4` | Keep | `IntelligenceX.Tools.Common` | `todo` |
| `IntelligenceX.Tools.Common/ToolTime.cs` | `L4` | Keep | `IntelligenceX.Tools.Common` | `todo` |
| `IntelligenceX.Tools.Common/NEW: ToolEnumBinders.cs` | `L4` | Add shared `Parse/ToString` binders for common enums (protocol/profile/direction/etc.) | `IntelligenceX.Tools.Common` | `done` |
| `IntelligenceX.Tools.Common/NEW: ToolFailureMapper.cs` | `L4` | Add generic typed failure mapper to remove 16+ per-tool methods | `IntelligenceX.Tools.Common` | `done` |
| `IntelligenceX.Tools.Common/NEW: ToolAutoTableColumns.cs` | `L4` | Add reflection-based typed column catalog to remove per-tool `ViewColumns` lists | `IntelligenceX.Tools.Common` | `done` |
| `IntelligenceX.Tools.Common/NEW: ToolDynamicTableViewEnvelope.cs` | `L4` | Move generic dynamic dictionary/bag projection flow out of AD wrapper helper into shared common layer | `IntelligenceX.Tools.Common` | `done` |

## Tests Project
| File | Lane | Action | Target | Status |
|---|---|---|---|---|
| `IntelligenceX.Tools.Tests/IntelligenceX.Tools.Tests.csproj` | `L6` | Keep | `IntelligenceX.Tools.Tests` | `todo` |
| `IntelligenceX.Tools.Tests/ToolDefinitionContractTests.cs` | `L6` | Keep; update stable tool name lists as needed | `IntelligenceX.Tools.Tests` | `todo` |
| `IntelligenceX.Tools.Tests/ToolResponseContractTests.cs` | `L6` | Keep; add coverage for new common failure mapper | `IntelligenceX.Tools.Tests` | `todo` |
| `IntelligenceX.Tools.Tests/ToolMarkdownContractTests.cs` | `L6` | Keep | `IntelligenceX.Tools.Tests` | `todo` |
| `IntelligenceX.Tools.Tests/ToolSchemaSnapshotTests.cs` | `L6` | Keep | `IntelligenceX.Tools.Tests` | `todo` |
| `IntelligenceX.Tools.Tests/ThinWrapperGuardrailTests.cs` | `L6` | Extend to flag local result-row clones where engine DTO exists | `IntelligenceX.Tools.Tests` | `todo` |
| `IntelligenceX.Tools.Tests/SourceGuardrailTests.cs` | `L6` | Extend guardrails for duplicated parser methods (`TryParseProtocol`-style) | `IntelligenceX.Tools.Tests` | `todo` |

## PR Cut Plan (Parallel)
| PR | Lane | Repo | Scope |
|---|---|---|---|
| `PR-A1` | `L1` | `TestimoX/ADPlayground` | Add typed executors + DTOs for stale/search facets/delegation/group/object-get pipelines |
| `PR-A2` | `L1` | `IntelligenceX.Tools` | Replace AD tool local DTOs with engine DTO pass-through |
| `PR-E1` | `L2` | `PSEventViewer/EventViewerX` | Move report aggregate/model contracts (`TopRow`, security report DTOs) |
| `PR-E2` | `L2` | `IntelligenceX.Tools` | Remove `EventLogAggregates` + local report DTO wrappers |
| `PR-S1` | `L3` | `TestimoX/ComputerX` | Promote missing DTOs needed by system wrappers |
| `PR-S2` | `L3` | `IntelligenceX.Tools` | Remove `FeatureRow/NetworkAdapterRow/...` local clones |
| `PR-C1` | `L4` | `IntelligenceX.Tools` | Add `ToolEnumBinders` + `ToolFailureMapper`; migrate system/event tools |
| `PR-Q1` | `L6` | `IntelligenceX.Tools` | Guardrails/tests enforcing thin-wrapper policy |

## Done Definition for Sweep
| Check | Requirement |
|---|---|
| Engine ownership | No AD/Event/System domain aggregation logic remains in tool files |
| DTO ownership | No local `*Row/*Result` clone types in tools when equivalent engine DTO exists |
| Parser dedupe | No repeated local enum parser blocks in individual tools |
| Failure dedupe | Shared failure mapping replaces repeated `ErrorFrom*Failure` methods |
| Guardrails | tests fail on new domain logic reintroduced in wrappers |
| Build | `IntelligenceX.Tools.sln` builds clean with warnings-as-errors |
