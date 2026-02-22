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
        Assert.Equal(JsonValueKind.Object, raw.ValueKind);
        Assert.Equal(3, raw.GetProperty("rank").GetInt32());

        var lengths = raw.GetProperty("lengths");
        Assert.Equal(JsonValueKind.Array, lengths.ValueKind);
        Assert.Equal(3, lengths.GetArrayLength());
        Assert.Equal(2, lengths[0].GetInt32());
        Assert.Equal(2, lengths[1].GetInt32());
        Assert.Equal(2, lengths[2].GetInt32());

        var lowerBounds = raw.GetProperty("lower_bounds");
        Assert.Equal(JsonValueKind.Array, lowerBounds.ValueKind);
        Assert.Equal(3, lowerBounds.GetArrayLength());
        Assert.Equal(0, lowerBounds[0].GetInt32());
        Assert.Equal(0, lowerBounds[1].GetInt32());
        Assert.Equal(0, lowerBounds[2].GetInt32());

        var values = raw.GetProperty("values");
        Assert.Equal(JsonValueKind.Array, values.ValueKind);
        Assert.Equal(2, values.GetArrayLength());
        Assert.True(values[1][0][1].GetBoolean());
    }

    [Fact]
    public void OkModel_WhenPayloadContainsNonZeroLowerBoundMultidimensionalArray_ShouldPreserveBounds() {
        var grid = Array.CreateInstance(typeof(int), lengths: [2, 2], lowerBounds: [1, -2]);
        grid.SetValue(21, [1, -2]);
        grid.SetValue(42, [2, -1]);

        var json = ToolResponse.OkModel(new {
            Name = "grid",
            Raw = grid
        });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var raw = root.GetProperty("raw");

        Assert.Equal(JsonValueKind.Object, raw.ValueKind);
        Assert.Equal(2, raw.GetProperty("rank").GetInt32());

        var lengths = raw.GetProperty("lengths");
        Assert.Equal(2, lengths.GetArrayLength());
        Assert.Equal(2, lengths[0].GetInt32());
        Assert.Equal(2, lengths[1].GetInt32());

        var lowerBounds = raw.GetProperty("lower_bounds");
        Assert.Equal(2, lowerBounds.GetArrayLength());
        Assert.Equal(1, lowerBounds[0].GetInt32());
        Assert.Equal(-2, lowerBounds[1].GetInt32());

        var values = raw.GetProperty("values");
        Assert.Equal(2, values.GetArrayLength());
        Assert.Equal(21, values[0][0].GetInt32());
        Assert.Equal(42, values[1][1].GetInt32());
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
