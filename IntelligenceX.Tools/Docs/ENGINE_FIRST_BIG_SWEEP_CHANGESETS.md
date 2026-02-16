# Engine-First Big Sweep Commit Sets

This file defines commit-ready staging sets for the current sweep.

## Boundaries
- Do not include `IntelligenceX.Chat.Service/ChatServiceSession.cs` from the GUI worktree because another agent is actively editing it.
- Keep `IntelligenceX.Tools` and `ADPlayground` commits separate.

## Recommended Order
1. `ADP-01` engine contracts/services
2. `ADP-02` engine model alignments
3. `IXT-01` tools common + base helpers
4. `IXT-02` AD wrappers consume engine contracts
5. `IXT-03` Event/System/Email wrapper dedupe
6. `IXT-04` guardrails + sweep docs

## ADPlayground Repo (`C:/Support/GitHub/TestimoX-master`)

### ADP-01 Engine Contracts and Services
Purpose: add all engine-owned query services and typed contracts consumed by thin wrappers.

```powershell
git -C C:/Support/GitHub/TestimoX-master add `
  ADPlayground/Helpers/AdDomainControllersService.cs `
  ADPlayground/Helpers/AdGroupLookupStatus.cs `
  ADPlayground/Helpers/AdGroupMembersResolvedService.cs `
  ADPlayground/Helpers/AdGroupMembersService.cs `
  ADPlayground/Helpers/AdGroupsListService.cs `
  ADPlayground/Helpers/AdWhoAmIService.cs `
  ADPlayground/Helpers/DomainInfoService.cs `
  ADPlayground/Helpers/LdapToolAdLdapQueryPagedService.cs `
  ADPlayground/Helpers/LdapToolAdLdapQueryService.cs `
  ADPlayground/Helpers/LdapToolDelegationAuditService.cs `
  ADPlayground/Helpers/LdapToolObjectGetPolicy.cs `
  ADPlayground/Helpers/LdapToolObjectGetService.cs `
  ADPlayground/Helpers/LdapToolObjectResolveService.cs `
  ADPlayground/Helpers/LdapToolOffsetCursor.cs `
  ADPlayground/Helpers/LdapToolOutputRow.cs `
  ADPlayground/Helpers/LdapToolSearchAttributePolicy.cs `
  ADPlayground/Helpers/LdapToolSearchFacetsService.cs `
  ADPlayground/Helpers/LdapToolSearchService.cs `
  ADPlayground/Helpers/LdapToolSpnSearchService.cs `
  ADPlayground/Groups/DomainAdminsSummaryService.cs `
  ADPlayground/Groups/PrivilegedGroupsSummaryService.cs `
  ADPlayground/Replication/Services/ReplicationSummaryQueryService.cs `
  ADPlayground/ADPlayground.csproj
```

### ADP-02 Engine Model Alignments
Purpose: align existing engine models/services used by wrappers.

```powershell
git -C C:/Support/GitHub/TestimoX-master add `
  ADPlayground/DirectoryOps/ExpiredUsersModels.cs `
  ADPlayground/DirectoryOps/ExpiredUsersService.cs `
  ADPlayground/DirectoryOps/StaleAccountsModels.cs `
  ADPlayground/DirectoryOps/StaleAccountsService.cs `
  ADPlayground/Helpers/LdapToolContextHelper.cs `
  ADPlayground/Kerberos/SpnStatsService.cs
```

## IntelligenceX.Tools Repo (`C:/Support/GitHub/.wt-ix-tools-big-sweep-phase1`)

### IXT-01 Common Foundation
Purpose: shared binders/failure mappers/time/array parsing + deterministic engine-root selection.

```powershell
git -C C:/Support/GitHub/.wt-ix-tools-big-sweep-phase1 add `
  Directory.Build.props `
  IntelligenceX.Tools.Common/ToolArgs.cs `
  IntelligenceX.Tools.Common/ToolEnumBinders.cs `
  IntelligenceX.Tools.Common/ToolFailureMapper.cs `
  IntelligenceX.Tools.System/SystemToolBase.cs
```

### IXT-02 Active Directory Wrappers to Engine Contracts
Purpose: keep AD wrappers thin and consume ADPlayground services/contracts.

