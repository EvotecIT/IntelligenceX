using System;
using System.Collections.Generic;
using System.Reflection;
using IntelligenceX.Tools.ADPlayground;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdEnvironmentDiscoverToolTests {
    private static readonly MethodInfo BuildReadOnlyEnvironmentNextActionsMethod =
        typeof(AdEnvironmentDiscoverTool).GetMethod("BuildReadOnlyEnvironmentNextActions", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildReadOnlyEnvironmentNextActions not found.");
    private static readonly MethodInfo TryExtractDomainNameFromDistinguishedNameMethod =
        typeof(AdEnvironmentDiscoverTool).GetMethod("TryExtractDomainNameFromDistinguishedName", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("TryExtractDomainNameFromDistinguishedName not found.");

    [Fact]
    public void BuildReadOnlyEnvironmentNextActions_WhenNotLimited_ReturnsEmpty() {
        var result = BuildReadOnlyEnvironmentNextActionsMethod.Invoke(
            null,
            new object?[] { false, "AD0", "contoso.com", "contoso.com", true, false });

        var actions = Assert.IsAssignableFrom<IReadOnlyList<ToolNextActionModel>>(result);
        Assert.Empty(actions);
    }

    [Fact]
    public void BuildReadOnlyEnvironmentNextActions_WhenLimited_AddsRecoveryAndDiagnosticsActions() {
        var result = BuildReadOnlyEnvironmentNextActionsMethod.Invoke(
            null,
            new object?[] { true, "AD0", "contoso.com", "contoso.com", false, false });

        var actions = Assert.IsAssignableFrom<IReadOnlyList<ToolNextActionModel>>(result);
        Assert.True(actions.Count >= 4);
        Assert.Contains(actions, static action => string.Equals(action.Tool, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(actions, static action => string.Equals(action.Tool, "ad_forest_discover", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(actions, static action => string.Equals(action.Tool, "ad_domain_controllers", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(actions, static action => string.Equals(action.Tool, "ad_directory_discovery_diagnostics", StringComparison.OrdinalIgnoreCase));
        Assert.All(actions, static action => Assert.False(action.Mutating ?? true));

        var scopeAction = Assert.Single(actions, static action => string.Equals(action.Tool, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("current_forest", scopeAction.SuggestedArguments["discovery_fallback"]);
        Assert.Equal("True", scopeAction.SuggestedArguments["include_forest_domains"]);
        Assert.Equal("True", scopeAction.SuggestedArguments["include_trusts"]);
        Assert.Equal("5000", scopeAction.SuggestedArguments["max_domain_controllers_total"]);
        Assert.Equal("500", scopeAction.SuggestedArguments["max_domain_controllers_per_domain"]);

        var diagnosticsAction = Assert.Single(actions, static action => string.Equals(action.Tool, "ad_directory_discovery_diagnostics", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("2000", diagnosticsAction.SuggestedArguments["max_issues"]);
    }

    [Fact]
    public void TryExtractDomainNameFromDistinguishedName_WhenDnContainsDcLabels_ReturnsDomain() {
        var args = new object?[] { "CN=User,OU=Ops,DC=ad,DC=evotec,DC=xyz", string.Empty };
        var result = TryExtractDomainNameFromDistinguishedNameMethod.Invoke(null, args);

        Assert.True(result is bool value && value);
        Assert.Equal("ad.evotec.xyz", args[1] as string);
    }

    [Fact]
    public void TryExtractDomainNameFromDistinguishedName_WhenDnHasNoDcLabels_ReturnsFalse() {
        var args = new object?[] { "CN=User,OU=Ops", string.Empty };
        var result = TryExtractDomainNameFromDistinguishedNameMethod.Invoke(null, args);

        Assert.True(result is bool value && !value);
        Assert.Equal(string.Empty, args[1] as string);
    }
}
