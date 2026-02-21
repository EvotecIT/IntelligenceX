using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class AdScopeDiscoveryToolTests {
    [Fact]
    public async Task InvokeAsync_WhenDiscoveryFallbackMissing_ReturnsInvalidArgument() {
        var tool = new AdScopeDiscoveryTool(new ActiveDirectoryToolOptions());

        var json = await tool.InvokeAsync(
            arguments: new JsonObject(),
            cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Contains("discovery_fallback", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_WhenDiscoveryFallbackInvalid_ReturnsInvalidArgument() {
        var tool = new AdScopeDiscoveryTool(new ActiveDirectoryToolOptions());

        var json = await tool.InvokeAsync(
            arguments: new JsonObject()
                .Add("discovery_fallback", "unsupported_mode"),
            cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Contains("discovery_fallback", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_WhenScopeMissingAndFallbackNone_ReturnsInvalidArgument() {
        var tool = new AdScopeDiscoveryTool(new ActiveDirectoryToolOptions());

        var json = await tool.InvokeAsync(
            arguments: new JsonObject()
                .Add("discovery_fallback", "none"),
            cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Contains("scope", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_WhenFallbackCurrentDomain_EmitsChainingContractFields() {
        var tool = new AdScopeDiscoveryTool(new ActiveDirectoryToolOptions());

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
        Assert.True(root.TryGetProperty("flow_id", out var flowId));
        Assert.True(root.TryGetProperty("step_id", out var stepId));
        Assert.True(root.TryGetProperty("checkpoint", out var checkpoint));
        Assert.True(root.TryGetProperty("handoff", out var handoff));
        Assert.True(root.TryGetProperty("confidence", out var confidence));
        Assert.True(nextActions.ValueKind == global::System.Text.Json.JsonValueKind.Array);
        Assert.Equal("ad_forest_discover", nextActions[0].GetProperty("tool").GetString());
        Assert.Equal("ad_scope_discovery_handoff", handoff.GetProperty("contract").GetString());
        Assert.False(string.IsNullOrWhiteSpace(cursor.GetString()));
        Assert.False(string.IsNullOrWhiteSpace(resumeToken.GetString()));
        Assert.False(string.IsNullOrWhiteSpace(flowId.GetString()));
        Assert.Equal("scope_receipt", stepId.GetString());
        Assert.True(checkpoint.TryGetProperty("domains", out _));
        Assert.InRange(confidence.GetDouble(), 0d, 1d);
    }
}
