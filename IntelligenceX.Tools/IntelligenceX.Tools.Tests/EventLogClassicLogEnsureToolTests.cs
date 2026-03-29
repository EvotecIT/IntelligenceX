using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.EventLog;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class EventLogClassicLogEnsureToolTests {
    [Fact]
    public void ResolveOverflowActionName_FallsBackToOverwriteAsNeeded_WhenRequestAndSnapshotAreMissing() {
        var effectiveOverflowAction = EventLogClassicLogEnsureTool.ResolveOverflowActionName(
            requestedOverflowAction: null,
            currentOverflowAction: null);

        Assert.Equal("overwrite_as_needed", effectiveOverflowAction);
    }

    [Fact]
    public async Task InvokeAsync_RetentionDaysWithoutOverwriteOlder_ShouldFailValidation() {
        var tool = new EventLogClassicLogEnsureTool(new EventLogToolOptions());

        var json = await tool.InvokeAsync(
            new JsonObject()
                .Add("log_name", "Application")
                .Add("retention_days", 30)
                .Add("apply", false),
            CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", document.RootElement.GetProperty("error_code").GetString());
        Assert.Contains("retention_days", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task InvokeAsync_DryRunKnownLogWithNewSource_ShouldReturnWritePreviewEnvelope() {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        var tool = new EventLogClassicLogEnsureTool(new EventLogToolOptions());
        var sourceName = "IxCodexPreviewSource-" + Guid.NewGuid().ToString("N");

        var json = await tool.InvokeAsync(
            new JsonObject()
                .Add("log_name", "Application")
                .Add("source_name", sourceName)
                .Add("overflow_action", "overwrite_as_needed")
                .Add("apply", false),
            CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("Application", root.GetProperty("log_name").GetString());
        Assert.Equal(sourceName, root.GetProperty("source_name").GetString());
        Assert.False(root.GetProperty("meta").GetProperty("write_applied").GetBoolean());
        Assert.True(root.GetProperty("meta").GetProperty("write_candidate").GetBoolean());
        Assert.True(root.TryGetProperty("before", out var before));
        Assert.Equal(global::System.Text.Json.JsonValueKind.Object, before.ValueKind);
        Assert.True(root.TryGetProperty("after", out var after));
        Assert.Equal(global::System.Text.Json.JsonValueKind.Object, after.ValueKind);
        Assert.True(root.TryGetProperty("rollback_guidance", out var rollback));
        Assert.Equal(global::System.Text.Json.JsonValueKind.Object, rollback.ValueKind);
    }
}
