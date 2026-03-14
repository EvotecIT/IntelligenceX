using System;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class ToolSelectionHintTagsTests {
    [Fact]
    public void ApplyExplicitRoutingHints_ShouldDriveSelectionMetadata_ForCustomToolName() {
        var definition = ToolSelectionHintTags.ApplyExplicitRoutingHints(
            new ToolDefinition(
                name: "custom_owned_tool",
                description: "Custom tool",
                parameters: new JsonObject()
                    .Add("type", "object")
                    .Add("properties", new JsonObject())
                    .Add("additionalProperties", false),
                category: "system"),
            scope: "host",
            operation: "probe",
            entity: "host",
            risk: "medium",
            additionalTags: new[] { "inventory" });

        var enriched = ToolSelectionMetadata.Enrich(definition, toolType: null);
        var routing = ToolSelectionMetadata.ResolveRouting(definition);

        Assert.Contains("inventory", enriched.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("scope:host", enriched.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("operation:probe", enriched.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("entity:host", enriched.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("risk:medium", enriched.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("routing:explicit", enriched.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(enriched.Tags, static tag => ToolSelectionHintTags.IsControlTag(tag));
        Assert.True(routing.IsExplicit);
        Assert.Equal("host", routing.Scope);
        Assert.Equal("probe", routing.Operation);
        Assert.Equal("host", routing.Entity);
        Assert.Equal("medium", routing.Risk);
    }

    [Fact]
    public void ApplyExplicitRoutingHints_ShouldStampStructuredRoutingContractFields() {
        var definition = ToolSelectionHintTags.ApplyExplicitRoutingHints(
            new ToolDefinition(
                name: "custom_structured_tool",
                description: "Custom tool",
                parameters: new JsonObject()
                    .Add("type", "object")
                    .Add("properties", new JsonObject())
                    .Add("additionalProperties", false),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            scope: "host",
            operation: "probe",
            entity: "resource",
            risk: "medium");

        var routing = Assert.IsType<ToolRoutingContract>(definition.Routing);

        Assert.Equal("host", routing.Scope);
        Assert.Equal("probe", routing.Operation);
        Assert.Equal("resource", routing.Entity);
        Assert.Equal("medium", routing.Risk);
        Assert.Equal(ToolRoutingTaxonomy.SourceExplicit, routing.RoutingSource);
    }

    [Fact]
    public void ResolveRouting_ShouldPreferStructuredRoutingContractFieldsOverHeuristics() {
        var definition = new ToolDefinition(
            name: "custom_probe",
            description: "Custom tool",
            parameters: new JsonObject()
                .Add("type", "object")
                .Add("properties", new JsonObject())
                .Add("additionalProperties", false),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RoleOperational,
                Scope = "host",
                Operation = "probe",
                Entity = "command",
                Risk = "medium"
            });

        var routing = ToolSelectionMetadata.ResolveRouting(definition);
        var enriched = ToolSelectionMetadata.Enrich(definition, toolType: null);
        var enrichedRouting = Assert.IsType<ToolRoutingContract>(enriched.Routing);

        Assert.True(routing.IsExplicit);
        Assert.Equal("host", routing.Scope);
        Assert.Equal("probe", routing.Operation);
        Assert.Equal("command", routing.Entity);
        Assert.Equal("medium", routing.Risk);
        Assert.Equal("host", enrichedRouting.Scope);
        Assert.Equal("probe", enrichedRouting.Operation);
        Assert.Equal("command", enrichedRouting.Entity);
        Assert.Equal("medium", enrichedRouting.Risk);
    }
}
