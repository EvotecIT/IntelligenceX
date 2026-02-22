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

    [Fact]
    public void OkModel_WhenPayloadContainsNonFiniteNumbers_ShouldEmitNulls() {
        var json = ToolResponse.OkModel(new {
            Name = "non-finite",
            Score = double.NaN,
            Ratio = double.PositiveInfinity,
            Delta = float.NegativeInfinity
        });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("non-finite", root.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("score").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("ratio").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("delta").ValueKind);
    }

    [Fact]
    public void OkModel_WhenFallbackPayloadContainsNonFiniteNumbers_ShouldEmitNulls() {
        var node = new CycleNode {
            Name = "root",
            Score = double.NaN
        };
        node.Next = node;

        var json = ToolResponse.OkModel(node);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("root", root.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("score").ValueKind);
        Assert.Equal("[cycle]", root.GetProperty("next").GetString());
    }

    [Fact]
    public void JsonMapper_ShouldNormalizeDateTimeOffsetAndEnumConsistentlyWithToolJson() {
        var model = new ContractModel {
            StartedAt = new DateTimeOffset(2026, 2, 22, 12, 34, 56, TimeSpan.FromHours(2)),
            Stage = ContractStage.Ready
        };

        var mappedDate = global::IntelligenceX.Json.JsonMapper.FromObject(model.StartedAt);
        var mappedStage = global::IntelligenceX.Json.JsonMapper.FromObject(model.Stage);
        var root = ToolJson.ToJsonObjectSnakeCase(model);

        Assert.Equal("2026-02-22T10:34:56.0000000+00:00", mappedDate.AsString());
        Assert.Equal("Ready", mappedStage.AsString());
        Assert.Equal(mappedDate.AsString(), root.GetString("started_at"));
        Assert.Equal(mappedStage.AsString(), root.GetString("stage"));
    }

    [Fact]
    public void JsonMapper_ShouldNormalizeUtcDateTimeToRoundTripFriendlyIsoString() {
        var utc = new DateTime(2026, 2, 22, 1, 2, 3, DateTimeKind.Utc);
        var mapped = global::IntelligenceX.Json.JsonMapper.FromObject(utc);

        Assert.Equal(global::IntelligenceX.Json.JsonValueKind.String, mapped.Kind);
        Assert.Equal("2026-02-22T01:02:03.0000000Z", mapped.AsString());
    }

    [Fact]
    public void JsonMapper_ShouldNormalizeTimeSpanUsingInvariantConstantFormat() {
        var span = new TimeSpan(days: 1, hours: 2, minutes: 3, seconds: 4, milliseconds: 5);
        var mapped = global::IntelligenceX.Json.JsonMapper.FromObject(span);
        var root = ToolJson.ToJsonObjectSnakeCase(new {
            Elapsed = span
        });

        Assert.Equal("1.02:03:04.0050000", mapped.AsString());
        Assert.Equal(mapped.AsString(), root.GetString("elapsed"));
    }

    [Fact]
    public void OkModel_WhenFallbackHandlesCycle_ShouldPreserveDeepChildContext() {
        var rootNode = new CycleNode { Name = "root" };
        var current = rootNode;
        for (var index = 0; index < 12; index++) {
            var next = new CycleNode { Name = $"level-{index:00}" };
            current.Child = next;
            current = next;
        }
        rootNode.Next = rootNode;

        var json = ToolResponse.OkModel(rootNode);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(
            "level-09",
            root.GetProperty("child")
                .GetProperty("child")
                .GetProperty("child")
                .GetProperty("child")
                .GetProperty("child")
                .GetProperty("child")
                .GetProperty("child")
                .GetProperty("child")
                .GetProperty("child")
                .GetProperty("child")
                .GetProperty("name")
                .GetString());
        Assert.Equal("[cycle]", root.GetProperty("next").GetString());
    }

    private sealed class CycleNode {
        public string Name { get; set; } = string.Empty;
        public double Score { get; set; }

        public CycleNode? Child { get; set; }

        public CycleNode? Next { get; set; }
    }

    private enum ContractStage {
        Ready,
        Running
    }

    private sealed class ContractModel {
        public DateTimeOffset StartedAt { get; set; }

        public ContractStage Stage { get; set; }
    }
}
