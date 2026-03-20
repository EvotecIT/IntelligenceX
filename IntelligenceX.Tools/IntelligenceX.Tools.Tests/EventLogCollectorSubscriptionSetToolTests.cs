using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EventViewerX;
using IntelligenceX.Json;
using IntelligenceX.Tools.EventLog;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class EventLogCollectorSubscriptionSetToolTests {
    [Fact]
    public async Task InvokeAsync_WithoutRequestedChanges_ShouldFailValidation() {
        var tool = new EventLogCollectorSubscriptionSetTool(new EventLogToolOptions());

        var json = await tool.InvokeAsync(
            new JsonObject()
                .Add("subscription_name", "ForwardedEvents")
                .Add("apply", false),
            CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", document.RootElement.GetProperty("error_code").GetString());
        Assert.Contains("Provide at least one requested change", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task InvokeAsync_InvalidSubscriptionXml_ShouldFailValidation() {
        var tool = new EventLogCollectorSubscriptionSetTool(new EventLogToolOptions());

        var json = await tool.InvokeAsync(
            new JsonObject()
                .Add("subscription_name", "ForwardedEvents")
                .Add("subscription_xml", "<Subscription>")
                .Add("apply", false),
            CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", document.RootElement.GetProperty("error_code").GetString());
        Assert.Contains("subscription_xml", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task InvokeAsync_DryRunKnownCollectorSubscription_ShouldReturnWritePreviewEnvelope() {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        SubscriptionInfo? knownSubscription;
        try {
            knownSubscription = SearchEvents.GetCollectorSubscriptions()
                .FirstOrDefault(static item => !string.IsNullOrWhiteSpace(item.Name));
        } catch {
            return;
        }

        if (knownSubscription is null) {
            return;
        }

        var tool = new EventLogCollectorSubscriptionSetTool(new EventLogToolOptions());

        var json = await tool.InvokeAsync(
            new JsonObject()
                .Add("subscription_name", knownSubscription.Name)
                .Add("is_enabled", !(knownSubscription.Enabled ?? false))
                .Add("apply", false),
            CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(knownSubscription.Name, root.GetProperty("subscription_name").GetString());
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
