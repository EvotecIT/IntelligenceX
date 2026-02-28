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
            .Add(ToolOutputHints.RenderCode(language: "ix-network", contentPath: "graph"));

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
    }
}
