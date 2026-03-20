using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.EventLog;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class EventLogCollectorSubscriptionsListToolTests {
    [Fact]
    public async Task InvokeAsync_ShouldReturnCollectorSubscriptionInventoryOrPlatformError() {
        var tool = new EventLogCollectorSubscriptionsListTool(new EventLogToolOptions());

        var json = await tool.InvokeAsync(
            new JsonObject()
                .Add("max_results", 5),
            CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!OperatingSystem.IsWindows()) {
            Assert.False(root.GetProperty("ok").GetBoolean());
            Assert.Equal("platform_not_supported", root.GetProperty("error_code").GetString());
            return;
        }

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.True(root.TryGetProperty("items", out var items));
        Assert.Equal(global::System.Text.Json.JsonValueKind.Array, items.ValueKind);
        Assert.True(root.TryGetProperty("items_view", out var itemsView));
        Assert.Equal(global::System.Text.Json.JsonValueKind.Array, itemsView.ValueKind);
        Assert.True(root.TryGetProperty("meta", out var meta));
        Assert.Equal(5, meta.GetProperty("max_results").GetInt32());
    }
}
