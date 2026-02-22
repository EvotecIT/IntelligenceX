using System;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class JsonMapperNumericSafetyTests {
    [Fact]
    public void FromObject_WithLargeUnsignedLong_ShouldNotThrowOrOverflow() {
        var mapped = JsonMapper.FromObject(ulong.MaxValue);

        Assert.Equal(JsonValueKind.Number, mapped.Kind);
        var numeric = mapped.AsDouble();
        Assert.NotNull(numeric);
        Assert.True(double.IsFinite(numeric!.Value));
        Assert.True(numeric.Value > long.MaxValue);
    }

    [Fact]
    public void FromObject_WithUnsignedLongWithinInt64Range_ShouldPreserveIntegerSemantics() {
        var mapped = JsonMapper.FromObject((ulong)42);

        Assert.Equal(JsonValueKind.Number, mapped.Kind);
        Assert.Equal(42L, mapped.AsInt64());
    }

    [Fact]
    public void FromObject_WithNonFiniteDouble_ShouldMapToNull() {
        Assert.Equal(JsonValueKind.Null, JsonMapper.FromObject(double.NaN).Kind);
        Assert.Equal(JsonValueKind.Null, JsonMapper.FromObject(double.PositiveInfinity).Kind);
        Assert.Equal(JsonValueKind.Null, JsonMapper.FromObject(double.NegativeInfinity).Kind);
    }

    [Fact]
    public void JsonLiteSerialize_WithNonFiniteDoubles_ShouldEmitNullLiterals() {
        var json = JsonLite.Serialize(
            new JsonObject()
                .Add("nan", double.NaN)
                .Add("positive_infinity", double.PositiveInfinity)
                .Add("negative_infinity", double.NegativeInfinity));

        var parsed = JsonLite.Parse(json).AsObject();
        Assert.NotNull(parsed);
        Assert.Equal(JsonValueKind.Null, parsed!["nan"].Kind);
        Assert.Equal(JsonValueKind.Null, parsed["positive_infinity"].Kind);
        Assert.Equal(JsonValueKind.Null, parsed["negative_infinity"].Kind);
    }

    [Fact]
    public void ToolTableView_Apply_WithLargeUnsignedLongCell_ShouldSerializeRow() {
        var rows = new[] { new UnsignedRow(ulong.MaxValue) };
        var columns = new[] {
            new ToolTableColumnSpec<UnsignedRow>(
                column: new ToolColumn("counter", "Counter", "number"),
                selector: static row => row.Counter)
        };

        var result = ToolTableView.Apply(
            sourceRows: rows,
            request: new ToolTableViewRequest(),
            columnSpecs: columns,
            previewMaxRows: 1);

        Assert.Single(result.Rows);
        var first = result.Rows[0].AsObject();
        Assert.NotNull(first);

        var numeric = first!.GetDouble("counter");
        Assert.NotNull(numeric);
        Assert.True(double.IsFinite(numeric!.Value));
        Assert.True(numeric.Value > long.MaxValue);
    }

    [Fact]
    public void ToolTableView_Apply_WithNonFiniteDoubleCell_ShouldSerializeNull() {
        var rows = new[] { new FloatingPointRow(double.NaN) };
        var columns = new[] {
            new ToolTableColumnSpec<FloatingPointRow>(
                column: new ToolColumn("score", "Score", "number"),
                selector: static row => row.Score)
        };

        var result = ToolTableView.Apply(
            sourceRows: rows,
            request: new ToolTableViewRequest(),
            columnSpecs: columns,
            previewMaxRows: 1);

        Assert.Single(result.Rows);
        var first = result.Rows[0].AsObject();
        Assert.NotNull(first);
        Assert.True(first!.TryGetValue("score", out var mapped));
        Assert.NotNull(mapped);
        Assert.Equal(JsonValueKind.Null, mapped!.Kind);
    }

    private sealed record UnsignedRow(ulong Counter);
    private sealed record FloatingPointRow(double Score);
}
