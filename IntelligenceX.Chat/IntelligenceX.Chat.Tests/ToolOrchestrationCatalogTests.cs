using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ToolOrchestrationCatalogTests {
    [Fact]
    public void Build_ProjectsContractsAndIndexesByPackRole() {
        var catalog = ToolOrchestrationCatalog.Build(new[] {
            CreateDefinition(
                name: "custom_pack_info",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "CustomX",
                    Role = ToolRoutingTaxonomy.RolePackInfo
                },
                setup: new ToolSetupContract {
                    IsSetupAware = true,
                    SetupToolName = "custom_setup"
                },
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "dnsclientx",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "host",
                                    TargetArgument = "target"
                                }
                            }
                        }
                    }
                },
                recovery: new ToolRecoveryContract {
                    IsRecoveryAware = true,
                    SupportsTransientRetry = true,
                    MaxRetryAttempts = 3,
                    SupportsAlternateEngines = true,
                    AlternateEngineIds = new[] { "wmi", "cim" }
                }),
            CreateDefinition(
                name: "custom_operational_query",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        });

        Assert.Equal(2, catalog.Count);
        Assert.True(catalog.TryGetEntry("custom_pack_info", out var entry));
        Assert.Equal("custom_pack_info", entry.ToolName);
        Assert.Equal("customx", entry.PackId);
        Assert.Equal(ToolRoutingTaxonomy.RolePackInfo, entry.Role);
        Assert.Equal(ToolRoutingTaxonomy.SourceExplicit, entry.RoutingSource);
        Assert.True(entry.IsRoutingAware);
        Assert.True(entry.IsSetupAware);
        Assert.Equal(1, entry.HandoffRouteCount);
        Assert.Equal(1, entry.HandoffBindingCount);
        Assert.True(entry.IsRecoveryAware);
        Assert.True(entry.SupportsTransientRetry);
        Assert.Equal(3, entry.MaxRetryAttempts);
        Assert.True(entry.SupportsAlternateEngines);
        Assert.Equal(2, entry.AlternateEngineCount);
        Assert.Equal("custom_setup", entry.SetupToolName);

        var byPack = catalog.GetByPackId("customx");
        Assert.Equal(2, byPack.Count);
        Assert.Equal("custom_operational_query", byPack[0].ToolName);
        Assert.Equal("custom_pack_info", byPack[1].ToolName);

        var byRole = catalog.GetByRole(ToolRoutingTaxonomy.RolePackInfo);
        Assert.Single(byRole);
        Assert.Equal("custom_pack_info", byRole[0].ToolName);

        var byPackAndRole = catalog.GetByPackAndRole("customx", ToolRoutingTaxonomy.RolePackInfo);
        Assert.Single(byPackAndRole);
        Assert.Equal("custom_pack_info", byPackAndRole[0].ToolName);
    }

    [Fact]
    public void Build_UsesRegisteredPackAssignmentsWhenRoutingPackIsMissing() {
        var definitions = new[] {
            CreateDefinition(
                name: "orphan_tool",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };

        var catalogWithoutFallback = ToolOrchestrationCatalog.Build(definitions);
        Assert.False(catalogWithoutFallback.TryGetPackId("orphan_tool", out _));

        var registeredPackIdsByToolName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["orphan_tool"] = "System"
        };
        var catalogWithFallback = ToolOrchestrationCatalog.Build(definitions, registeredPackIdsByToolName);

        Assert.True(catalogWithFallback.TryGetPackId("orphan_tool", out var packId));
        Assert.Equal("system", packId);
        var byPack = catalogWithFallback.GetByPackId("system");
        Assert.Single(byPack);
        Assert.Equal("orphan_tool", byPack[0].ToolName);
    }

    private static ToolDefinition CreateDefinition(
        string name,
        ToolRoutingContract? routing = null,
        ToolSetupContract? setup = null,
        ToolHandoffContract? handoff = null,
        ToolRecoveryContract? recovery = null) {
        return new ToolDefinition(
            name: name,
            description: "test tool",
            routing: routing,
            setup: setup,
            handoff: handoff,
            recovery: recovery);
    }
}
