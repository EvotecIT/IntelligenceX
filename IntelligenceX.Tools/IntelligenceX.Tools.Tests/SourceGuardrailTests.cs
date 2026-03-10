using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class SourceGuardrailTests {
    [Theory]
    [InlineData("AdSearchTool.cs")]
    [InlineData("AdObjectGetTool.cs")]
    [InlineData("AdLdapQueryTool.cs")]
    [InlineData("AdLdapQueryPagedTool.cs")]
    [InlineData("AdGroupMembersTool.cs")]
    [InlineData("AdGroupMembersResolvedTool.cs")]
    [InlineData("AdGroupsListTool.cs")]
    [InlineData("AdSpnSearchTool.cs")]
    [InlineData("AdDomainControllersTool.cs")]
    [InlineData("AdDelegationAuditTool.cs")]
    [InlineData("AdSearchFacetsTool.cs")]
    public void ActiveDirectoryThinWrappers_ShouldNotContainRawLdapLoops(string fileName) {
        var repoRoot = FindRepoRoot();
        var filePath = Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", fileName);
        var source = File.ReadAllText(filePath);

        Assert.DoesNotContain("SearchResult sr", source);
        Assert.DoesNotContain(".Properties[", source);
        Assert.DoesNotContain(".Properties.Contains(", source);
        Assert.DoesNotContain("LdapSearchHelper.Search(", source);
        Assert.DoesNotContain("LdapSearchHelper.SearchWithTimeout(", source);
    }

    [Fact]
    public void DomainInfoTool_ShouldNotImplementLocalDnParsing() {
        var repoRoot = FindRepoRoot();
        var filePath = Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdDomainInfoTool.cs");
        var source = File.ReadAllText(filePath);

        Assert.DoesNotContain("Split(',')", source);
        Assert.DoesNotContain("StartsWith(\"DC=\"", source);
    }

    [Fact]
    public void EventLogTools_ShouldNotUseLowLevelEventParsingApis() {
        var repoRoot = FindRepoRoot();
        var folder = Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog");
        var files = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories);

        foreach (var file in files) {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("using System.Diagnostics.Eventing.Reader", source);
            Assert.DoesNotContain("EventLogRecord", source);
            Assert.DoesNotContain("EventRecord", source);
            Assert.DoesNotContain("XmlDocument", source);
            Assert.DoesNotContain("XDocument", source);
            Assert.DoesNotContain("XPathDocument", source);
        }
    }

    [Theory]
    [InlineData("IntelligenceX.Tools.ADPlayground", "AdEnvironmentDiscoverTool.cs", "AddReadOnlyEnvironmentChainingMeta(")]
    [InlineData("IntelligenceX.Tools.EventLog", "EventLogLiveStatsTool.cs", "AddReadOnlyTriageChainingMeta(")]
    [InlineData("IntelligenceX.Tools.EventLog", "EventLogLiveQueryTool.cs", "AddReadOnlyTriageChainingMeta(")]
    [InlineData("IntelligenceX.Tools.EventLog", "EventLogTopEventsTool.cs", "AddReadOnlyTriageChainingMeta(")]
    [InlineData("IntelligenceX.Tools.PowerShell", "PowerShellEnvironmentDiscoverTool.cs", "AddReadOnlyRuntimeChainingMeta(")]
    [InlineData("IntelligenceX.Tools.System", "SystemInfoTool.cs", "AddReadOnlyPostureChainingMeta(")]
    [InlineData("IntelligenceX.Tools.System", "SystemUpdatesInstalledTool.cs", "AddReadOnlyPostureChainingMeta(")]
    public void BaselineTriageTools_ShouldEmitChainingMetadata(
        string projectFolder,
        string fileName,
        string requiredSnippet) {
        var repoRoot = FindRepoRoot();
        var filePath = Path.Combine(repoRoot, projectFolder, fileName);
        var source = File.ReadAllText(filePath);

        Assert.Contains(
            requiredSnippet,
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SystemTools_ShouldDependOnComputerXExecutors() {
        var repoRoot = FindRepoRoot();
        var folder = Path.Combine(repoRoot, "IntelligenceX.Tools.System");
        var files = Directory.EnumerateFiles(folder, "*Tool.cs", SearchOption.TopDirectoryOnly);

        foreach (var file in files) {
            var name = Path.GetFileName(file);
            if (!name.StartsWith("System", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("WslStatusTool.cs", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (name.Equals("SystemPackInfoTool.cs", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var source = File.ReadAllText(file);
            Assert.Contains("ComputerX.", source);
            Assert.DoesNotContain("new ManagementObjectSearcher(", source);
            Assert.DoesNotContain("Process.GetProcesses(", source);
            Assert.DoesNotContain("NetworkInterface.GetAllNetworkInterfaces(", source);
        }
    }

    [Fact]
    public void FileSystemTools_ShouldUseComputerXFileSystemQuery() {
        var repoRoot = FindRepoRoot();
        var folder = Path.Combine(repoRoot, "IntelligenceX.Tools.FileSystem");
        var files = Directory.EnumerateFiles(folder, "Fs*Tool.cs", SearchOption.TopDirectoryOnly);

        foreach (var file in files) {
            var source = File.ReadAllText(file);
            Assert.Contains("FileSystemQuery.", source);
        }
    }

    [Fact]
    public void Tools_ShouldNotReintroduceRemovedLocalWrapperClones() {
        var repoRoot = FindRepoRoot();

        string[] removedFiles = {
            Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog", "EventLogTopRow.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog", "EventLogAggregates.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog", "EventLogArgs.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdSearchRow.cs")
        };

        foreach (var removedFile in removedFiles) {
            Assert.False(File.Exists(removedFile), $"Removed thin-wrapper file should not exist: {removedFile}");
        }

        string[] filesToScan = {
            Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog", "EventLogPackInfoTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdSpnStatsTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdStaleAccountsTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdUsersExpiredTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdWhoAmITool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdDomainInfoTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdPrivilegedGroupsSummaryTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdDomainAdminsSummaryTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdReplicationSummaryTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdDomainControllersTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdDelegationAuditTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdSearchFacetsTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdSearchTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdSpnSearchTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdObjectGetTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdLdapQueryTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdLdapQueryPagedTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdGroupsListTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdGroupMembersTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdGroupMembersResolvedTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdObjectResolveTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemInfoTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemHardwareIdentityTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemWhoAmITool.cs")
        };

        string[] bannedTypeNames = {
            "FailedLogonsReportResult",
            "AccountLockoutsReportResult",
            "UserLogonsReportResult",
            "PackInfoResult",
            "DelegationAuditResult",
            "DelegationAuditRow",
            "AdSearchFacetsResult",
            "AdSearchResult",
            "EnabledFacet",
            "ContainerFacetRow",
            "UacFlagFacetRow",
            "PwdAgeBucketRow",
            "LdapQueryResult",
            "LdapQueryPagedResult",
            "SpnServiceClassRow",
            "SpnHostRow",
            "SpnSearchResult",
            "SpnStatsResult",
            "StaleAccountRow",
            "ExpiredUserRow",
            "SystemInfoResult",
            "OsSummary",
            "OsDetail",
            "ComputerSystemDetail",
            "HardwareIdentityResult",
            "BiosRow",
            "BaseBoardRow",
            "WhoAmIResult",
            "DomainInfoResult",
            "PrivilegedGroupsSummaryResult",
            "PrivilegedGroupRow",
            "DomainAdminsSummaryResult",
            "DomainAdminMemberRow",
            "ReplicationSummaryResult",
            "ReplicationSummaryRow",
            "ReplicationDetailRow",
            "GroupsListResult",
            "GroupMembersResult",
            "GroupMembersResolvedResult",
            "AdObjectResolveRow",
            "AdObjectResolveResult",
            "AdSearchRow",
            "FirewallRuleListResult",
            "FirewallRuleRow",
            "FirewallProfileListResult",
            "FirewallProfileRow",
            "DomainControllersResult",
            "ObjectGetResult"
        };

        foreach (var file in filesToScan) {
            var source = File.ReadAllText(file);
            foreach (var banned in bannedTypeNames) {
                Assert.DoesNotContain($"class {banned}", source);
            }
        }
    }

    [Fact]
    public void ToolWrappers_ShouldNotReintroduceLocalArgumentParserHelpers() {
        var repoRoot = FindRepoRoot();
        string[] toolFolders = {
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.FileSystem"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.Email")
        };

        string[] bannedSnippets = {
            "private static List<string> ReadDistinct(",
            "private static List<string> ReadStringArray(",
            "private static List<string> ReadIdentities(",
            "private static List<int> ReadIntList(",
            "private static List<int> ReadBuckets(",
            "private static SearchScope ParseScope(",
            "private static int? ReadPositiveInt(",
            "private static bool TryParseProtocol(",
            "private static bool TryParseProfile(",
            "private static string EncodeOffsetCursor("
        };

        foreach (var folder in toolFolders) {
            var files = Directory.EnumerateFiles(folder, "*Tool.cs", SearchOption.TopDirectoryOnly);
            foreach (var file in files) {
                var source = File.ReadAllText(file);
                foreach (var banned in bannedSnippets) {
                    Assert.DoesNotContain(banned, source);
                }
            }
        }
    }

    [Fact]
    public void ActiveDirectoryToolBaseHelpers_ShouldUseToolResultV2InsteadOfToolResponse() {
        var repoRoot = FindRepoRoot();
        var folder = Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground");
        var files = Directory.EnumerateFiles(folder, "ActiveDirectoryToolBase*.cs", SearchOption.TopDirectoryOnly);

        foreach (var file in files) {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("ToolResponse.", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AdGpoInventoryHealthTool_ShouldUseSharedTypedRowsViewHelperPath() {
        var repoRoot = FindRepoRoot();
        var filePath = Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdGpoInventoryHealthTool.cs");
        var source = File.ReadAllText(filePath);

        Assert.Contains("RunPipelineAsync(", source, StringComparison.Ordinal);
        Assert.Contains("ToolRequestBindingResult<", source, StringComparison.Ordinal);
        Assert.Contains("ExecuteDomainRowsViewTool(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolResponse.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("arguments?.Get", source, StringComparison.Ordinal);
        Assert.DoesNotContain("arguments.Get", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveDirectoryTools_ShouldNotLeakRawExceptionMessagesInToolErrors() {
        var repoRoot = FindRepoRoot();
        var folder = Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground");
        var files = Directory.EnumerateFiles(folder, "*Tool.cs", SearchOption.TopDirectoryOnly);
        var errorCallPattern = new Regex(
            @"ToolResponse\.Error\([\s\S]*?\);",
            RegexOptions.CultureInvariant);

        foreach (var file in files) {
            var source = File.ReadAllText(file);
            foreach (Match match in errorCallPattern.Matches(source)) {
                Assert.True(
                    !match.Value.Contains("ex.Message", StringComparison.Ordinal),
                    $"Tool should not pass raw ex.Message into ToolResponse.Error: {Path.GetFileName(file)}");
            }
        }
    }

    [Fact]
    public void Wave2NoArgPackAndDiscoveryTools_ShouldUseTypedPipelineAndResultV2() {
        var repoRoot = FindRepoRoot();
        string[] filePaths = {
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemPackInfoTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemDevicesSummaryTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemHardwareIdentityTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemHardwareSummaryTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemInfoTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemBiosSummaryTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemSecurityOptionsTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemBootConfigurationTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemRdpPostureTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemSmbPostureTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemFeaturesListTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemUpdatesInstalledTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemPatchDetailsTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemDisksListTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemLogicalDisksListTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemPortsListTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemProcessListTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemServiceListTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemScheduledTasksListTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemFirewallRulesTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemFirewallProfilesTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemTimeSyncTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemWhoAmITool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "WslStatusTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemBitlockerStatusTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemInstalledApplicationsTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemNetworkAdaptersTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemPatchComplianceTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog", "EventLogPackInfoTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog", "EventLogNamedEventsCatalogTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog", "EventLogNamedEventsQueryTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog", "EventLogLiveQueryTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog", "EventLogTopEventsTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog", "EventLogLiveStatsTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog", "EventLogEvtxFindTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog", "EventLogEvtxQueryTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog", "EventLogEvtxStatsTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog", "EventLogEvtxSecuritySummaryTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog", "EventLogTimelineQueryTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog", "EventLogTimelineExplainTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.TestimoX", "TestimoXPackInfoTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.TestimoX", "TestimoXRulesListTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.TestimoX", "TestimoXRulesRunTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.TestimoX.Analytics", "TestimoXAnalyticsPackInfoTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.TestimoX.Analytics", "TestimoXAnalyticsDiagnosticsGetTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.FileSystem", "FileSystemPackInfoTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.FileSystem", "FsListTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.FileSystem", "FsReadTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.FileSystem", "FsSearchTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.Email", "EmailPackInfoTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.Email", "EmailImapSearchTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.Email", "EmailImapGetTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.PowerShell", "PowerShellPackInfoTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.PowerShell", "PowerShellRunTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.OfficeIMO", "OfficeImoPackInfoTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.OfficeIMO", "OfficeImoReadTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.PowerShell", "PowerShellEnvironmentDiscoverTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.PowerShell", "PowerShellHostsTool.cs")
        };

        foreach (var filePath in filePaths) {
            var source = File.ReadAllText(filePath);
            Assert.Contains("RunPipelineAsync(", source, StringComparison.Ordinal);
            Assert.Contains("ToolRequestBindingResult<", source, StringComparison.Ordinal);
            Assert.Contains("ToolResultV2.", source, StringComparison.Ordinal);

            Assert.DoesNotContain("ToolResponse.Ok", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ToolResponse.Error(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("arguments?.Get", source, StringComparison.Ordinal);
            Assert.DoesNotContain("arguments.Get", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RemoteCapableSystemComputerXWrappers_ShouldExposeComputerNameAndPassThroughComputerXScope() {
        var repoRoot = FindRepoRoot();
        string[] filePaths = {
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemProcessListTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemNetworkAdaptersTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemPortsListTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemServiceListTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemScheduledTasksListTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemDevicesSummaryTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemFeaturesListTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemDisksListTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System", "SystemLogicalDisksListTool.cs")
        };

        foreach (var filePath in filePaths) {
            var source = File.ReadAllText(filePath);
            Assert.Contains("\"computer_name\"", source, StringComparison.Ordinal);
            Assert.Contains("ResolveTargetComputerName(computerName)", source, StringComparison.Ordinal);
            Assert.Contains("ComputerName = request.ComputerName", source, StringComparison.Ordinal);
            Assert.Contains("AddComputerNameMeta(", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Wave1AndAdReadOnlyWrappers_ShouldUseTypedPipelineAndResultV2() {
        var repoRoot = FindRepoRoot();
        string[] filePaths = {
            Path.Combine(repoRoot, "IntelligenceX.Tools.DomainDetective", "DomainDetectivePackInfoTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.DomainDetective", "DomainDetectiveChecksCatalogTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.DomainDetective", "DomainDetectiveNetworkProbeTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.DomainDetective", "DomainDetectiveDomainSummaryTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.DnsClientX", "DnsClientXPackInfoTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.DnsClientX", "DnsClientXQueryTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.DnsClientX", "DnsClientXPingTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdDomainAdminsSummaryTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdPrivilegedGroupsSummaryTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdStaleAccountsTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdUsersExpiredTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdWhoAmITool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdPackInfoTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdScopeDiscoveryTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdMonitoringProbeCatalogTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdRecycleBinLifetimeTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdGroupMembersTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdGroupMembersResolvedTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdGroupsListTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdAdminCountReportTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdGpoChangesTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdGpoListTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdGpoHealthTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdGpoPermissionReportTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdGpoPermissionConsistencyTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdLdapQueryTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdLdapQueryPagedTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdObjectGetTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdObjectResolveTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdSearchTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdSpnSearchTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdDomainInfoTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdLdapDiagnosticsTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdSearchFacetsTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdReplicationSummaryTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground", "AdReplicationConnectionsTool.cs")
        };

        foreach (var filePath in filePaths) {
            var source = File.ReadAllText(filePath);
            Assert.Contains("RunPipelineAsync(", source, StringComparison.Ordinal);
            Assert.Contains("ToolRequestBindingResult<", source, StringComparison.Ordinal);
            Assert.Contains("ToolResultV2.", source, StringComparison.Ordinal);

            Assert.DoesNotContain("ToolResponse.Ok", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ToolResponse.Error(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("arguments?.Get", source, StringComparison.Ordinal);
            Assert.DoesNotContain("arguments.Get", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReviewerSetupWrappers_ShouldUseTypedPipelineAndResultV2() {
        var repoRoot = FindRepoRoot();
        string[] filePaths = {
            Path.Combine(repoRoot, "IntelligenceX.Tools.ReviewerSetup", "ReviewerSetupPackInfoTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ReviewerSetup", "ReviewerSetupContractVerifyTool.cs")
        };

        foreach (var filePath in filePaths) {
            var source = File.ReadAllText(filePath);
            Assert.Contains("RunPipelineAsync(", source, StringComparison.Ordinal);
            Assert.Contains("ToolRequestBindingResult<", source, StringComparison.Ordinal);
            Assert.Contains("ToolResultV2.", source, StringComparison.Ordinal);

            Assert.DoesNotContain("ToolResponse.Ok", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ToolResponse.Error(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("arguments?.Get", source, StringComparison.Ordinal);
            Assert.DoesNotContain("arguments.Get", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RefactoredPackWrappers_ShouldUseTypedPipelineBinder() {
        var repoRoot = FindRepoRoot();
        string[] targetToolFolders = {
            Path.Combine(repoRoot, "IntelligenceX.Tools.DomainDetective"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.DnsClientX"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ReviewerSetup"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.TestimoX"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.TestimoX.Analytics"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.FileSystem"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.Email"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.PowerShell"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.OfficeIMO")
        };

        foreach (var folder in targetToolFolders) {
            var files = Directory.EnumerateFiles(folder, "*Tool.cs", SearchOption.TopDirectoryOnly);
            foreach (var file in files) {
                var source = File.ReadAllText(file);
                Assert.Contains("RunPipelineAsync(", source, StringComparison.Ordinal);
                Assert.Contains("ToolRequestBindingResult<", source, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void RefactoredTypedPipelineTools_ShouldNotUseRawArgumentGetPatterns() {
        var repoRoot = FindRepoRoot();
        string[] targetToolFolders = {
            Path.Combine(repoRoot, "IntelligenceX.Tools.ADPlayground"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.DomainDetective"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.DnsClientX"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.System"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.TestimoX"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.TestimoX.Analytics"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.FileSystem"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.Email"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.PowerShell"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.OfficeIMO"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.ReviewerSetup")
        };

        foreach (var folder in targetToolFolders) {
            var files = Directory.EnumerateFiles(folder, "*Tool.cs", SearchOption.TopDirectoryOnly);
            foreach (var file in files) {
                var source = File.ReadAllText(file);
                if (!source.Contains("RunPipelineAsync(", StringComparison.Ordinal)
                    || !source.Contains("ToolRequestBindingResult<", StringComparison.Ordinal)) {
                    continue;
                }

                Assert.DoesNotContain("arguments?.Get", source, StringComparison.Ordinal);
                Assert.DoesNotContain("arguments.Get", source, StringComparison.Ordinal);
            }
        }
    }

    private static string FindRepoRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir.FullName, "IntelligenceX.Tools.sln"))) {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Unable to locate IntelligenceX.Tools repository root.");
    }
}
