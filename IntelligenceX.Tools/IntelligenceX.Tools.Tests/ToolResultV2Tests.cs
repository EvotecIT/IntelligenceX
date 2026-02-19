using System.Text.Json;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolResultV2Tests {
    [Fact]
    public void OkWriteActionModel_ShouldNotMutateCallerMetaObject() {
        var meta = new JsonObject()
            .Add("trace_id", "abc-123");

        var json = ToolResultV2.OkWriteActionModel(
            model: new { Result = "ok" },
            action: "demo_action",
            writeApplied: true,
            meta: meta);

        Assert.Null(meta.GetString("mode"));
        Assert.Null(meta.GetString("action"));
        Assert.False(meta.GetBoolean("write_applied", defaultValue: false));
        Assert.Equal("abc-123", meta.GetString("trace_id"));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        var responseMeta = root.GetProperty("meta");
        Assert.Equal("apply", responseMeta.GetProperty("mode").GetString());
        Assert.Equal("demo_action", responseMeta.GetProperty("action").GetString());
        Assert.True(responseMeta.GetProperty("write_applied").GetBoolean());
        Assert.Equal("abc-123", responseMeta.GetProperty("trace_id").GetString());
    }
}
