using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
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
                    AlternateEngineIds = new[] { "wmi", "cim" },
                    RecoveryToolNames = new[] { "custom_discover_scope", "custom_pack_info" }
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
        Assert.Equal(2, entry.RecoveryToolCount);
        Assert.Equal(new[] { "custom_discover_scope", "custom_pack_info" }, entry.RecoveryToolNames);
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
    public void Build_PrefersPackOwnedCatalogContracts_WhenAvailable() {
        var definition = CreateDefinition(
            name: "custom_pack_info",
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "legacy_pack",
                Role = ToolRoutingTaxonomy.RoleOperational
            });

        var packCatalogEntry = new ToolPackToolCatalogEntryModel {
            Name = "custom_pack_info",
            Description = "Pack info",
            Traits = new ToolPackToolTraitsModel {
                SupportsTargetScoping = true,
                TargetScopeArguments = new[] { "machine_name" },
                SupportsRemoteHostTargeting = true,
                RemoteHostArguments = new[] { "machine_name" }
            },
            Orchestration = new ToolPackToolOrchestrationModel {
                PackId = "customx",
                Role = ToolRoutingTaxonomy.RolePackInfo,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                IsRoutingAware = true,
                DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                DomainIntentActionId = "act_custom_scope",
                IsSetupAware = true,
                SetupRequirementCount = 1,
                SetupToolName = "custom_setup",
                SetupContractId = ToolSetupContract.DefaultContractId,
                SetupRequirementIds = new[] { "auth.session" },
                SetupRequirementKinds = new[] { ToolSetupRequirementKinds.Authentication },
                SetupHintKeys = new[] { "needs_auth" },
                IsHandoffAware = true,
                HandoffRouteCount = 1,
                HandoffBindingCount = 1,
                HandoffContractId = ToolHandoffContract.DefaultContractId,
                HandoffEdges = new[] {
                    new ToolPackToolHandoffEdgeModel {
                        TargetPackId = "dnsclientx",
                        TargetToolName = "dns_lookup",
                        TargetRole = ToolRoutingTaxonomy.RoleOperational,
                        BindingCount = 1,
                        BindingPairs = new[] { "host->target" }
                    }
                },
                IsRecoveryAware = true,
                SupportsTransientRetry = true,
                MaxRetryAttempts = 3,
                SupportsAlternateEngines = true,
                AlternateEngineCount = 2,
                RecoveryContractId = ToolRecoveryContract.DefaultContractId,
                RecoveryToolCount = 1,
                RetryableErrorCodes = new[] { "timeout" },
                AlternateEngineIds = new[] { "cim", "wmi" },
                RecoveryToolNames = new[] { "custom_pack_info" }
            }
        };

        var catalog = ToolOrchestrationCatalog.Build(
            new[] { definition },
            new IToolPack[] { new SyntheticCatalogPack("customx", packCatalogEntry) });

        Assert.True(catalog.TryGetEntry("custom_pack_info", out var entry));
        Assert.Equal("customx", entry.PackId);
        Assert.Equal(ToolRoutingTaxonomy.RolePackInfo, entry.Role);
        Assert.True(entry.IsRoutingAware);
        Assert.True(entry.SupportsTargetScoping);
        Assert.Equal(new[] { "machine_name" }, entry.TargetScopeArguments);
        Assert.True(entry.SupportsRemoteHostTargeting);
        Assert.Equal(new[] { "machine_name" }, entry.RemoteHostArguments);
        Assert.Equal(ToolSelectionMetadata.DomainIntentFamilyAd, entry.DomainIntentFamily);
        Assert.Equal("act_custom_scope", entry.DomainIntentActionId);
        Assert.True(entry.IsSetupAware);
        Assert.Equal("custom_setup", entry.SetupToolName);
        Assert.True(entry.IsHandoffAware);
        Assert.Single(entry.HandoffEdges);
        Assert.True(entry.IsRecoveryAware);
        Assert.True(entry.SupportsTransientRetry);
        Assert.Equal(3, entry.MaxRetryAttempts);
        Assert.True(entry.SupportsAlternateEngines);
    }

    [Fact]
    public void Build_NormalizesInvalidPackOwnedRoleOrSource_ToAllowedRoutingTaxonomyValues() {
        var definition = CreateDefinition(
            name: "custom_resolver_query",
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceInferred,
                PackId = "customx",
                Role = ToolRoutingTaxonomy.RoleResolver
            });

        var packCatalogEntry = new ToolPackToolCatalogEntryModel {
            Name = "custom_resolver_query",
            Description = "Resolver query",
            Orchestration = new ToolPackToolOrchestrationModel {
                PackId = "customx",
                Role = "totally_invalid_role",
                RoutingSource = "totally_invalid_source",
                IsRoutingAware = true
            }
        };

        var catalog = ToolOrchestrationCatalog.Build(
            new[] { definition },
            new IToolPack[] { new SyntheticCatalogPack("customx", packCatalogEntry) });

        Assert.True(catalog.TryGetEntry("custom_resolver_query", out var entry));
        Assert.Equal(ToolRoutingTaxonomy.RoleOperational, entry.Role);
        Assert.Equal(ToolRoutingTaxonomy.SourceExplicit, entry.RoutingSource);
        Assert.DoesNotContain("invalid", entry.Role, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("invalid", entry.RoutingSource, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_GetByPackAndRole_IsCaseInsensitive_ForPackAndRoleArguments() {
        var catalog = ToolOrchestrationCatalog.Build(new[] {
            CreateDefinition(
                name: "custom_pack_info",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "CustomX",
                    Role = ToolRoutingTaxonomy.RolePackInfo
                })
        });

        var byPackAndRole = catalog.GetByPackAndRole("CUSTOMX", "PACK_INFO");
        Assert.Single(byPackAndRole);
        Assert.Equal("custom_pack_info", byPackAndRole[0].ToolName);
    }

    [Fact]
    public void Build_ProjectsReadOnlyCollections_ForEntryAndIndexes() {
        var catalog = ToolOrchestrationCatalog.Build(new[] {
            CreateDefinition(
                name: "custom_pack_info",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RolePackInfo
                },
                setup: new ToolSetupContract {
                    IsSetupAware = true,
                    SetupHintKeys = new[] { "needs_auth" },
                    Requirements = new[] {
                        new ToolSetupRequirement {
                            RequirementId = "auth.session",
                            Kind = ToolSetupRequirementKinds.Authentication
                        }
                    }
                },
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "dnsclientx",
                            TargetToolName = "dns_lookup",
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "host",
                                    TargetArgument = "target"
                                }
                            }
                        }
                    }
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

        Assert.True(catalog.TryGetEntry("custom_pack_info", out var entry));
        var setupRequirementIds = Assert.IsAssignableFrom<IList<string>>(entry.SetupRequirementIds);
        Assert.Throws<NotSupportedException>(() => setupRequirementIds[0] = "tampered");
        Assert.Equal("auth.session", entry.SetupRequirementIds[0]);

        var handoffBindingPairs = Assert.IsAssignableFrom<IList<string>>(entry.HandoffEdges[0].BindingPairs);
        Assert.Throws<NotSupportedException>(() => handoffBindingPairs[0] = "tampered->tampered");
        Assert.Equal("host->target", entry.HandoffEdges[0].BindingPairs[0]);

        var handoffEdges = Assert.IsAssignableFrom<IList<ToolOrchestrationHandoffEdge>>(entry.HandoffEdges);
        Assert.Throws<NotSupportedException>(() => handoffEdges.Add(new ToolOrchestrationHandoffEdge()));

        var byPack = Assert.IsAssignableFrom<IList<ToolOrchestrationCatalogEntry>>(catalog.GetByPackId("customx"));
        Assert.Throws<NotSupportedException>(() => byPack.Clear());
    }

    [Fact]
    public void Build_DefensivelyCopiesProjectedCollections_FromDefinitionContracts() {
        var definition = CreateDefinition(
            name: "custom_pack_info",
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "customx",
                Role = ToolRoutingTaxonomy.RolePackInfo
            },
            setup: new ToolSetupContract {
                IsSetupAware = true,
                SetupHintKeys = new[] { "needs_auth" },
                Requirements = new[] {
                    new ToolSetupRequirement {
                        RequirementId = "auth.session",
                        Kind = ToolSetupRequirementKinds.Authentication
                    }
                }
            },
            handoff: new ToolHandoffContract {
                IsHandoffAware = true,
                OutboundRoutes = new[] {
                    new ToolHandoffRoute {
                        TargetPackId = "dnsclientx",
                        TargetToolName = "dns_lookup",
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
                RetryableErrorCodes = new[] { "timeout" },
                AlternateEngineIds = new[] { "cim" },
                RecoveryToolNames = new[] { "custom_discover_scope" }
            });

        var catalog = ToolOrchestrationCatalog.Build(new[] { definition });
        Assert.True(catalog.TryGetEntry("custom_pack_info", out var entryBeforeMutation));

        definition.Setup!.SetupHintKeys = new[] { "mutated_hint" };
        definition.Setup.Requirements![0].RequirementId = "mutated.id";
        definition.Setup.Requirements[0].Kind = "mutated.kind";
        definition.Handoff!.OutboundRoutes![0].Bindings![0].SourceField = "mutated_source";
        definition.Handoff.OutboundRoutes[0].Bindings[0].TargetArgument = "mutated_target";
        definition.Recovery!.RetryableErrorCodes = new[] { "mutated_error" };
        definition.Recovery.AlternateEngineIds = new[] { "mutated_engine" };
        definition.Recovery.RecoveryToolNames = new[] { "mutated_recovery_tool" };

        Assert.True(catalog.TryGetEntry("custom_pack_info", out var entryAfterMutation));
        Assert.Equal(new[] { "auth.session" }, entryAfterMutation.SetupRequirementIds);
        Assert.Equal(new[] { ToolSetupRequirementKinds.Authentication }, entryAfterMutation.SetupRequirementKinds);
        Assert.Equal(new[] { "needs_auth" }, entryAfterMutation.SetupHintKeys);
        Assert.Equal(new[] { "host->target" }, entryAfterMutation.HandoffEdges[0].BindingPairs);
        Assert.Equal(new[] { "timeout" }, entryAfterMutation.RetryableErrorCodes);
        Assert.Equal(new[] { "cim" }, entryAfterMutation.AlternateEngineIds);
        Assert.Equal(new[] { "custom_discover_scope" }, entryAfterMutation.RecoveryToolNames);
        Assert.Equal(entryBeforeMutation, entryAfterMutation);
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
    public void Build_PreservesNormalizedHandoffBindingMultiplicity() {
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
        Assert.Equal(2, entry.HandoffBindingCount);
        Assert.Single(entry.HandoffEdges);
        Assert.Equal(2, entry.HandoffEdges[0].BindingCount);
        Assert.Equal(new[] { "host->target", "host->target" }, entry.HandoffEdges[0].BindingPairs);
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

    [Fact]
    public void Build_SetupRequirementCount_UsesDistinctIdKindPairs() {
        var catalog = ToolOrchestrationCatalog.Build(new[] {
            CreateDefinition(
                name: "custom_setup_pair_tool",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                setup: new ToolSetupContract {
                    IsSetupAware = true,
                    Requirements = new[] {
                        new ToolSetupRequirement {
                            RequirementId = "auth.session",
                            Kind = ToolSetupRequirementKinds.Authentication
                        },
                        new ToolSetupRequirement {
                            RequirementId = "auth.session",
                            Kind = ToolSetupRequirementKinds.Connectivity
                        }
                    }
                })
        });

        Assert.True(catalog.TryGetEntry("custom_setup_pair_tool", out var entry));
        Assert.Equal(2, entry.SetupRequirementCount);
        Assert.Equal(new[] { "auth.session" }, entry.SetupRequirementIds);
        Assert.Equal(
            new[] {
                ToolSetupRequirementKinds.Authentication,
                ToolSetupRequirementKinds.Connectivity
            },
            entry.SetupRequirementKinds);
    }

    [Fact]
    public void Build_DowngradesAwarenessFlagsWhenProjectedContractStateIsMalformedAfterMutation() {
        var definition = CreateDefinition(
            name: "custom_stateful_tool",
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "customx",
                Role = ToolRoutingTaxonomy.RoleOperational
            },
            setup: new ToolSetupContract {
                IsSetupAware = true,
                SetupHintKeys = new[] { "needs_auth" },
                Requirements = new[] {
                    new ToolSetupRequirement {
                        RequirementId = "auth.session",
                        Kind = ToolSetupRequirementKinds.Authentication
                    }
                }
            },
            handoff: new ToolHandoffContract {
                IsHandoffAware = true,
                OutboundRoutes = new[] {
                    new ToolHandoffRoute {
                        TargetPackId = "dnsclientx",
                        TargetToolName = "dns_lookup",
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
                MaxRetryAttempts = 2,
                RetryableErrorCodes = new[] { "timeout" },
                RecoveryToolNames = new[] { "custom_pack_probe" }
            });

        // Simulate malformed in-memory mutations after contract validation.
        definition.Setup!.SetupHintKeys = Array.Empty<string>();
        definition.Setup.SetupToolName = string.Empty;
        definition.Setup.Requirements![0].RequirementId = " ";
        definition.Setup.Requirements[0].Kind = " ";
        definition.Handoff!.OutboundRoutes![0].Bindings![0].SourceField = " ";
        definition.Recovery!.SupportsTransientRetry = false;
        definition.Recovery.MaxRetryAttempts = 0;
        definition.Recovery.SupportsAlternateEngines = false;
        definition.Recovery.RetryableErrorCodes = Array.Empty<string>();
        definition.Recovery.AlternateEngineIds = Array.Empty<string>();
        definition.Recovery.RecoveryToolNames = Array.Empty<string>();
        definition.Recovery.RecoveryContractId = string.Empty;

        var catalog = ToolOrchestrationCatalog.Build(new[] { definition });
        Assert.True(catalog.TryGetEntry("custom_stateful_tool", out var entry));
        Assert.False(entry.IsSetupAware);
        Assert.False(entry.IsHandoffAware);
        Assert.False(entry.IsRecoveryAware);
    }

    [Fact]
    public void Build_ProjectsSchemaTargetScopeAndRemoteHostTraits() {
        var catalog = ToolOrchestrationCatalog.Build(new[] {
            new ToolDefinition(
                "system_tls_posture",
                "Inspect TLS posture for a remote host.",
                ToolSchema.Object(
                        ("computer_name", ToolSchema.String("Remote host.")),
                        ("search_base_dn", ToolSchema.String("Optional directory scope.")),
                        ("columns", ToolSchema.Array(ToolSchema.String("Column."))))
                    .NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        });

        Assert.True(catalog.TryGetEntry("system_tls_posture", out var entry));
        Assert.True(entry.SupportsTargetScoping);
        Assert.Equal(new[] { "search_base_dn", "computer_name" }, entry.TargetScopeArguments);
        Assert.True(entry.SupportsRemoteHostTargeting);
        Assert.Equal(new[] { "computer_name" }, entry.RemoteHostArguments);
    }

    [Fact]
    public void Build_ProjectsEventLogRemoteMachineTraits_FromMachineNameArguments() {
        var catalog = ToolOrchestrationCatalog.Build(new[] {
            new ToolDefinition(
                "eventlog_live_query",
                "Inspect live event logs on local or remote machines.",
                ToolSchema.Object(
                        ("machine_name", ToolSchema.String("Remote machine.")),
                        ("machine_names", ToolSchema.Array(ToolSchema.String("Remote machines."))),
                        ("channel", ToolSchema.String("Event log channel.")))
                    .NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        });

        Assert.True(catalog.TryGetEntry("eventlog_live_query", out var entry));
        Assert.True(entry.SupportsTargetScoping);
        Assert.Equal(new[] { "channel", "machine_name", "machine_names" }, entry.TargetScopeArguments);
        Assert.True(entry.SupportsRemoteHostTargeting);
        Assert.Equal(new[] { "machine_name", "machine_names" }, entry.RemoteHostArguments);
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

    private sealed class SyntheticCatalogPack : IToolPack, IToolPackCatalogProvider {
        private readonly IReadOnlyList<ToolPackToolCatalogEntryModel> _catalog;

        public SyntheticCatalogPack(string id, params ToolPackToolCatalogEntryModel[] catalog) {
            Descriptor = new ToolPackDescriptor {
                Id = id,
                Name = id,
                Tier = ToolCapabilityTier.ReadOnly,
                IsDangerous = false,
                SourceKind = "builtin"
            };
            _catalog = catalog;
        }

        public ToolPackDescriptor Descriptor { get; }

        public IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog() {
            return _catalog;
        }

        public void Register(ToolRegistry registry) {
        }
    }
}
