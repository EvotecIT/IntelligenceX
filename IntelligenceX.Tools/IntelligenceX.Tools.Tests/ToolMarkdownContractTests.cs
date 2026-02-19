using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class ToolMarkdownContractTests {
    [Fact]
    public void DocumentBuilder_ShouldRenderMermaidChartAndNetworkFences() {
        var markdown = ToolMarkdownContract.Create()
            .AddHeading(3, "Visualization")
            .AddMermaid("graph TD\nA-->B", "Topology")
            .AddIxChart("{\"type\":\"bar\",\"data\":{\"labels\":[\"A\"],\"datasets\":[{\"label\":\"X\",\"data\":[1]}]}}", "Counts")
            .AddIxNetwork("{\"nodes\":[{\"id\":\"A\",\"label\":\"User\"},{\"id\":\"B\",\"label\":\"Group\"}],\"edges\":[{\"from\":\"A\",\"to\":\"B\"}]}", "Relationships")
            .Build();

        Assert.Contains("### Visualization", markdown);
        Assert.Contains("#### Topology", markdown);
        Assert.Contains("```mermaid", markdown);
        Assert.Contains("graph TD", markdown);
        Assert.Contains("```ix-chart", markdown);
        Assert.Contains("#### Counts", markdown);
        Assert.Contains("```ix-network", markdown);
        Assert.Contains("#### Relationships", markdown);
    }

    [Fact]
    public void DocumentBuilder_ShouldKeepLegacyChartFenceForCompatibility() {
        var markdown = ToolMarkdownContract.Create()
            .AddChart("{\"type\":\"bar\",\"data\":{}}", "Legacy")
            .Build();

        Assert.Contains("#### Legacy", markdown);
        Assert.Contains("```chart", markdown);
    }

    [Fact]
    public void DocumentBuilder_ShouldRenderTableSection() {
        var markdown = ToolMarkdownContract.Create()
            .AddTable(
                title: "Top Users",
                headers: new[] { "User", "Count" },
                rows: new[] { new[] { "alice", "5" }, new[] { "bob", "3" } },
                totalCount: 2,
                truncated: false)
            .Build();

        Assert.Contains("### Top Users", markdown);
        Assert.Contains("| User | Count |", markdown);
        Assert.Contains("| alice | 5 |", markdown);
        Assert.Contains("Count: 2", markdown);
    }
}
