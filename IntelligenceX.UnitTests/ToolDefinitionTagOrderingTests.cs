using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class ToolDefinitionTagOrderingTests {

    [Fact]
    public void CreateAliasDefinition_ShouldReturnStableTags_ForDifferentOverrideInputOrdering() {
        var canonical = ToolSelectionMetadata.Enrich(
            new ToolDefinition(
                name: "custom_probe",
                description: "Probe",
                parameters: null,
                tags: new[] { "zeta", "alpha" }),
            toolType: null);

        var aliasA = canonical.CreateAliasDefinition(
            aliasName: "custom_probe_alias_a",
            tags: new[] {
                "routing:explicit",
                "risk:high",
                "scope:domain",
                "operation:search"
            });

        var aliasB = canonical.CreateAliasDefinition(
            aliasName: "custom_probe_alias_b",
            tags: new[] {
                "operation:search",
                "scope:domain",
                "risk:high",
                "routing:explicit"
            });

        Assert.Equal(aliasA.Tags, aliasB.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(aliasA.Tags.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase), aliasA.Tags);
        Assert.Contains("scope:domain", aliasA.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("operation:search", aliasA.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("entity:resource", aliasA.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("risk:high", aliasA.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("routing:explicit", aliasA.Tags, StringComparer.OrdinalIgnoreCase);
        AssertSingleTaxonomyTag(aliasA.Tags, "scope:");
        AssertSingleTaxonomyTag(aliasA.Tags, "operation:");
        AssertSingleTaxonomyTag(aliasA.Tags, "entity:");
        AssertSingleTaxonomyTag(aliasA.Tags, "risk:");
        AssertSingleTaxonomyTag(aliasA.Tags, "routing:");
    }

    private static void AssertSingleTaxonomyTag(IReadOnlyList<string> tags, string prefix) {
        Assert.Equal(
            1,
            tags.Count(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    }
}
