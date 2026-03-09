using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Monitoring.Probes;
using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class AdScopeDiscoveryToolTests {
    private static readonly MethodInfo BuildChainContractMethod =
        typeof(AdScopeDiscoveryTool).GetMethod("BuildChainContract", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildChainContract not found.");

    private static readonly Type ScopeDiscoveryRequestType =
        typeof(AdScopeDiscoveryTool).GetNestedType("ScopeDiscoveryRequest", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("ScopeDiscoveryRequest not found.");

    private static readonly Type ScopeDiscoveryGapType =
        typeof(AdScopeDiscoveryTool).GetNestedType("ScopeDiscoveryGap", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("ScopeDiscoveryGap not found.");

    private static readonly Type ScopeDiscoveryStepType =
        typeof(AdScopeDiscoveryTool).GetNestedType("ScopeDiscoveryStep", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("ScopeDiscoveryStep not found.");

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

    [Fact]
    public async Task InvokeAsync_WhenFallbackCurrentDomain_EmitsTypedArgumentsForProbeRunNextAction() {
        var tool = new AdScopeDiscoveryTool(new ActiveDirectoryToolOptions());

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
        var suggestedArguments = probeRunAction.Value.GetProperty("suggested_arguments");
        Assert.Equal(global::System.Text.Json.JsonValueKind.Object, suggestedArguments.ValueKind);
        Assert.True(suggestedArguments.TryGetProperty("forest_name", out _));
        var arguments = probeRunAction.Value.GetProperty("arguments");
        Assert.Equal(global::System.Text.Json.JsonValueKind.Object, arguments.ValueKind);
        Assert.True(arguments.TryGetProperty("forest_name", out _));
        Assert.True(arguments.TryGetProperty("include_domain_controllers", out var includeDomainControllers));
        Assert.Equal(global::System.Text.Json.JsonValueKind.Array, includeDomainControllers.ValueKind);
    }

    [Fact]
    public async Task InvokeAsync_WhenDomainControllerInventorySparse_EmitsForestExpansionNextAction() {
        var tool = new AdScopeDiscoveryTool(new ActiveDirectoryToolOptions());

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
            if (string.Equals(action.GetProperty("tool").GetString(), "ad_scope_discovery", StringComparison.OrdinalIgnoreCase)) {
                if (action.TryGetProperty("arguments", out _)) {
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
    public async Task InvokeAsync_WhenFallbackCurrentDomain_EmitsPrioritizedRenderHints() {
        var tool = new AdScopeDiscoveryTool(new ActiveDirectoryToolOptions());

        var json = await tool.InvokeAsync(
            arguments: new JsonObject()
                .Add("discovery_fallback", "current_domain"),
            cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        var renderHints = root.GetProperty("render").EnumerateArray().ToArray();
        Assert.True(renderHints.Length >= 2);

        Assert.Equal("receipt/steps", renderHints[0].GetProperty("rows_path").GetString());
        Assert.Equal(500, renderHints[0].GetProperty("priority").GetInt32());

        Assert.Equal("next_actions", renderHints[1].GetProperty("rows_path").GetString());
        Assert.Equal(400, renderHints[1].GetProperty("priority").GetInt32());
    }

    [Fact]
    public void BuildChainContract_WhenForestScopePreserved_BlanksDomainNameForChainedActions() {
        var contract = InvokeBuildChainContract(
            requestedForest: "ad.evotec.xyz",
            requestedDomain: "child.ad.evotec.xyz",
            effectiveForest: "ad.evotec.xyz",
            effectiveDomain: "child.ad.evotec.xyz",
            discoveryFallback: DirectoryDiscoveryFallback.CurrentDomain,
            includeTrusts: false,
            domains: new[] { "ad.evotec.xyz", "child.ad.evotec.xyz" });

        var forestDiscoverAction = FindAction(contract, "ad_forest_discover");
        Assert.Equal("ad.evotec.xyz", forestDiscoverAction.SuggestedArguments["forest_name"]);
        Assert.Equal(string.Empty, forestDiscoverAction.SuggestedArguments["domain_name"]);

        var probeRunAction = FindAction(contract, "ad_monitoring_probe_run");
        Assert.NotNull(probeRunAction.Arguments);
        Assert.Equal("ad.evotec.xyz", Assert.IsAssignableFrom<string>(probeRunAction.Arguments!["forest_name"]));
        Assert.Equal(string.Empty, Assert.IsAssignableFrom<string>(probeRunAction.Arguments["domain_name"]));
    }

    [Fact]
    public void BuildChainContract_WhenForestScopeUnknown_KeepsExplicitDomainForSparseExpansion() {
        var contract = InvokeBuildChainContract(
            requestedForest: null,
            requestedDomain: "child.ad.evotec.xyz",
            effectiveForest: null,
            effectiveDomain: "child.ad.evotec.xyz",
            discoveryFallback: DirectoryDiscoveryFallback.CurrentDomain,
            includeTrusts: false,
            domains: new[] { "child.ad.evotec.xyz" });

        var forestDiscoverAction = FindAction(contract, "ad_forest_discover");
        Assert.Equal(string.Empty, forestDiscoverAction.SuggestedArguments["forest_name"]);
        Assert.Equal("child.ad.evotec.xyz", forestDiscoverAction.SuggestedArguments["domain_name"]);

        var sparseExpansionAction = FindAction(contract, "ad_scope_discovery");
        Assert.NotNull(sparseExpansionAction.Arguments);
        Assert.Equal(string.Empty, Assert.IsAssignableFrom<string>(sparseExpansionAction.Arguments!["forest_name"]));
        Assert.Equal("child.ad.evotec.xyz", Assert.IsAssignableFrom<string>(sparseExpansionAction.Arguments["domain_name"]));
        Assert.Equal("current_domain", Assert.IsAssignableFrom<string>(sparseExpansionAction.Arguments["discovery_fallback"]));
    }

    private static ToolChainContractModel InvokeBuildChainContract(
        string? requestedForest,
        string? requestedDomain,
        string? effectiveForest,
        string? effectiveDomain,
        DirectoryDiscoveryFallback discoveryFallback,
        bool includeTrusts,
        IReadOnlyList<string> domains) {
        var request = Activator.CreateInstance(
                          ScopeDiscoveryRequestType,
                          requestedForest,
                          requestedDomain,
                          null,
                          Array.Empty<string>(),
                          Array.Empty<string>(),
                          Array.Empty<string>(),
                          Array.Empty<string>(),
                          false,
                          includeTrusts,
                          discoveryFallback,
                          250,
                          2000,
                          200,
                          5000,
                          10000,
                          5000)
                      ?? throw new InvalidOperationException("Could not create scope discovery request.");

        var gaps = Array.CreateInstance(ScopeDiscoveryGapType, 0);
        var steps = Array.CreateInstance(ScopeDiscoveryStepType, 0);
        var result = BuildChainContractMethod.Invoke(
            null,
            new object?[] {
                request,
                effectiveForest,
                effectiveDomain,
                domains,
                Array.Empty<string>(),
                gaps,
                steps
            });

        return Assert.IsType<ToolChainContractModel>(result);
    }

    private static ToolNextActionModel FindAction(ToolChainContractModel contract, string toolName) {
        return Assert.Single(
            contract.NextActions,
            action => string.Equals(action.Tool, toolName, StringComparison.OrdinalIgnoreCase));
    }
}
