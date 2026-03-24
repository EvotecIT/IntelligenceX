using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class ToolMarkdownContractTests {
    [Fact]
    public void DocumentBuilder_ShouldRenderMermaidAndGenericVisualFences() {
        var markdown = ToolMarkdownContract.Create()
            .AddHeading(3, "Visualization")
            .AddMermaid("graph TD\nA-->B", "Topology")
            .AddChart("{\"type\":\"bar\",\"data\":{\"labels\":[\"A\"],\"datasets\":[{\"label\":\"X\",\"data\":[1]}]}}", "Counts")
            .AddNetwork("{\"nodes\":[{\"id\":\"A\",\"label\":\"User\"},{\"id\":\"B\",\"label\":\"Group\"}],\"edges\":[{\"from\":\"A\",\"to\":\"B\"}]}", "Relationships")
            .AddDataView("{\"rows\":[[\"Name\",\"Count\"],[\"alpha\",\"1\"]]}", "Tabular")
            .Build();

        Assert.Contains("### Visualization", markdown);
        Assert.Contains("#### Topology", markdown);
        Assert.Contains("```mermaid", markdown);
        Assert.Contains("graph TD", markdown);
        Assert.Contains("```chart", markdown);
        Assert.Contains("#### Counts", markdown);
        Assert.Contains("```network", markdown);
        Assert.Contains("#### Relationships", markdown);
        Assert.Contains("```dataview", markdown);
        Assert.Contains("#### Tabular", markdown);
    }

    [Fact]
    public void DocumentBuilder_ShouldKeepIntelligenceXAliasFencesForCompatibility() {
        var markdown = ToolMarkdownContract.Create()
            .AddIxChart("{\"type\":\"bar\",\"data\":{}}", "Legacy chart")
            .AddIxNetwork("{\"nodes\":[],\"edges\":[]}", "Legacy network")
            .AddIxDataView("{\"rows\":[[\"Name\"],[\"alpha\"]]}", "Legacy dataview")
            .Build();

        Assert.Contains("#### Legacy chart", markdown);
        Assert.Contains("```ix-chart", markdown);
        Assert.Contains("#### Legacy network", markdown);
        Assert.Contains("```ix-network", markdown);
        Assert.Contains("#### Legacy dataview", markdown);
        Assert.Contains("```ix-dataview", markdown);
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
