using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Tools;
using IntelligenceX.Tools.ADPlayground;
using IntelligenceX.Tools.DnsClientX;
using IntelligenceX.Tools.DomainDetective;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class Wave1ToolContractMigrationTests {
    private static readonly HashSet<string> Wave1Packs = new(StringComparer.OrdinalIgnoreCase) {
        "active_directory",
        "domaindetective",
        "dnsclientx"
    };

    private static readonly IReadOnlyDictionary<string, string> FamilyByPack =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["active_directory"] = ToolSelectionMetadata.DomainIntentFamilyAd,
            ["domaindetective"] = ToolSelectionMetadata.DomainIntentFamilyPublic,
            ["dnsclientx"] = ToolSelectionMetadata.DomainIntentFamilyPublic
        };

    [Fact]
    public void Wave1Tools_ShouldExposeExplicitRoutingSetupAndRecoveryContracts() {
        var definitions = BuildWave1CanonicalDefinitions();

        foreach (var definition in definitions) {
            var routing = Assert.IsType<ToolRoutingContract>(definition.Routing);
            Assert.True(routing.IsRoutingAware);
            Assert.Equal(ToolRoutingTaxonomy.SourceExplicit, routing.RoutingSource, ignoreCase: true);
            Assert.Contains(routing.PackId, Wave1Packs, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(routing.Role, ToolRoutingTaxonomy.AllowedRoles, StringComparer.Ordinal);

            if (string.Equals(routing.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var setup = Assert.IsType<ToolSetupContract>(definition.Setup);
            Assert.True(setup.IsSetupAware);
            Assert.True(setup.Requirements.Count > 0 || setup.SetupHintKeys.Count > 0 || !string.IsNullOrWhiteSpace(setup.SetupToolName));

            var recovery = Assert.IsType<ToolRecoveryContract>(definition.Recovery);
            Assert.True(recovery.IsRecoveryAware);
        }
    }

    [Fact]
    public void Wave1Tools_ShouldDeclarePackFamilyConsistently() {
        var definitions = BuildWave1CanonicalDefinitions();

        foreach (var definition in definitions) {
            var routing = Assert.IsType<ToolRoutingContract>(definition.Routing);
            Assert.True(FamilyByPack.TryGetValue(routing.PackId, out var expectedFamily));
            Assert.Equal(expectedFamily, routing.DomainIntentFamily);
            Assert.False(string.IsNullOrWhiteSpace(routing.DomainIntentActionId));
        }
    }

    [Fact]
    public void Wave1Tools_ShouldKeepAdAndPublicBoundaryUnlessExplicitHandoffRouteExists() {
        var definitions = BuildWave1CanonicalDefinitions();
        var crossFamilyRoutes = new List<(string SourceTool, string SourcePack, string TargetPack)>();

        foreach (var definition in definitions) {
            var routing = Assert.IsType<ToolRoutingContract>(definition.Routing);
            var sourcePack = routing.PackId;
            var sourceFamily = routing.DomainIntentFamily;
            var handoff = definition.Handoff;
            if (handoff is null || !handoff.IsHandoffAware) {
                continue;
            }

            foreach (var route in handoff.OutboundRoutes) {
                if (!FamilyByPack.TryGetValue(route.TargetPackId, out var targetFamily)) {
                    continue;
                }

                if (string.Equals(sourceFamily, targetFamily, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                crossFamilyRoutes.Add((definition.Name, sourcePack, route.TargetPackId));

                var allowedSource =
                    string.Equals(definition.Name, "domaindetective_domain_summary", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(definition.Name, "domaindetective_network_probe", StringComparison.OrdinalIgnoreCase);
                Assert.True(allowedSource, $"Unexpected cross-family handoff route from '{definition.Name}'.");
                Assert.Equal("domaindetective", sourcePack, ignoreCase: true);
                Assert.Equal("active_directory", route.TargetPackId, ignoreCase: true);
            }
        }

        Assert.NotEmpty(crossFamilyRoutes);
        Assert.DoesNotContain(
            crossFamilyRoutes,
            static route => string.Equals(route.SourcePack, "active_directory", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<ToolDefinition> BuildWave1CanonicalDefinitions() {
        var registry = new ToolRegistry();
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());
        registry.RegisterDnsClientXPack(new DnsClientXToolOptions());
        registry.RegisterDomainDetectivePack(new DomainDetectiveToolOptions());

        return registry.GetDefinitions()
            .Where(static definition => string.IsNullOrWhiteSpace(definition.AliasOf))
            .Where(definition => Wave1Packs.Contains(definition.Routing?.PackId ?? string.Empty))
            .ToArray();
    }
}
