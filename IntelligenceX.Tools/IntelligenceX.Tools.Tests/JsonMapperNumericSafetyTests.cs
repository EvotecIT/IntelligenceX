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

    private sealed record UnsignedRow(ulong Counter);
}
