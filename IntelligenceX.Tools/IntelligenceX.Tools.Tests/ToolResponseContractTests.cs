using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class ToolResponseContractTests {
    [Fact]
    public void OkMermaidModel_ShouldEmitMermaidRenderHint() {
        var payload = new MermaidPayload {
            MermaidSource = "graph TD;A-->B;"
        };

        var json = ToolResponse.OkMermaidModel(
            model: payload,
            mermaidPath: "mermaid_source",
            summaryMarkdown: "### Diagram");

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"kind\":\"code\"", json);
        Assert.Contains("\"language\":\"mermaid\"", json);
        Assert.Contains("\"content_path\":\"mermaid_source\"", json);
        Assert.Contains("\"summary_markdown\":\"### Diagram\"", json);
    }

    [Fact]
    public void OkFactsModel_ShouldEmitSummaryTableAndMeta() {
        var payload = new { Name = "HostA", Value = 42 };
        var json = ToolResponse.OkFactsModel(
            model: payload,
            title: "Facts",
            facts: new[] {
                ("Name", "HostA"),
                ("Value", "42")
            });

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"name\":\"HostA\"", json);
        Assert.Contains("\"count\":2", json);
        Assert.Contains("Facts", json);
        Assert.Contains("Name", json);
        Assert.Contains("Value", json);
    }

    private sealed class MermaidPayload {
        public string MermaidSource { get; init; } = string.Empty;
    }
}
