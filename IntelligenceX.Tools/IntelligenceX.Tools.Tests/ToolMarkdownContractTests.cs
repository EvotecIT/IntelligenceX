using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class ToolMarkdownContractTests {
    [Fact]
    public void DocumentBuilder_ShouldRenderMermaidAndChartFences() {
        var markdown = ToolMarkdownContract.Create()
            .AddHeading(3, "Visualization")
            .AddMermaid("graph TD\nA-->B", "Topology")
            .AddChart("{\"type\":\"bar\",\"data\":{}}", "Counts")
            .Build();

        Assert.Contains("### Visualization", markdown);
        Assert.Contains("#### Topology", markdown);
        Assert.Contains("```mermaid", markdown);
        Assert.Contains("graph TD", markdown);
        Assert.Contains("```chart", markdown);
        Assert.Contains("#### Counts", markdown);
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
