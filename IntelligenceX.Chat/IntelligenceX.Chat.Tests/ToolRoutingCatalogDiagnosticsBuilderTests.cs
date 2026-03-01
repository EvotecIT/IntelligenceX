using System;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ToolRoutingCatalogDiagnosticsBuilderTests {
    [Fact]
    public void Build_HealthyCatalog_ReportsNoWarnings() {
        var diagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(new[] {
            CreateDefinition(
                name: "ad_replication_summary",
                category: "active_directory",
                routing: new ToolRoutingContract {
                    PackId = "active_directory",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdAd
                }),
            CreateDefinition(
                name: "ad_dc_health",
                category: "active_directory",
                routing: new ToolRoutingContract {
                    PackId = "active_directory",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdAd
                }),
            CreateDefinition(
                name: "dnsclientx_query",
                category: "dns",
                routing: new ToolRoutingContract {
                    PackId = "dnsclientx",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyPublic,
                    DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdPublic
                })
        });

        Assert.True(diagnostics.IsHealthy);
        Assert.Equal(3, diagnostics.TotalTools);
        Assert.Equal(3, diagnostics.RoutingAwareTools);
        Assert.Equal(0, diagnostics.MissingRoutingContractTools);
        Assert.Equal(3, diagnostics.DomainFamilyTools);
        Assert.Equal(0, diagnostics.ExpectedDomainFamilyMissingTools);
        Assert.Equal(0, diagnostics.DomainFamilyMissingActionTools);
        Assert.Equal(0, diagnostics.ActionWithoutFamilyTools);
        Assert.Equal(0, diagnostics.FamilyActionConflictFamilies);

        var summary = ToolRoutingCatalogDiagnosticsBuilder.FormatSummary(diagnostics);
        Assert.Contains("tools=3", summary, StringComparison.Ordinal);
        Assert.Contains("conflicts=0", summary, StringComparison.Ordinal);

        var familySummaries = ToolRoutingCatalogDiagnosticsBuilder.FormatFamilySummaries(diagnostics, maxItems: 8);
        Assert.Contains(familySummaries, static line => line.Contains("ad_domain", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(familySummaries, static line => line.Contains("public_domain", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(ToolRoutingCatalogDiagnosticsBuilder.BuildWarnings(diagnostics));
    }

    [Fact]
    public void Build_DegradedCatalog_ReportsConflictsAndMissingMetadata() {
        var missingActionDefinition = CreateDefinition(
            name: "dnsclientx_probe",
            category: "dns",
            routing: new ToolRoutingContract {
                PackId = "dnsclientx",
                DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyPublic,
                DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdPublic
            });
        // Simulate accidental runtime mutation after registration/enrichment.
        missingActionDefinition.Routing!.DomainIntentActionId = string.Empty;

        var diagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(new[] {
            CreateDefinition(
                name: "ad_tool_a",
                category: "active_directory",
                routing: new ToolRoutingContract {
                    PackId = "active_directory",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = "act_domain_scope_ad_a"
                }),
            CreateDefinition(
                name: "ad_tool_b",
                category: "active_directory",
                routing: new ToolRoutingContract {
                    PackId = "active_directory",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = "act_domain_scope_ad_b"
                }),
            missingActionDefinition,
            CreateDefinition(
                name: "utility_action_only",
                category: "general",
                routing: new ToolRoutingContract {
                    DomainIntentActionId = "act_orphan_action"
                }),
            CreateDefinition(
                name: "dnsclientx_no_routing",
                category: "dns",
                routing: null)
        });

        Assert.False(diagnostics.IsHealthy);
        Assert.Equal(5, diagnostics.TotalTools);
        Assert.Equal(4, diagnostics.RoutingAwareTools);
        Assert.Equal(1, diagnostics.MissingRoutingContractTools);
        Assert.Equal(3, diagnostics.DomainFamilyTools);
        Assert.Equal(1, diagnostics.ExpectedDomainFamilyMissingTools);
        Assert.Equal(1, diagnostics.DomainFamilyMissingActionTools);
        Assert.Equal(1, diagnostics.ActionWithoutFamilyTools);
        Assert.Equal(1, diagnostics.FamilyActionConflictFamilies);

        var warnings = ToolRoutingCatalogDiagnosticsBuilder.BuildWarnings(diagnostics, maxWarnings: 12);
        Assert.Contains(warnings, static line => line.Contains("missing routing contracts", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, static line => line.Contains("missing domain intent family", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, static line => line.Contains("miss action id", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, static line => line.Contains("action id without a domain intent family", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, static line => line.Contains("multiple action ids", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, static line => line.Contains("conflict ad_domain", StringComparison.OrdinalIgnoreCase));
    }

    private static ToolDefinition CreateDefinition(string name, string? category, ToolRoutingContract? routing) {
        return new ToolDefinition(
            name: name,
            description: "test tool",
            category: category,
            routing: routing);
    }
}
