using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using IntelligenceX.Tools.System;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class SystemPostureRenderHintsTests {
    private static readonly MethodInfo BuildBootRenderHintsMethod =
        typeof(SystemBootConfigurationTool).GetMethod("BuildRenderHints", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("SystemBootConfigurationTool.BuildRenderHints not found.");

    private static readonly MethodInfo BuildRdpRenderHintsMethod =
        typeof(SystemRdpPostureTool).GetMethod("BuildRenderHints", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("SystemRdpPostureTool.BuildRenderHints not found.");

    private static readonly MethodInfo BuildSmbRenderHintsMethod =
        typeof(SystemSmbPostureTool).GetMethod("BuildRenderHints", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("SystemSmbPostureTool.BuildRenderHints not found.");

    [Fact]
    public void BuildBootRenderHints_WhenWarningsPresent_EmitsWarningTableHint() {
        var result = BuildBootRenderHintsMethod.Invoke(null, new object?[] { 2 });

        Assert.NotNull(result);
        using var document = JsonDocument.Parse(result!.ToString()!);
        var renderHints = document.RootElement.EnumerateArray().ToArray();
        Assert.Single(renderHints);
        Assert.Equal("warnings", renderHints[0].GetProperty("rows_path").GetString());
        Assert.Equal(300, renderHints[0].GetProperty("priority").GetInt32());
    }

    [Fact]
    public void BuildBootRenderHints_WhenNoWarnings_ReturnsNull() {
        var result = BuildBootRenderHintsMethod.Invoke(null, new object?[] { 0 });

        Assert.Null(result);
    }

    [Fact]
    public void BuildRdpRenderHints_WhenWarningsPresent_EmitsWarningTableHint() {
        var result = BuildRdpRenderHintsMethod.Invoke(null, new object?[] { 1 });

        Assert.NotNull(result);
        using var document = JsonDocument.Parse(result!.ToString()!);
        var renderHints = document.RootElement.EnumerateArray().ToArray();
        Assert.Single(renderHints);
        Assert.Equal("warnings", renderHints[0].GetProperty("rows_path").GetString());
        Assert.Equal(300, renderHints[0].GetProperty("priority").GetInt32());
    }

    [Fact]
    public void BuildRdpRenderHints_WhenNoWarnings_ReturnsNull() {
        var result = BuildRdpRenderHintsMethod.Invoke(null, new object?[] { 0 });

        Assert.Null(result);
    }

    [Fact]
    public void BuildSmbRenderHints_WhenSectionsPresent_EmitsPrioritizedHintOrder() {
        var result = BuildSmbRenderHintsMethod.Invoke(null, new object?[] { 2, 1, 1 });

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
        var result = BuildSmbRenderHintsMethod.Invoke(null, new object?[] { 0, 0, 0 });

        Assert.Null(result);
    }
}
