using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EventViewerX;
using IntelligenceX.Json;
using IntelligenceX.Tools.EventLog;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class EventLogChannelPolicySetToolTests {
    [Fact]
    public async Task InvokeAsync_WithoutRequestedChanges_ShouldFailValidation() {
        var tool = new EventLogChannelPolicySetTool(new EventLogToolOptions());

        var json = await tool.InvokeAsync(
            new JsonObject()
                .Add("log_name", "Application")
                .Add("apply", false),
            CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", document.RootElement.GetProperty("error_code").GetString());
        Assert.Contains("Provide at least one requested change", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task InvokeAsync_DryRunKnownChannel_ShouldReturnWritePreviewEnvelope() {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        var policy = SearchEvents.GetChannelPolicy("Application") ?? SearchEvents.GetChannelPolicy("System");
        if (policy is null) {
            return;
        }

        var tool = new EventLogChannelPolicySetTool(new EventLogToolOptions());

        var json = await tool.InvokeAsync(
            new JsonObject()
                .Add("log_name", policy.LogName)
                .Add("is_enabled", policy.IsEnabled ?? true)
                .Add("apply", false),
            CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(policy.LogName, root.GetProperty("log_name").GetString());
        Assert.False(root.GetProperty("meta").GetProperty("write_applied").GetBoolean());
        Assert.True(root.GetProperty("meta").GetProperty("write_candidate").GetBoolean());
        Assert.True(root.TryGetProperty("before", out var before));
        Assert.Equal(global::System.Text.Json.JsonValueKind.Object, before.ValueKind);
        Assert.True(root.TryGetProperty("after", out var after));
        Assert.Equal(global::System.Text.Json.JsonValueKind.Object, after.ValueKind);
        Assert.True(root.TryGetProperty("rollback_arguments", out var rollback));
        Assert.Equal(global::System.Text.Json.JsonValueKind.Object, rollback.ValueKind);
    }
}
