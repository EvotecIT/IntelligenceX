using System.Linq;
using System.Text.Json;
using IntelligenceX.Tools.System;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class SystemPostureRenderHintsTests {
    [Fact]
    public void BuildBootRenderHints_WhenWarningsPresent_EmitsWarningTableHint() {
        var result = SystemRenderHintBuilders.BuildWarningListHints(warningCount: 2);

        Assert.NotNull(result);
        using var document = JsonDocument.Parse(result!.ToString()!);
        var renderHints = document.RootElement.EnumerateArray().ToArray();
        Assert.Single(renderHints);
        Assert.Equal("warnings", renderHints[0].GetProperty("rows_path").GetString());
        Assert.Equal(300, renderHints[0].GetProperty("priority").GetInt32());
    }

    [Fact]
    public void BuildBootRenderHints_WhenNoWarnings_ReturnsNull() {
        var result = SystemRenderHintBuilders.BuildWarningListHints(warningCount: 0);

        Assert.Null(result);
    }

    [Fact]
    public void BuildRdpRenderHints_WhenWarningsPresent_EmitsWarningTableHint() {
        var result = SystemRenderHintBuilders.BuildWarningListHints(warningCount: 1);

        Assert.NotNull(result);
        using var document = JsonDocument.Parse(result!.ToString()!);
        var renderHints = document.RootElement.EnumerateArray().ToArray();
        Assert.Single(renderHints);
        Assert.Equal("warnings", renderHints[0].GetProperty("rows_path").GetString());
        Assert.Equal(300, renderHints[0].GetProperty("priority").GetInt32());
    }

    [Fact]
    public void BuildRdpRenderHints_WhenNoWarnings_ReturnsNull() {
        var result = SystemRenderHintBuilders.BuildWarningListHints(warningCount: 0);

        Assert.Null(result);
    }

    [Fact]
    public void BuildSmbRenderHints_WhenSectionsPresent_EmitsPrioritizedHintOrder() {
        var result = SystemRenderHintBuilders.BuildSmbPostureHints(
            warningCount: 2,
            nullSessionShareCount: 1,
            nullSessionPipeCount: 1);

        Assert.NotNull(result);
        using var document = JsonDocument.Parse(result!.ToString()!);
        var renderHints = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(3, renderHints.Length);

        Assert.Equal("warnings", renderHints[0].GetProperty("rows_path").GetString());
        Assert.Equal(400, renderHints[0].GetProperty("priority").GetInt32());

        Assert.Equal("configuration/null_session_shares", renderHints[1].GetProperty("rows_path").GetString());
        Assert.Equal(300, renderHints[1].GetProperty("priority").GetInt32());

        Assert.Equal("configuration/null_session_pipes", renderHints[2].GetProperty("rows_path").GetString());
        Assert.Equal(200, renderHints[2].GetProperty("priority").GetInt32());
    }

    [Fact]
    public void BuildSmbRenderHints_WhenNoSections_ReturnsNull() {
        var result = SystemRenderHintBuilders.BuildSmbPostureHints(
            warningCount: 0,
            nullSessionShareCount: 0,
            nullSessionPipeCount: 0);

        Assert.Null(result);
    }
}