```powershell
git -C C:/Support/GitHub/.wt-ix-tools-big-sweep-phase1 add `
  IntelligenceX.Tools.ADPlayground/AdDelegationAuditTool.cs `
  IntelligenceX.Tools.ADPlayground/AdDomainAdminsSummaryTool.cs `
  IntelligenceX.Tools.ADPlayground/AdDomainControllersTool.cs `
  IntelligenceX.Tools.ADPlayground/AdDomainInfoTool.cs `
  IntelligenceX.Tools.ADPlayground/AdGroupMembersResolvedTool.cs `
  IntelligenceX.Tools.ADPlayground/AdGroupMembersTool.cs `
  IntelligenceX.Tools.ADPlayground/AdGroupsListTool.cs `
  IntelligenceX.Tools.ADPlayground/AdLdapDiagnosticsTool.cs `
  IntelligenceX.Tools.ADPlayground/AdLdapQueryPagedTool.cs `
  IntelligenceX.Tools.ADPlayground/AdLdapQueryTool.cs `
  IntelligenceX.Tools.ADPlayground/AdObjectGetTool.cs `
  IntelligenceX.Tools.ADPlayground/AdObjectResolveTool.cs `
  IntelligenceX.Tools.ADPlayground/AdPrivilegedGroupsSummaryTool.cs `
  IntelligenceX.Tools.ADPlayground/AdQueryResultHelpers.cs `
  IntelligenceX.Tools.ADPlayground/AdReplicationSummaryTool.cs `
  IntelligenceX.Tools.ADPlayground/AdSearchFacetsTool.cs `
  IntelligenceX.Tools.ADPlayground/AdSearchTool.cs `
  IntelligenceX.Tools.ADPlayground/AdSpnSearchTool.cs `
  IntelligenceX.Tools.ADPlayground/AdSpnStatsTool.cs `
  IntelligenceX.Tools.ADPlayground/AdStaleAccountsTool.cs `
  IntelligenceX.Tools.ADPlayground/AdUsersExpiredTool.cs `
  IntelligenceX.Tools.ADPlayground/AdWhoAmITool.cs `
  IntelligenceX.Tools.ADPlayground/AdSearchRow.cs
```

### IXT-03 Event/System/Email Wrapper Dedupe
Purpose: remove wrapper-local result/parser duplication and use common helpers.

```powershell
git -C C:/Support/GitHub/.wt-ix-tools-big-sweep-phase1 add `
  IntelligenceX.Tools.EventLog/EventLogEvtxAccountLockoutsReportTool.cs `
  IntelligenceX.Tools.EventLog/EventLogEvtxFailedLogonsReportTool.cs `
  IntelligenceX.Tools.EventLog/EventLogEvtxQueryTool.cs `
  IntelligenceX.Tools.EventLog/EventLogEvtxSecurityReportHelper.cs `
  IntelligenceX.Tools.EventLog/EventLogEvtxStatsTool.cs `
  IntelligenceX.Tools.EventLog/EventLogEvtxUserLogonsReportTool.cs `
  IntelligenceX.Tools.EventLog/EventLogLiveStatsTool.cs `
  IntelligenceX.Tools.EventLog/EventLogPackInfoTool.cs `
  IntelligenceX.Tools.EventLog/EventLogToolBase.cs `
  IntelligenceX.Tools.EventLog/EventLogAggregates.cs `
  IntelligenceX.Tools.EventLog/EventLogTopRow.cs `
  IntelligenceX.Tools.EventLog/EventLogArgs.cs `
  IntelligenceX.Tools.System/SystemDevicesSummaryTool.cs `
  IntelligenceX.Tools.System/SystemDisksListTool.cs `
  IntelligenceX.Tools.System/SystemFeaturesListTool.cs `
  IntelligenceX.Tools.System/SystemFirewallProfilesTool.cs `
  IntelligenceX.Tools.System/SystemFirewallRulesTool.cs `
  IntelligenceX.Tools.System/SystemHardwareIdentityTool.cs `
  IntelligenceX.Tools.System/SystemHardwareSummaryTool.cs `
  IntelligenceX.Tools.System/SystemInfoTool.cs `
  IntelligenceX.Tools.System/SystemLogicalDisksListTool.cs `
  IntelligenceX.Tools.System/SystemNetworkAdaptersTool.cs `
  IntelligenceX.Tools.System/SystemPortsListTool.cs `
  IntelligenceX.Tools.System/SystemProcessListTool.cs `
  IntelligenceX.Tools.System/SystemScheduledTasksListTool.cs `
  IntelligenceX.Tools.System/SystemServiceListTool.cs `
  IntelligenceX.Tools.System/SystemWhoAmITool.cs `
  IntelligenceX.Tools.Email/EmailSmtpSendTool.cs
```

### IXT-04 Guardrails and Sweep Docs
Purpose: prevent regressions and keep the cleanup plan synchronized.

```powershell
git -C C:/Support/GitHub/.wt-ix-tools-big-sweep-phase1 add `
  IntelligenceX.Tools.Tests/SourceGuardrailTests.cs `
  Docs/ENGINE_FIRST_BIG_SWEEP_TODO.md `
  Docs/ENGINE_FIRST_BIG_SWEEP_CHANGESETS.md
```

## Suggested Non-Interactive Commits

```powershell
git -C C:/Support/GitHub/TestimoX-master commit -m "ADPlayground: add engine-first LDAP/group/domain services for thin tool wrappers"
git -C C:/Support/GitHub/TestimoX-master commit -m "ADPlayground: align directory ops and SPN models with tool contracts"

git -C C:/Support/GitHub/.wt-ix-tools-big-sweep-phase1 commit -m "Tools.Common: add shared binders/failure mappers and parser helpers"
git -C C:/Support/GitHub/.wt-ix-tools-big-sweep-phase1 commit -m "Tools.AD: convert wrappers to engine-owned query services/contracts"
git -C C:/Support/GitHub/.wt-ix-tools-big-sweep-phase1 commit -m "Tools.Event/System/Email: remove wrapper-local parser/result duplication"
git -C C:/Support/GitHub/.wt-ix-tools-big-sweep-phase1 commit -m "Tools.Tests/Docs: add guardrails and sync engine-first sweep tracker"
```
