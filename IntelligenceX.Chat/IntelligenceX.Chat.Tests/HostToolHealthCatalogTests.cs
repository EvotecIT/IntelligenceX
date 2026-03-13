using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Host;
using IntelligenceX.Chat.Tooling;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class HostToolHealthCatalogTests {
    [Fact]
    public void FilterToolHealthProbeCatalog_AppliesSourceAndPackFiltersAgainstSharedCatalog() {
        var catalog = new[] {
            new ToolHealthDiagnostics.PackInfoProbeCatalogEntry(
                ToolName: "ad_pack_info",
                PackId: "active_directory",
                PackName: "ADPlayground",
                SourceKind: ToolPackSourceKind.ClosedSource),
            new ToolHealthDiagnostics.PackInfoProbeCatalogEntry(
                ToolName: "eventlog_pack_info",
                PackId: "eventlog",
                PackName: "Event Viewer",
                SourceKind: ToolPackSourceKind.OpenSource),
            new ToolHealthDiagnostics.PackInfoProbeCatalogEntry(
                ToolName: "system_pack_info",
                PackId: "system",
                PackName: "ComputerX",
                SourceKind: ToolPackSourceKind.ClosedSource)
        };

        var filtered = Program.FilterToolHealthProbeCatalog(
            catalog,
            new Program.ToolHealthFilter(
                SourceKinds: new HashSet<string> { "closed_source" },
                PackIds: new HashSet<string> { "active_directory", "eventlog" }));

        var entry = Assert.Single(filtered);
        Assert.Equal("ad_pack_info", entry.ToolName);
        Assert.Equal("active_directory", entry.PackId);
        Assert.Equal(ToolPackSourceKind.ClosedSource, entry.SourceKind);
    }
}
