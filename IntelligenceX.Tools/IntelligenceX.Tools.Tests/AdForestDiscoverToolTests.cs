using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdForestDiscoverToolTests {
    [Fact]
    public async Task InvokeAsync_WhenFallbackCurrentDomain_EmitsChainingContractFields() {
        var tool = new AdForestDiscoverTool(new ActiveDirectoryToolOptions());

        var json = await tool.InvokeAsync(
            arguments: new JsonObject()
                .Add("discovery_fallback", "current_domain"),
            cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.True(root.TryGetProperty("next_actions", out var nextActions));
        Assert.True(root.TryGetProperty("cursor", out var cursor));
        Assert.True(root.TryGetProperty("resume_token", out var resumeToken));
        Assert.True(root.TryGetProperty("handoff", out var handoff));
        Assert.True(root.TryGetProperty("confidence", out var confidence));
        Assert.True(nextActions.ValueKind == global::System.Text.Json.JsonValueKind.Array);
        Assert.Equal("ad_forest_discover_handoff", handoff.GetProperty("contract").GetString());
        Assert.False(string.IsNullOrWhiteSpace(cursor.GetString()));
        Assert.False(string.IsNullOrWhiteSpace(resumeToken.GetString()));
        Assert.InRange(confidence.GetDouble(), 0d, 1d);
    }
}
