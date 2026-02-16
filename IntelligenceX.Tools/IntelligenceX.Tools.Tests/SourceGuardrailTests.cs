using System;
using System.IO;
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
            Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog", "EventLogEvtxFailedLogonsReportTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog", "EventLogEvtxAccountLockoutsReportTool.cs"),
            Path.Combine(repoRoot, "IntelligenceX.Tools.EventLog", "EventLogEvtxUserLogonsReportTool.cs"),
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
