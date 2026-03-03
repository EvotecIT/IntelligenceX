using System;
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
                    SetupToolName = "custom_setup",
                    SetupHintKeys = new[] { "needs_auth" },
                    Requirements = new[] {
                        new ToolSetupRequirement {
                            RequirementId = "auth.session",
                            Kind = ToolSetupRequirementKinds.Authentication,
                            IsRequired = true,
                            HintKeys = new[] { "auth_required" }
                        }
                    }
                },
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "dnsclientx",
                            TargetToolName = "dns_lookup",
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
                    RetryableErrorCodes = new[] { "timeout", "query_failed" },
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
        Assert.Equal(ToolSetupContract.DefaultContractId, entry.SetupContractId);
        Assert.Equal(new[] { "auth.session" }, entry.SetupRequirementIds);
        Assert.Equal(new[] { ToolSetupRequirementKinds.Authentication }, entry.SetupRequirementKinds);
        Assert.Equal(new[] { "auth_required", "needs_auth" }, entry.SetupHintKeys);
        Assert.Equal(1, entry.HandoffRouteCount);
        Assert.Equal(1, entry.HandoffBindingCount);
        Assert.Equal(ToolHandoffContract.DefaultContractId, entry.HandoffContractId);
        Assert.Single(entry.HandoffEdges);
        Assert.Equal("dnsclientx", entry.HandoffEdges[0].TargetPackId);
        Assert.Equal("dns_lookup", entry.HandoffEdges[0].TargetToolName);
        Assert.Equal(ToolRoutingTaxonomy.RoleOperational, entry.HandoffEdges[0].TargetRole);
        Assert.Equal(1, entry.HandoffEdges[0].BindingCount);
        Assert.Equal(new[] { "host->target" }, entry.HandoffEdges[0].BindingPairs);
        Assert.True(entry.IsRecoveryAware);
        Assert.Equal(ToolRecoveryContract.DefaultContractId, entry.RecoveryContractId);
        Assert.True(entry.SupportsTransientRetry);
        Assert.Equal(3, entry.MaxRetryAttempts);
        Assert.Equal(new[] { "query_failed", "timeout" }, entry.RetryableErrorCodes);
        Assert.True(entry.SupportsAlternateEngines);
        Assert.Equal(2, entry.AlternateEngineCount);
        Assert.Equal(new[] { "cim", "wmi" }, entry.AlternateEngineIds);
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
    public void Build_DoesNotAssignPackWhenRoutingPackIsMissing() {
        var definitions = new[] {
            CreateDefinition(
                name: "orphan_tool",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };

        var catalog = ToolOrchestrationCatalog.Build(definitions);
        Assert.False(catalog.TryGetPackId("orphan_tool", out _));
        Assert.Empty(catalog.GetByPackId("system"));
    }

    [Fact]
    public void Build_DoesNotInferCrossPackHandoffWithoutExplicitContracts() {
        var catalog = ToolOrchestrationCatalog.Build(new[] {
            CreateDefinition(
                name: "ad_replication_probe",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "active_directory",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            CreateDefinition(
                name: "domaindetective_domain_summary",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "domaindetective",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        });

        Assert.True(catalog.TryGetEntry("ad_replication_probe", out var adEntry));
        Assert.True(catalog.TryGetEntry("domaindetective_domain_summary", out var publicEntry));
        Assert.False(adEntry.IsHandoffAware);
        Assert.False(publicEntry.IsHandoffAware);
        Assert.Equal(0, adEntry.HandoffRouteCount);
        Assert.Equal(0, publicEntry.HandoffRouteCount);
        Assert.Empty(adEntry.HandoffEdges);
        Assert.Empty(publicEntry.HandoffEdges);
    }

    [Fact]
    public void Build_NormalizesHandoffCountersAgainstProjectedBindings() {
        var catalog = ToolOrchestrationCatalog.Build(new[] {
            CreateDefinition(
                name: "custom_pack_info",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RolePackInfo
                },
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "dnsclientx",
                            TargetToolName = "dns_lookup",
                            Bindings = new[] {
                                new ToolHandoffBinding { SourceField = "host", TargetArgument = "target" },
                                new ToolHandoffBinding { SourceField = "HOST", TargetArgument = "TARGET" }
                            }
                        }
                    }
                })
        });

        Assert.True(catalog.TryGetEntry("custom_pack_info", out var entry));
        Assert.Equal(1, entry.HandoffRouteCount);
        Assert.Equal(1, entry.HandoffBindingCount);
        Assert.Single(entry.HandoffEdges);
        Assert.Equal(1, entry.HandoffEdges[0].BindingCount);
        Assert.Equal(new[] { "host->target" }, entry.HandoffEdges[0].BindingPairs);
    }

    [Fact]
    public void Build_NormalizesSetupRequirementCountAgainstProjectedRequirements() {
        var catalog = ToolOrchestrationCatalog.Build(new[] {
            CreateDefinition(
                name: "custom_setup_tool",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                setup: new ToolSetupContract {
                    IsSetupAware = true,
                    SetupHintKeys = new[] { "needs_auth", "needs_auth" },
                    Requirements = new[] {
                        new ToolSetupRequirement {
                            RequirementId = "auth.session",
                            Kind = ToolSetupRequirementKinds.Authentication,
                            IsRequired = true,
                            HintKeys = new[] { "auth_required" }
                        },
                        new ToolSetupRequirement {
                            RequirementId = "auth.session",
                            Kind = ToolSetupRequirementKinds.Authentication,
                            IsRequired = true,
                            HintKeys = new[] { "auth_required" }
                        }
                    }
                })
        });

        Assert.True(catalog.TryGetEntry("custom_setup_tool", out var entry));
        Assert.True(entry.IsSetupAware);
        Assert.Equal(1, entry.SetupRequirementCount);
        Assert.Equal(entry.SetupRequirementIds.Count, entry.SetupRequirementCount);
        Assert.Equal(entry.SetupRequirementKinds.Count, entry.SetupRequirementCount);
        Assert.Equal(new[] { "auth.session" }, entry.SetupRequirementIds);
        Assert.Equal(new[] { ToolSetupRequirementKinds.Authentication }, entry.SetupRequirementKinds);
    }

    [Fact]
    public void Build_ExcludesHandoffRoutesWhenNormalizedBindingsAreEmpty() {
        var definition = CreateDefinition(
            name: "custom_pack_info",
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "customx",
                Role = ToolRoutingTaxonomy.RolePackInfo
            },
            handoff: new ToolHandoffContract {
                IsHandoffAware = true,
                OutboundRoutes = new[] {
                    new ToolHandoffRoute {
                        TargetPackId = "dnsclientx",
                        TargetToolName = "dns_lookup",
                        Bindings = new[] {
                            new ToolHandoffBinding { SourceField = "host", TargetArgument = "target" }
                        }
                    }
                }
            });

        // Simulate a malformed in-memory mutation after contract validation.
        definition.Handoff!.OutboundRoutes![0].Bindings![0].SourceField = " ";

        var catalog = ToolOrchestrationCatalog.Build(new[] { definition });

        Assert.True(catalog.TryGetEntry("custom_pack_info", out var entry));
        Assert.Equal(0, entry.HandoffRouteCount);
        Assert.Equal(0, entry.HandoffBindingCount);
        Assert.Empty(entry.HandoffEdges);
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
