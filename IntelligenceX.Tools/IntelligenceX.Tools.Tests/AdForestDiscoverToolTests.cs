using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdForestDiscoverToolTests {
    private static readonly MethodInfo BuildRenderHintsMethod =
        typeof(AdForestDiscoverTool).GetMethod("BuildRenderHints", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildRenderHints not found.");

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
        Assert.True(root.TryGetProperty("flow_id", out var flowId));
        Assert.True(root.TryGetProperty("step_id", out var stepId));
        Assert.True(root.TryGetProperty("checkpoint", out var checkpoint));
        Assert.True(root.TryGetProperty("handoff", out var handoff));
        Assert.True(root.TryGetProperty("confidence", out var confidence));
        Assert.True(nextActions.ValueKind == global::System.Text.Json.JsonValueKind.Array);
        Assert.Equal("ad_scope_discovery", nextActions[0].GetProperty("tool").GetString());
        Assert.Equal("ad_forest_discover_handoff", handoff.GetProperty("contract").GetString());
        Assert.False(string.IsNullOrWhiteSpace(cursor.GetString()));
        Assert.False(string.IsNullOrWhiteSpace(resumeToken.GetString()));
        Assert.False(string.IsNullOrWhiteSpace(flowId.GetString()));
        Assert.Equal("forest_receipt", stepId.GetString());
        Assert.True(checkpoint.TryGetProperty("domains", out _));
        Assert.InRange(confidence.GetDouble(), 0d, 1d);
    }

    [Fact]
    public async Task InvokeAsync_WhenFallbackCurrentDomain_EmitsTypedArgumentsForProbeRunNextAction() {
        var tool = new AdForestDiscoverTool(new ActiveDirectoryToolOptions());

        var json = await tool.InvokeAsync(
            arguments: new JsonObject()
                .Add("discovery_fallback", "current_domain"),
            cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        var nextActions = root.GetProperty("next_actions");
        JsonElement? probeRunAction = null;
        foreach (var action in nextActions.EnumerateArray()) {
            if (string.Equals(action.GetProperty("tool").GetString(), "ad_monitoring_probe_run", StringComparison.OrdinalIgnoreCase)) {
                probeRunAction = action;
                break;
            }
        }

        Assert.True(probeRunAction.HasValue);
        var arguments = probeRunAction.Value.GetProperty("arguments");
        Assert.Equal(global::System.Text.Json.JsonValueKind.Object, arguments.ValueKind);
        Assert.True(arguments.TryGetProperty("include_domain_controllers", out var includeDomainControllers));
        Assert.Equal(global::System.Text.Json.JsonValueKind.Array, includeDomainControllers.ValueKind);
    }

    [Fact]
    public async Task InvokeAsync_WhenDomainControllerInventorySparse_EmitsForestFallbackExpansionNextAction() {
        var tool = new AdForestDiscoverTool(new ActiveDirectoryToolOptions());

        var json = await tool.InvokeAsync(
            arguments: new JsonObject()
                .Add("discovery_fallback", "current_domain")
                .Add("include_domain_controllers", new JsonArray().Add("___unlikely_dc_name___")),
            cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        var nextActions = root.GetProperty("next_actions");
        JsonElement? expansionAction = null;
        foreach (var action in nextActions.EnumerateArray()) {
            if (string.Equals(action.GetProperty("tool").GetString(), "ad_forest_discover", StringComparison.OrdinalIgnoreCase)) {
                if (action.TryGetProperty("arguments", out var arguments)
                    && arguments.TryGetProperty("discovery_fallback", out var fallback)
                    && string.Equals(fallback.GetString(), "current_forest", StringComparison.OrdinalIgnoreCase)) {
                    expansionAction = action;
                    break;
                }
            }
        }

        Assert.True(expansionAction.HasValue);
        var typedArguments = expansionAction.Value.GetProperty("arguments");
        Assert.True(typedArguments.TryGetProperty("include_trusts", out var includeTrusts));
        Assert.True(includeTrusts.GetBoolean());
    }

    [Fact]
    public void BuildRenderHints_WhenSectionsExist_EmitsPrioritizedHints() {
        var result = BuildRenderHintsMethod.Invoke(
            null,
            new object?[] { 2, 5, 3, 1, 4, 2 });

        Assert.NotNull(result);
        using var doc = JsonDocument.Parse(result!.ToString()!);
        var renderHints = doc.RootElement.EnumerateArray().ToArray();
        Assert.Equal(6, renderHints.Length);

        Assert.Equal("domain_controllers_by_domain", renderHints[0].GetProperty("rows_path").GetString());
        Assert.Equal(500, renderHints[0].GetProperty("priority").GetInt32());

        Assert.Equal("domain_controllers", renderHints[1].GetProperty("rows_path").GetString());
        Assert.Equal(450, renderHints[1].GetProperty("priority").GetInt32());

        Assert.Equal("domains", renderHints[2].GetProperty("rows_path").GetString());
        Assert.Equal(400, renderHints[2].GetProperty("priority").GetInt32());

        Assert.Equal("trusts", renderHints[3].GetProperty("rows_path").GetString());
        Assert.Equal(300, renderHints[3].GetProperty("priority").GetInt32());

        Assert.Equal("receipt/steps", renderHints[4].GetProperty("rows_path").GetString());
        Assert.Equal(200, renderHints[4].GetProperty("priority").GetInt32());

        Assert.Equal("next_actions", renderHints[5].GetProperty("rows_path").GetString());
        Assert.Equal(150, renderHints[5].GetProperty("priority").GetInt32());
    }

    [Fact]
    public void BuildRenderHints_WhenNoSectionsExist_ReturnsNull() {
        var result = BuildRenderHintsMethod.Invoke(
            null,
            new object?[] { 0, 0, 0, 0, 0, 0 });

        Assert.Null(result);
    }
}
