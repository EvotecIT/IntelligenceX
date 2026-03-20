using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.EventLog;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class EventLogClassicLogRemoveToolTests {
    [Fact]
    public async Task InvokeAsync_RemoveLogWithoutRemoveSource_ShouldFailValidation() {
        var tool = new EventLogClassicLogRemoveTool(new EventLogToolOptions());

        var json = await tool.InvokeAsync(
            new JsonObject()
                .Add("log_name", "Application")
                .Add("source_name", "IxCodexPreviewSource")
                .Add("remove_log", true)
                .Add("apply", false),
            CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", document.RootElement.GetProperty("error_code").GetString());
        Assert.Contains("remove_log", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task InvokeAsync_DryRunReservedLogCleanup_ShouldReturnPreviewEnvelope() {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        var tool = new EventLogClassicLogRemoveTool(new EventLogToolOptions());
        var sourceName = "IxCodexPreviewSource-" + Guid.NewGuid().ToString("N");

        var json = await tool.InvokeAsync(
            new JsonObject()
                .Add("log_name", "Application")
                .Add("source_name", sourceName)
                .Add("remove_source", true)
                .Add("remove_log", true)
                .Add("apply", false),
            CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("Application", root.GetProperty("log_name").GetString());
        Assert.Equal(sourceName, root.GetProperty("source_name").GetString());
        Assert.False(root.GetProperty("meta").GetProperty("write_applied").GetBoolean());
        Assert.True(root.GetProperty("meta").GetProperty("write_candidate").GetBoolean());
        Assert.False(root.GetProperty("can_apply").GetBoolean());
        Assert.True(root.TryGetProperty("before", out var before));
        Assert.Equal(global::System.Text.Json.JsonValueKind.Object, before.ValueKind);
        Assert.True(root.TryGetProperty("after", out var after));
        Assert.Equal(global::System.Text.Json.JsonValueKind.Object, after.ValueKind);
        Assert.True(root.TryGetProperty("rollback_arguments", out var rollback));
        Assert.Equal(global::System.Text.Json.JsonValueKind.Object, rollback.ValueKind);
    }
}
