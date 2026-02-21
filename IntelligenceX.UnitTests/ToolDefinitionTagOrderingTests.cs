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
    public void Constructor_ShouldReportDroppedMalformedTaxonomyTags_WhenObserverConfigured() {
        var observed = new List<string>();
        ToolDefinition.MalformedTaxonomyTagDroppedObserver = observed.Add;
        try {
            _ = new ToolDefinition(
                name: "custom_probe",
                description: "Probe",
                parameters: null,
                tags: new[] { "risk:", "scope:   ", "alpha" });
        } finally {
            ToolDefinition.MalformedTaxonomyTagDroppedObserver = null;
        }

        Assert.Contains("risk:", observed, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("scope:", observed, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_ShouldIgnoreObserverFailures_WhenDroppingMalformedTaxonomyTags() {
        ToolDefinition.MalformedTaxonomyTagDroppedObserver = _ => throw new InvalidOperationException("observer-failure");
        try {
            var definition = new ToolDefinition(
                name: "custom_probe",
                description: "Probe",
                parameters: null,
                tags: new[] { "risk:", "alpha" });
            Assert.Contains("alpha", definition.Tags, StringComparer.OrdinalIgnoreCase);
        } finally {
            ToolDefinition.MalformedTaxonomyTagDroppedObserver = null;
        }
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
    public void Constructor_ShouldPreferLastTaxonomyValue_ForDuplicateKeysWithinSameTagSet() {
        var definition = new ToolDefinition(
            name: "custom_probe",
            description: "Probe",
            parameters: null,
            tags: new[] { "risk:low", "RISK:HIGH", "scope:general", "scope:domain", "alpha" });

        Assert.Contains("risk:high", definition.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("risk:low", definition.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("scope:domain", definition.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("scope:general", definition.Tags, StringComparer.OrdinalIgnoreCase);
        AssertSingleTaxonomyTag(definition.Tags, "risk:");
        AssertSingleTaxonomyTag(definition.Tags, "scope:");
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

    [Fact]
    public void CreateAliasDefinition_ShouldApplyCaseInsensitiveTaxonomyOverrides_AcrossAllKeys() {
        var canonical = ToolSelectionMetadata.Enrich(
            new ToolDefinition(
                name: "custom_probe",
                description: "Probe",
                parameters: null,
                tags: new[] {
                    "scope:general",
                    "operation:probe",
                    "entity:resource",
                    "risk:low",
                    "routing:inferred"
                }),
            toolType: null);

        var alias = canonical.CreateAliasDefinition(
            aliasName: "custom_probe_alias",
            tags: new[] {
                "SCOPE:DOMAIN",
                "Operation:Search",
                "ENTITY:directory_object",
                "RISK:HIGH",
                "ROUTING:EXPLICIT"
            });

        Assert.Contains("scope:domain", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("scope:general", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("operation:search", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("operation:probe", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("entity:directory_object", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("entity:resource", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("risk:high", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("risk:low", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("routing:explicit", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("routing:inferred", alias.Tags, StringComparer.OrdinalIgnoreCase);
        AssertSingleTaxonomyTag(alias.Tags, "scope:");
        AssertSingleTaxonomyTag(alias.Tags, "operation:");
        AssertSingleTaxonomyTag(alias.Tags, "entity:");
        AssertSingleTaxonomyTag(alias.Tags, "risk:");
        AssertSingleTaxonomyTag(alias.Tags, "routing:");
    }

    [Fact]
    public void CreateAliasDefinition_ShouldBeDeterministic_ForNonTaxonomyOverrideInputOrder() {
        var canonical = ToolSelectionMetadata.Enrich(
            new ToolDefinition(
                name: "custom_probe",
                description: "Probe",
                parameters: null,
                tags: new[] { "beta", "alpha", "scope:general", "operation:probe" }),
            toolType: null);

        var aliasA = canonical.CreateAliasDefinition(
            aliasName: "custom_probe_alias_a",
            tags: new[] { "delta", "gamma", "risk:high", "routing:explicit" });
        var aliasB = canonical.CreateAliasDefinition(
            aliasName: "custom_probe_alias_b",
            tags: new[] { "gamma", "delta", "routing:explicit", "risk:high" });

        Assert.True(aliasA.Tags.SequenceEqual(aliasB.Tags, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(aliasA.Tags.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase), aliasA.Tags);
        Assert.Contains("alpha", aliasA.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("beta", aliasA.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("delta", aliasA.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("gamma", aliasA.Tags, StringComparer.OrdinalIgnoreCase);
        AssertSingleTaxonomyTag(aliasA.Tags, "scope:");
        AssertSingleTaxonomyTag(aliasA.Tags, "operation:");
        AssertSingleTaxonomyTag(aliasA.Tags, "risk:");
        AssertSingleTaxonomyTag(aliasA.Tags, "routing:");
    }

    [Fact]
    public void CreateAliasDefinition_ShouldPreferOverrideTaxonomyValue_WithCaseAndWhitespaceVariants() {
        var canonical = new ToolDefinition(
            name: "custom_probe",
            description: "Probe",
            parameters: null,
            tags: new[] { "Risk:Low", "Scope:General", "inventory" });

        var alias = canonical.CreateAliasDefinition(
            aliasName: "custom_probe_alias",
            tags: new[] { " risk:HIGH ", " scope:domain ", "routing:explicit" });

        Assert.Contains("risk:HIGH", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Risk:Low", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("scope:domain", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Scope:General", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("routing:explicit", alias.Tags, StringComparer.OrdinalIgnoreCase);
        AssertSingleTaxonomyTag(alias.Tags, "risk:");
        AssertSingleTaxonomyTag(alias.Tags, "scope:");
        AssertSingleTaxonomyTag(alias.Tags, "routing:");
    }

    [Fact]
    public void CreateAliasDefinition_ShouldOverrideSingleTaxonomyKey_AndPreserveRemainingCanonicalKeys() {
        var canonical = new ToolDefinition(
            name: "custom_probe",
            description: "Probe",
            parameters: null,
            tags: new[] {
                "scope:general",
                "operation:probe",
                "entity:resource",
                "risk:low",
                "routing:inferred",
                "inventory"
            });

        var alias = canonical.CreateAliasDefinition(
            aliasName: "custom_probe_alias",
            tags: new[] { "operation:search" });

        Assert.Contains("scope:general", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("operation:search", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("operation:probe", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("entity:resource", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("risk:low", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("routing:inferred", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("inventory", alias.Tags, StringComparer.OrdinalIgnoreCase);
        AssertSingleTaxonomyTag(alias.Tags, "scope:");
        AssertSingleTaxonomyTag(alias.Tags, "operation:");
        AssertSingleTaxonomyTag(alias.Tags, "entity:");
        AssertSingleTaxonomyTag(alias.Tags, "risk:");
        AssertSingleTaxonomyTag(alias.Tags, "routing:");
    }

    private static void AssertSingleTaxonomyTag(IReadOnlyList<string> tags, string prefix) {
        Assert.Equal(
            1,
            tags.Count(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    }
}
