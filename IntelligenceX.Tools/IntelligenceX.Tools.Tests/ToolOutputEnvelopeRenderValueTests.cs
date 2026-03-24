using System.Text.Json;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolOutputEnvelopeRenderValueTests {
    [Fact]
    public void OkFlatWithRenderValue_AllowsRenderHintArrays() {
        var root = new JsonObject().Add("name", "demo");
        var meta = ToolOutputHints.Meta(count: 1, truncated: false);
        var render = new JsonArray()
            .Add(ToolOutputHints.RenderTable(
                "rows",
                new ToolColumn("id", "Id", "string")))
            .Add(ToolOutputHints.RenderNetwork("graph"));

        var json = ToolOutputEnvelope.OkFlatWithRenderValue(
            root: root,
            meta: meta,
            summaryMarkdown: "demo",
            render: JsonValue.From(render));

        using var document = JsonDocument.Parse(json);
        var envelope = document.RootElement;
        Assert.True(envelope.GetProperty("ok").GetBoolean());
        Assert.Equal("demo", envelope.GetProperty("summary_markdown").GetString());
        Assert.Equal(global::System.Text.Json.JsonValueKind.Array, envelope.GetProperty("render").ValueKind);
        Assert.Equal(2, envelope.GetProperty("render").GetArrayLength());
        Assert.Equal("network", envelope.GetProperty("render")[1].GetProperty("language").GetString());
    }

    [Fact]
    public void RenderVisualHelpers_UseGenericLanguagesByDefault() {
        var chart = ToolOutputHints.RenderChart("chart_payload");
        var network = ToolOutputHints.RenderNetwork("graph_payload");
        var dataView = ToolOutputHints.RenderDataView("table_payload");

        Assert.Equal("chart", chart.GetString("language"));
        Assert.Equal("chart_payload", chart.GetString("content_path"));
        Assert.Equal("network", network.GetString("language"));
        Assert.Equal("graph_payload", network.GetString("content_path"));
        Assert.Equal("dataview", dataView.GetString("language"));
        Assert.Equal("table_payload", dataView.GetString("content_path"));
    }
}
