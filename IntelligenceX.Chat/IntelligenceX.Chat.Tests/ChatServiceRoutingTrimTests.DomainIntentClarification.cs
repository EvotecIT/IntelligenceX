using System;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void ShouldRequestDomainIntentClarification_ReturnsTrueForMixedNonDominantSubset() {
        var selectedTools = new[] {
            new ToolDefinition("ad_object_get", "stub", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("ad_replication_summary", "stub", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("dnsclientx_query", "stub", tags: new[] { "domain_family:public_domain" }),
            new ToolDefinition("domaindetective_domain_summary", "stub", tags: new[] { "domain_family:public_domain" })
        };

        var result = ShouldRequestDomainIntentClarificationMethod.Invoke(
            null,
            new object?[] { true, false, false, "check evotec.xyz", 4, 10, selectedTools, selectedTools });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldRequestDomainIntentClarification_ReturnsFalseForFreshGreetingEvenWithMixedSubset() {
        var selectedTools = new[] {
            new ToolDefinition("ad_object_get", "stub", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("ad_replication_summary", "stub", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("dnsclientx_query", "stub", tags: new[] { "domain_family:public_domain" }),
            new ToolDefinition("domaindetective_domain_summary", "stub", tags: new[] { "domain_family:public_domain" })
        };

        var result = ShouldRequestDomainIntentClarificationMethod.Invoke(
            null,
            new object?[] { true, false, false, "Hello mr", 4, 10, selectedTools, selectedTools });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldRequestDomainIntentClarification_DoesNotTriggerAtDominantShareBoundary() {
        var selectedTools = new[] {
            new ToolDefinition("ad_object_get", "stub", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("ad_replication_summary", "stub", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("ad_search", "stub", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("ad_domain_discover", "stub", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("dnsclientx_query", "stub", tags: new[] { "domain_family:public_domain" })
        };

        var result = ShouldRequestDomainIntentClarificationMethod.Invoke(
            null,
            new object?[] { true, false, false, "check evotec.xyz", 5, 12, selectedTools, selectedTools });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldRequestDomainIntentClarification_ReturnsFalseWhenWeightedRoutingDisabled() {
        var selectedTools = new[] {
            new ToolDefinition("ad_object_get", "stub", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("dnsclientx_query", "stub", tags: new[] { "domain_family:public_domain" }),
            new ToolDefinition("domaindetective_domain_summary", "stub", tags: new[] { "domain_family:public_domain" })
        };

        var result = ShouldRequestDomainIntentClarificationMethod.Invoke(
            null,
            new object?[] { false, false, false, "check evotec.xyz", 3, 8, selectedTools, selectedTools });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldRequestDomainIntentClarification_ReturnsFalseWhenSelectionIsFullToolset() {
        var selectedTools = new[] {
            new ToolDefinition("ad_object_get", "stub", tags: new[] { "domain_family:ad_domain" }),
            new ToolDefinition("dnsclientx_query", "stub", tags: new[] { "domain_family:public_domain" }),
            new ToolDefinition("domaindetective_domain_summary", "stub", tags: new[] { "domain_family:public_domain" })
        };

        var result = ShouldRequestDomainIntentClarificationMethod.Invoke(
            null,
            new object?[] { true, false, false, "check evotec.xyz", 3, 3, selectedTools, selectedTools });

        Assert.False(Assert.IsType<bool>(result));
    }
}
