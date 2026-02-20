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

        Assert.True(aliasA.Tags.SequenceEqual(aliasB.Tags, StringComparer.OrdinalIgnoreCase));
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

    [Fact]
    public void CreateAliasDefinition_ShouldRetainCanonicalTaxonomy_WhenAliasDoesNotOverrideKeys() {
        var canonical = ToolSelectionMetadata.Enrich(
            new ToolDefinition(
                name: "custom_probe",
                description: "Probe",
                parameters: null,
                tags: new[] { "zeta", "alpha" }),
            toolType: null);

        var alias = canonical.CreateAliasDefinition(
            aliasName: "custom_probe_alias",
            tags: new[] { "host", "HOST", "inventory" });

        Assert.Contains("scope:general", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("operation:probe", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("entity:resource", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("risk:low", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("routing:inferred", alias.Tags, StringComparer.OrdinalIgnoreCase);
        AssertSingleTaxonomyTag(alias.Tags, "scope:");
        AssertSingleTaxonomyTag(alias.Tags, "operation:");
        AssertSingleTaxonomyTag(alias.Tags, "entity:");
        AssertSingleTaxonomyTag(alias.Tags, "risk:");
        AssertSingleTaxonomyTag(alias.Tags, "routing:");
    }

    [Fact]
    public void CreateAliasDefinition_ShouldIgnoreMalformedTaxonomyOverrideTags() {
        var canonical = ToolSelectionMetadata.Enrich(
            new ToolDefinition(
                name: "custom_probe",
                description: "Probe",
                parameters: null,
                tags: new[] { "zeta", "alpha" }),
            toolType: null);

        var alias = canonical.CreateAliasDefinition(
            aliasName: "custom_probe_alias",
            tags: new[] { "risk:", "routing:", "scope:   ", "operation:search" });

        Assert.DoesNotContain("risk:", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("routing:", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("scope:", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("scope:general", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("operation:search", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("entity:resource", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("risk:low", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("routing:inferred", alias.Tags, StringComparer.OrdinalIgnoreCase);
        AssertSingleTaxonomyTag(alias.Tags, "scope:");
        AssertSingleTaxonomyTag(alias.Tags, "operation:");
        AssertSingleTaxonomyTag(alias.Tags, "entity:");
        AssertSingleTaxonomyTag(alias.Tags, "risk:");
        AssertSingleTaxonomyTag(alias.Tags, "routing:");
    }

    [Fact]
    public void CreateAliasDefinition_ShouldApplyCaseInsensitiveTaxonomyOverrides() {
        var canonical = ToolSelectionMetadata.Enrich(
            new ToolDefinition(
                name: "custom_probe",
                description: "Probe",
                parameters: null,
                tags: new[] { "alpha" }),
            toolType: null);

        var alias = canonical.CreateAliasDefinition(
            aliasName: "custom_probe_alias",
            tags: new[] { "RISK:LOW", "risk:high", "ROUTING:EXPLICIT", "routing:explicit" });

        Assert.Contains("risk:high", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("risk:low", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("routing:explicit", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("routing:inferred", alias.Tags, StringComparer.OrdinalIgnoreCase);
        AssertSingleTaxonomyTag(alias.Tags, "risk:");
        AssertSingleTaxonomyTag(alias.Tags, "routing:");
    }

    [Fact]
    public void CreateAliasDefinition_ShouldEmitDeterministicSortedTags_AcrossMergeSources() {
        var definition = new ToolDefinition(
            name: "custom_probe",
            description: "Probe",
            parameters: null,
            tags: new[] { "beta", "alpha", "scope:general", "risk:low" });

        var alias = definition.CreateAliasDefinition(
            aliasName: "custom_probe_alias",
            tags: new[] { "zeta", "gamma", "operation:search" });

        Assert.Equal(alias.Tags.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase), alias.Tags);
        Assert.Contains("alpha", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("beta", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("gamma", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("zeta", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("operation:search", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("risk:low", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("scope:general", alias.Tags, StringComparer.OrdinalIgnoreCase);
        AssertSingleTaxonomyTag(alias.Tags, "operation:");
        AssertSingleTaxonomyTag(alias.Tags, "risk:");
        AssertSingleTaxonomyTag(alias.Tags, "scope:");
    }

    private static void AssertSingleTaxonomyTag(IReadOnlyList<string> tags, string prefix) {
        Assert.Equal(
            1,
            tags.Count(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    }
}
