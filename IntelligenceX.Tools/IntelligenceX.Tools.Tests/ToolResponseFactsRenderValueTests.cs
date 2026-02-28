using System.Linq;
using System.Text.Json;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolResponseFactsRenderValueTests {
    [Fact]
    public void OkFactsModelWithRenderValue_AllowsRenderHintArrays() {
        var model = new JsonObject().Add("name", "posture");
        var renderHints = new JsonArray()
            .Add(ToolOutputHints.RenderTable(
                    "warnings",
                    new ToolColumn("value", "Warning", "string"))
                .Add("priority", 300));

        var json = ToolResponse.OkFactsModelWithRenderValue(
            model: model,
            title: "Demo",
            facts: new[] { ("Field", "Value") },
            render: JsonValue.From(renderHints));

        using var document = JsonDocument.Parse(json);
        var envelope = document.RootElement;

        Assert.True(envelope.GetProperty("ok").GetBoolean());
        Assert.Equal(global::System.Text.Json.JsonValueKind.Array, envelope.GetProperty("render").ValueKind);
        Assert.Equal("warnings", envelope.GetProperty("render").EnumerateArray().First().GetProperty("rows_path").GetString());
        Assert.True(envelope.TryGetProperty("summary_markdown", out _));
        Assert.True(envelope.TryGetProperty("meta", out _));
    }
}
