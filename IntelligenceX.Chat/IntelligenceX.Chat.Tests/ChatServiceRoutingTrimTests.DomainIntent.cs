using System;
using System.Collections.Generic;
using System.Reflection;
using IntelligenceX.Chat.Service;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    private static readonly MethodInfo ShouldRequestDomainIntentClarificationMethod =
        typeof(ChatServiceSession).GetMethod("ShouldRequestDomainIntentClarification", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ShouldRequestDomainIntentClarification not found.");

    private static readonly MethodInfo BuildDomainIntentClarificationTextMethod =
        typeof(ChatServiceSession).GetMethod("BuildDomainIntentClarificationText", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildDomainIntentClarificationText not found.");

    [Fact]
    public void ShouldRequestDomainIntentClarification_TrueForMixedAdAndPublicDomainSubset() {
        var selectedTools = new List<ToolDefinition> {
            new("ad_scope_discovery", description: "AD scope"),
            new("ad_domain_controllers", description: "AD DCs"),
            new("dnsclientx_query", description: "DNS query"),
            new("domaindetective_domain_summary", description: "Domain summary"),
            new("eventlog_live_query", description: "Event log")
        };

        var result = ShouldRequestDomainIntentClarificationMethod.Invoke(
            null,
            new object?[] {
                true,
                false,
                false,
                selectedTools.Count,
                24,
                selectedTools
            });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldRequestDomainIntentClarification_FalseWhenOneFamilyDominates() {
        var selectedTools = new List<ToolDefinition> {
            new("ad_scope_discovery", description: "AD scope"),
            new("ad_domain_controllers", description: "AD DCs"),
            new("ad_forest_discover", description: "AD forest"),
            new("ad_ldap_query", description: "AD LDAP"),
            new("ad_directory_discovery_diagnostics", description: "AD diagnostics"),
            new("dnsclientx_query", description: "DNS query")
        };

        var result = ShouldRequestDomainIntentClarificationMethod.Invoke(
            null,
            new object?[] {
                true,
                false,
                false,
                selectedTools.Count,
                30,
                selectedTools
            });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldRequestDomainIntentClarification_FalseWhenExecutionContractApplies() {
        var selectedTools = new List<ToolDefinition> {
            new("ad_scope_discovery", description: "AD scope"),
            new("dnsclientx_query", description: "DNS query"),
            new("domaindetective_domain_summary", description: "Domain summary")
        };

        var result = ShouldRequestDomainIntentClarificationMethod.Invoke(
            null,
            new object?[] {
                true,
                true,
                false,
                selectedTools.Count,
                20,
                selectedTools
            });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldRequestDomainIntentClarification_FalseWhenFullToolsetIsSelected() {
        var selectedTools = new List<ToolDefinition> {
            new("ad_scope_discovery", description: "AD scope"),
            new("dnsclientx_query", description: "DNS query"),
            new("domaindetective_domain_summary", description: "Domain summary")
        };

        var result = ShouldRequestDomainIntentClarificationMethod.Invoke(
            null,
            new object?[] {
                true,
                false,
                false,
                selectedTools.Count,
                selectedTools.Count,
                selectedTools
            });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldRequestDomainIntentClarification_FalseWhenSelectedToolsIsNull() {
        var result = ShouldRequestDomainIntentClarificationMethod.Invoke(
            null,
            new object?[] {
                true,
                false,
                false,
                3,
                20,
                null
            });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void BuildDomainIntentClarificationText_IncludesBothScopesAndChoicePrompt() {
        var text = BuildDomainIntentClarificationTextMethod.Invoke(null, Array.Empty<object?>());
        var clarification = Assert.IsType<string>(text);

        Assert.Contains("Active Directory domain", clarification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Public DNS/domain", clarification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Reply with `1` or `2`", clarification, StringComparison.OrdinalIgnoreCase);
    }
}
