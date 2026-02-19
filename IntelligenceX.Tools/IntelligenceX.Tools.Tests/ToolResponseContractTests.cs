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

    [Fact]
    public void OkWriteActionModel_ShouldEmitStandardWriteMetadataAndSummary() {
        var payload = new { Sent = true, Provider = "smtp" };
        var json = ToolResponse.OkWriteActionModel(
            model: payload,
            action: "smtp_send",
            writeApplied: true,
            facts: new[] {
                ("Target", "alerts@contoso.com")
            },
            summaryTitle: "SMTP send");

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"sent\":true", json);
        Assert.Contains("\"provider\":\"smtp\"", json);
        Assert.Contains("\"mode\":\"apply\"", json);
        Assert.Contains("\"write_applied\":true", json);
        Assert.Contains("\"action\":\"smtp_send\"", json);
        Assert.Contains("SMTP send", json);
        Assert.Contains("Target", json);
    }

    private sealed class MermaidPayload {
        public string MermaidSource { get; init; } = string.Empty;
    }
}
