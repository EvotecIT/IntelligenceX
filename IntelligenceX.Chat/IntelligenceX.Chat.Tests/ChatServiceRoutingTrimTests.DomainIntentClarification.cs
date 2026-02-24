using System;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void ShouldRequestDomainIntentClarification_ReturnsTrueForMixedNonDominantSubset() {
        var selectedTools = new[] {
            new ToolDefinition("ad_object_get", "stub"),
            new ToolDefinition("ad_replication_summary", "stub"),
            new ToolDefinition("dnsclientx_query", "stub"),
            new ToolDefinition("domaindetective_domain_summary", "stub")
        };

        var result = ShouldRequestDomainIntentClarificationMethod.Invoke(
            null,
            new object?[] { true, false, false, 4, 10, selectedTools });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldRequestDomainIntentClarification_DoesNotTriggerAtDominantShareBoundary() {
        var selectedTools = new[] {
            new ToolDefinition("ad_object_get", "stub"),
            new ToolDefinition("ad_replication_summary", "stub"),
            new ToolDefinition("ad_search", "stub"),
            new ToolDefinition("ad_domain_discover", "stub"),
            new ToolDefinition("dnsclientx_query", "stub")
        };

        var result = ShouldRequestDomainIntentClarificationMethod.Invoke(
            null,
            new object?[] { true, false, false, 5, 12, selectedTools });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldRequestDomainIntentClarification_ReturnsFalseWhenWeightedRoutingDisabled() {
        var selectedTools = new[] {
            new ToolDefinition("ad_object_get", "stub"),
            new ToolDefinition("dnsclientx_query", "stub"),
            new ToolDefinition("domaindetective_domain_summary", "stub")
        };

        var result = ShouldRequestDomainIntentClarificationMethod.Invoke(
            null,
            new object?[] { false, false, false, 3, 8, selectedTools });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldRequestDomainIntentClarification_ReturnsFalseWhenSelectionIsFullToolset() {
        var selectedTools = new[] {
            new ToolDefinition("ad_object_get", "stub"),
            new ToolDefinition("dnsclientx_query", "stub"),
            new ToolDefinition("domaindetective_domain_summary", "stub")
        };

        var result = ShouldRequestDomainIntentClarificationMethod.Invoke(
            null,
            new object?[] { true, false, false, 3, 3, selectedTools });

        Assert.False(Assert.IsType<bool>(result));
    }
}
