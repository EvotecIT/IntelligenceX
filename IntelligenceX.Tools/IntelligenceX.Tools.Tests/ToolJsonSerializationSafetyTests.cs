using System.Text.Json;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolJsonSerializationSafetyTests {
    [Fact]
    public void OkModel_WhenPayloadContainsMultidimensionalArray_ShouldNotThrow() {
        var grid = new bool[2, 2, 2];
        grid[1, 0, 1] = true;

        var json = ToolResponse.OkModel(new {
            Name = "grid",
            Raw = grid
        });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("grid", root.GetProperty("name").GetString());

        var raw = root.GetProperty("raw");
        Assert.Equal(JsonValueKind.Array, raw.ValueKind);
        Assert.Equal(8, raw.GetArrayLength());
    }

    [Fact]
    public void OkModel_WhenPayloadContainsCycle_ShouldEmitCycleMarker() {
        var node = new CycleNode { Name = "root" };
        node.Next = node;

        var json = ToolResponse.OkModel(node);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("root", root.GetProperty("name").GetString());
        Assert.Equal("[cycle]", root.GetProperty("next").GetString());
    }

    private sealed class CycleNode {
        public string Name { get; set; } = string.Empty;

        public CycleNode? Next { get; set; }
    }
}
