using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.System;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class SystemServiceLifecycleToolTests {
    [Fact]
    public async Task InvokeAsync_SetStartupTypeWithoutStartupType_ShouldFailValidation() {
        var tool = new SystemServiceLifecycleTool(new SystemToolOptions());

        var json = await tool.InvokeAsync(
            new JsonObject()
                .Add("service_name", "EventLog")
                .Add("operation", "set_startup_type")
                .Add("apply", false),
            CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", document.RootElement.GetProperty("error_code").GetString());
        Assert.Contains("startup_type is required", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task InvokeAsync_DryRunKnownService_ShouldReturnWritePreviewEnvelope() {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        var tool = new SystemServiceLifecycleTool(new SystemToolOptions());

        var json = await tool.InvokeAsync(
            new JsonObject()
                .Add("service_name", "EventLog")
                .Add("operation", "restart")
                .Add("apply", false),
            CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("restart", root.GetProperty("operation").GetString());
        Assert.Equal("EventLog", root.GetProperty("service_name").GetString());
        Assert.False(root.GetProperty("meta").GetProperty("write_applied").GetBoolean());
        Assert.True(root.GetProperty("meta").GetProperty("write_candidate").GetBoolean());
        Assert.True(root.TryGetProperty("before", out var before));
        Assert.Equal(global::System.Text.Json.JsonValueKind.Object, before.ValueKind);
        Assert.True(root.TryGetProperty("after", out var after));
        Assert.Equal(global::System.Text.Json.JsonValueKind.Object, after.ValueKind);
    }
}
