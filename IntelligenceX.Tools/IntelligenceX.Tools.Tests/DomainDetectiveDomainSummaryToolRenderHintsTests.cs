using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using IntelligenceX.Json;
using IntelligenceX.Tools.DomainDetective;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class DomainDetectiveDomainSummaryToolRenderHintsTests {
    private static readonly MethodInfo BuildRenderHintsMethod =
        typeof(DomainDetectiveDomainSummaryTool).GetMethod(
            "BuildRenderHints",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildRenderHints method was not found.");

    [Fact]
    public void BuildRenderHints_ReturnsNullWhenAllSectionsAreEmpty() {
        var hints = InvokeBuildRenderHints(
            analysisOverviewCount: 0,
            checksRequestedCount: 0,
            hintCount: 0);

        Assert.Null(hints);
    }

    [Fact]
    public void BuildRenderHints_EmitsPriorityOrderedSectionTables() {
        var hints = InvokeBuildRenderHints(
            analysisOverviewCount: 2,
            checksRequestedCount: 8,
            hintCount: 3);
        Assert.NotNull(hints);

        using var document = JsonDocument.Parse(JsonLite.Serialize(hints!));
        var renderHints = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(3, renderHints.Length);

        Assert.Equal("analysis_overview", renderHints[0].GetProperty("rows_path").GetString());
        Assert.Equal(400, renderHints[0].GetProperty("priority").GetInt32());

        Assert.Equal("checks_requested", renderHints[1].GetProperty("rows_path").GetString());
        Assert.Equal(300, renderHints[1].GetProperty("priority").GetInt32());

        Assert.Equal("summary/hints", renderHints[2].GetProperty("rows_path").GetString());
        Assert.Equal(200, renderHints[2].GetProperty("priority").GetInt32());
    }

    private static JsonValue? InvokeBuildRenderHints(
        int analysisOverviewCount,
        int checksRequestedCount,
        int hintCount) {
        var value = BuildRenderHintsMethod.Invoke(
            null,
            new object?[] { analysisOverviewCount, checksRequestedCount, hintCount });
        return value as JsonValue;
    }
}
