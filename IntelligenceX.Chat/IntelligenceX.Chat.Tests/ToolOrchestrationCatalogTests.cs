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
                writeGovernance: new ToolWriteGovernanceContract {
                    IsWriteCapable = true,
                    RequiresGovernanceAuthorization = true,
                    GovernanceContractId = "ix:governance:v1"
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
        Assert.True(entry.IsWriteCapable);
        Assert.True(entry.RequiresWriteGovernance);
        Assert.Equal("ix:governance:v1", entry.WriteGovernanceContractId);

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
    public void Build_DoesNotInferPackInfoFlagFromSuffixWhenRoleIsOperational() {
        var catalog = ToolOrchestrationCatalog.Build(new[] {
            CreateDefinition(
                name: "custom_pack_info",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        });

        Assert.True(catalog.TryGetEntry("custom_pack_info", out var entry));
        Assert.Equal(ToolRoutingTaxonomy.RoleOperational, entry.Role);
        Assert.False(entry.IsPackInfoTool);
        Assert.Empty(catalog.GetByRole(ToolRoutingTaxonomy.RolePackInfo));
    }

    [Fact]
    public void Build_DoesNotInferEnvironmentDiscoverFlagFromSuffixWhenRoleIsOperational() {
        var catalog = ToolOrchestrationCatalog.Build(new[] {
            CreateDefinition(
                name: "custom_environment_discover",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        });

        Assert.True(catalog.TryGetEntry("custom_environment_discover", out var entry));
        Assert.Equal(ToolRoutingTaxonomy.RoleOperational, entry.Role);
        Assert.False(entry.IsEnvironmentDiscoverTool);
        Assert.Empty(catalog.GetByRole(ToolRoutingTaxonomy.RoleEnvironmentDiscover));
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
        Assert.Equal("local_or_remote", entry.ExecutionScope);
        Assert.True(entry.SupportsTargetScoping);
        Assert.Equal(new[] { "search_base_dn", "computer_name" }, entry.TargetScopeArguments);
        Assert.True(entry.SupportsRemoteHostTargeting);
        Assert.Equal(new[] { "computer_name" }, entry.RemoteHostArguments);
    }

    [Fact]
    public void Build_ProjectsExecutionScopeForMachineNameRemoteTools() {
        var catalog = ToolOrchestrationCatalog.Build(new[] {
            new ToolDefinition(
                "eventlog_live_query",
                "Inspect remote Event Log data.",
                ToolSchema.Object(
                        ("machine_name", ToolSchema.String("Remote machine.")),
                        ("channel", ToolSchema.String("Channel.")))
                    .NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        });

        Assert.True(catalog.TryGetEntry("eventlog_live_query", out var entry));
        Assert.Equal("local_or_remote", entry.ExecutionScope);
        Assert.True(entry.SupportsTargetScoping);
        Assert.True(entry.SupportsRemoteHostTargeting);
        Assert.Equal(new[] { "machine_name" }, entry.RemoteHostArguments);
        Assert.Equal(new[] { "channel", "machine_name" }, entry.TargetScopeArguments);
    }

    [Fact]
    public void Build_PrefersExplicitExecutionContractOverSchemaInference() {
        var catalog = ToolOrchestrationCatalog.Build(new[] {
            new ToolDefinition(
                "ad_remote_snapshot",
                "Query a remote-only AD backend snapshot.",
                ToolSchema.Object(
                        ("domain_name", ToolSchema.String("Domain.")),
                        ("columns", ToolSchema.Array(ToolSchema.String("Columns."))))
                    .NoAdditionalProperties(),
                execution: new ToolExecutionContract {
                    IsExecutionAware = true,
                    ExecutionScope = ToolExecutionScopes.RemoteOnly,
                    TargetScopeArguments = new[] { "domain_name" }
                },
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "active_directory",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        });

        Assert.True(catalog.TryGetEntry("ad_remote_snapshot", out var entry));
        Assert.True(entry.IsExecutionAware);
        Assert.Equal(ToolExecutionContract.DefaultContractId, entry.ExecutionContractId);
        Assert.Equal(ToolExecutionScopes.RemoteOnly, entry.ExecutionScope);
        Assert.False(entry.SupportsLocalExecution);
        Assert.True(entry.SupportsRemoteExecution);
        Assert.True(entry.SupportsTargetScoping);
        Assert.Equal(new[] { "domain_name" }, entry.TargetScopeArguments);
        Assert.False(entry.SupportsRemoteHostTargeting);
        Assert.Empty(entry.RemoteHostArguments);
    }

    [Fact]
    public void Build_ProjectsDomainAndForestTargetScopeTraits() {
        var catalog = ToolOrchestrationCatalog.Build(new[] {
            new ToolDefinition(
                "ad_scope_discovery",
                "Inspect Active Directory scope.",
                ToolSchema.Object(
                        ("domain_name", ToolSchema.String("Domain.")),
                        ("forest_name", ToolSchema.String("Forest.")),
                        ("domain_controller", ToolSchema.String("Domain controller.")))
                    .NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "active_directory",
                    Role = ToolRoutingTaxonomy.RoleEnvironmentDiscover
                })
        });

        Assert.True(catalog.TryGetEntry("ad_scope_discovery", out var entry));
        Assert.Equal("local_or_remote", entry.ExecutionScope);
        Assert.True(entry.SupportsTargetScoping);
        Assert.Equal(new[] { "domain_name", "forest_name", "domain_controller" }, entry.TargetScopeArguments);
        Assert.True(entry.SupportsRemoteHostTargeting);
        Assert.Equal(new[] { "domain_controller" }, entry.RemoteHostArguments);
    }

    [Fact]
    public void Build_WithPackCatalogProvider_UsesPackOwnedOverlayForAutonomyMetadata() {
        var definitions = new[] {
            CreateDefinition(
                name: "custom_probe",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };
        var baseline = ToolOrchestrationCatalog.Build(definitions);
        var overlay = ToolOrchestrationCatalog.Build(definitions, new IToolPack[] { new SyntheticCatalogOverlayPack() });

        Assert.True(baseline.TryGetEntry("custom_probe", out var baselineEntry));
        Assert.Equal("local_only", baselineEntry.ExecutionScope);
        Assert.False(baselineEntry.IsPackInfoTool);
        Assert.False(baselineEntry.IsEnvironmentDiscoverTool);
        Assert.False(baselineEntry.IsSetupAware);
        Assert.False(baselineEntry.IsHandoffAware);
        Assert.False(baselineEntry.IsRecoveryAware);

        Assert.True(overlay.TryGetEntry("custom_probe", out var overlayEntry));
        Assert.Equal("active_directory", overlayEntry.PackId);
        Assert.Equal(ToolRoutingTaxonomy.RoleEnvironmentDiscover, overlayEntry.Role);
        Assert.False(overlayEntry.IsPackInfoTool);
        Assert.True(overlayEntry.IsEnvironmentDiscoverTool);
        Assert.Equal("host", overlayEntry.Scope);
        Assert.Equal("diagnose", overlayEntry.Operation);
        Assert.Equal("host", overlayEntry.Entity);
        Assert.Equal(ToolRoutingTaxonomy.RiskMedium, overlayEntry.Risk);
        Assert.Equal(ToolRoutingTaxonomy.SourceExplicit, overlayEntry.RoutingSource);
        Assert.Equal("local_or_remote", overlayEntry.ExecutionScope);
        Assert.True(overlayEntry.SupportsTargetScoping);
        Assert.True(overlayEntry.SupportsRemoteHostTargeting);
        Assert.Equal(new[] { "computer_name" }, overlayEntry.TargetScopeArguments);
        Assert.Equal(new[] { "computer_name" }, overlayEntry.RemoteHostArguments);
        Assert.Equal(
            new[] {
                "collect a remote baseline for the current host",
                "pivot the resolved host into event evidence when deeper triage is needed"
            },
            overlayEntry.RepresentativeExamples);
        Assert.Equal(ToolSelectionMetadata.DomainIntentFamilyAd, overlayEntry.DomainIntentFamily);
        Assert.Equal(ToolSelectionMetadata.DomainIntentActionIdAd, overlayEntry.DomainIntentActionId);
        Assert.Equal("Directory operations", overlayEntry.DomainIntentFamilyDisplayName);
        Assert.Equal("directory operations", overlayEntry.DomainIntentFamilyReplyExample);
        Assert.Equal("Directory operations scope (discovery and host diagnostics)", overlayEntry.DomainIntentFamilyChoiceDescription);
        Assert.True(overlayEntry.IsSetupAware);
        Assert.Equal("custom_environment_discover", overlayEntry.SetupToolName);
        Assert.Equal(new[] { "host_access" }, overlayEntry.SetupRequirementIds);
        Assert.Equal(new[] { "computer_name" }, overlayEntry.SetupHintKeys);
        Assert.True(overlayEntry.IsHandoffAware);
        Assert.Equal(1, overlayEntry.HandoffRouteCount);
        Assert.Equal(1, overlayEntry.HandoffBindingCount);
        Assert.Equal("eventlog", overlayEntry.HandoffEdges[0].TargetPackId);
        Assert.Equal("eventlog_channels_list", overlayEntry.HandoffEdges[0].TargetToolName);
        Assert.Equal(new[] { "meta/computer_name->machine_name" }, overlayEntry.HandoffEdges[0].BindingPairs);
        Assert.True(overlayEntry.IsRecoveryAware);
        Assert.True(overlayEntry.SupportsTransientRetry);
        Assert.Equal(2, overlayEntry.MaxRetryAttempts);
        Assert.Equal(new[] { "timeout" }, overlayEntry.RetryableErrorCodes);
        Assert.Equal(new[] { "custom_environment_discover" }, overlayEntry.RecoveryToolNames);
    }

    [Fact]
    public void Build_WithOverlayFamilyChange_DoesNotLeakBaselineFamilyPresentation() {
        var definitions = new[] {
            CreateDefinition(
                name: "custom_probe",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RoleOperational,
                    DomainIntentFamily = "corp_internal",
                    DomainIntentActionId = "act_domain_scope_corp_internal",
                    DomainIntentFamilyDisplayName = "Corporate operations",
                    DomainIntentFamilyReplyExample = "corporate operations",
                    DomainIntentFamilyChoiceDescription = "Corporate operations scope (internal runtime tooling)"
                })
        };

        var overlay = ToolOrchestrationCatalog.Build(definitions, new IToolPack[] { new SyntheticCatalogOverlayWithoutPresentationPack() });

        Assert.True(overlay.TryGetEntry("custom_probe", out var overlayEntry));
        Assert.Equal(ToolSelectionMetadata.DomainIntentFamilyAd, overlayEntry.DomainIntentFamily);
        Assert.Equal(ToolSelectionMetadata.DomainIntentActionIdAd, overlayEntry.DomainIntentActionId);
        Assert.Equal(string.Empty, overlayEntry.DomainIntentFamilyDisplayName);
        Assert.Equal(string.Empty, overlayEntry.DomainIntentFamilyReplyExample);
        Assert.Equal(string.Empty, overlayEntry.DomainIntentFamilyChoiceDescription);
    }

    [Fact]
    public void Build_ProjectsPackGuidancePreferredProbeAndRecipeHints() {
        var catalog = ToolOrchestrationCatalog.Build(
            new[] {
                CreateDefinition(
                    name: "custom_connectivity_probe",
                    routing: new ToolRoutingContract {
                        IsRoutingAware = true,
                        RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                        PackId = "customx",
                        Role = ToolRoutingTaxonomy.RoleDiagnostic
                    }),
                CreateDefinition(
                    name: "custom_followup",
                    routing: new ToolRoutingContract {
                        IsRoutingAware = true,
                        RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                        PackId = "customx",
                        Role = ToolRoutingTaxonomy.RoleOperational
                    })
            },
            new IToolPack[] { new SyntheticGuidanceOverlayPack() });

        Assert.True(catalog.TryGetEntry("custom_connectivity_probe", out var probeEntry));
        Assert.True(probeEntry.IsPackPreferredProbeTool);
        Assert.Equal(120, probeEntry.PackProbeHelperFreshnessWindowSeconds);
        Assert.Equal(600, probeEntry.PackSetupHelperFreshnessWindowSeconds);
        Assert.Equal(300, probeEntry.PackRecipeHelperFreshnessWindowSeconds);
        Assert.Contains("custom_runtime_triage", probeEntry.PackRecommendedRecipeIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            probeEntry.PackRecommendedRecipeHints,
            static hint => hint.Contains("stabilize the remote endpoint before deeper follow-up", StringComparison.OrdinalIgnoreCase)
                           && hint.Contains("runtime reachability is still uncertain", StringComparison.OrdinalIgnoreCase));

        Assert.True(catalog.TryGetEntry("custom_followup", out var followupEntry));
        Assert.False(followupEntry.IsPackPreferredProbeTool);
        Assert.Contains("custom_runtime_triage", followupEntry.PackRecommendedRecipeIds, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_AllowsGuidanceToClaimUniqueToolOwnershipByName() {
        var catalog = ToolOrchestrationCatalog.Build(
            new[] {
                CreateDefinition(name: "custom_connectivity_probe"),
                CreateDefinition(name: "custom_followup")
            },
            new IToolPack[] { new SyntheticGuidanceOverlayPack() });

        Assert.True(catalog.TryGetEntry("custom_connectivity_probe", out var probeEntry));
        Assert.Equal("customx", probeEntry.PackId);
        Assert.True(probeEntry.IsPackPreferredProbeTool);
        Assert.Equal(120, probeEntry.PackProbeHelperFreshnessWindowSeconds);

        Assert.True(catalog.TryGetPackId("custom_connectivity_probe", out var packId));
        Assert.Equal("customx", packId);

        var byPack = catalog.GetByPackId("customx");
        Assert.Contains(byPack, static entry => string.Equals(entry.ToolName, "custom_connectivity_probe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(byPack, static entry => string.Equals(entry.ToolName, "custom_followup", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_UsesGuidancePackIdentityInsteadOfProviderDescriptorIdWhenTheyDiffer() {
        var catalog = ToolOrchestrationCatalog.Build(
            new[] {
                CreateDefinition(name: "custom_connectivity_probe"),
                CreateDefinition(name: "custom_followup")
            },
            new IToolPack[] { new SyntheticMismatchedDescriptorGuidancePack() });

        Assert.True(catalog.TryGetEntry("custom_connectivity_probe", out var probeEntry));
        Assert.Equal("customx", probeEntry.PackId);
        Assert.True(probeEntry.IsPackPreferredProbeTool);

        Assert.True(catalog.TryGetEntry("custom_followup", out var followupEntry));
        Assert.Equal("customx", followupEntry.PackId);
        Assert.Contains("custom_runtime_triage", followupEntry.PackRecommendedRecipeIds, StringComparer.OrdinalIgnoreCase);

        Assert.True(catalog.TryGetPackId("custom_connectivity_probe", out var packId));
        Assert.Equal("customx", packId);
        Assert.DoesNotContain(
            catalog.GetByPackId("eventlog"),
            static entry => string.Equals(entry.ToolName, "custom_connectivity_probe", StringComparison.OrdinalIgnoreCase));
    }

    private static ToolDefinition CreateDefinition(
        string name,
        ToolExecutionContract? execution = null,
        ToolRoutingContract? routing = null,
        ToolWriteGovernanceContract? writeGovernance = null,
        ToolSetupContract? setup = null,
        ToolHandoffContract? handoff = null,
        ToolRecoveryContract? recovery = null) {
        return new ToolDefinition(
            name: name,
            description: "test tool",
            execution: execution,
            routing: routing,
            writeGovernance: writeGovernance,
            setup: setup,
            handoff: handoff,
            recovery: recovery);
    }

    private sealed class SyntheticCatalogOverlayPack : IToolPack, IToolPackCatalogProvider {
        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "customx",
            Name = "CustomX",
            Tier = ToolCapabilityTier.ReadOnly,
            Description = "Synthetic catalog overlay pack.",
            SourceKind = "builtin"
        };

        public void Register(ToolRegistry registry) {
            _ = registry;
        }

        public IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog() {
            return new[] {
                new ToolPackToolCatalogEntryModel {
                    Name = "custom_probe",
                    IsEnvironmentDiscoverTool = true,
                    Routing = new ToolPackToolRoutingModel {
                        PackId = "ADPlayground",
                        Role = ToolRoutingTaxonomy.RoleEnvironmentDiscover,
                        Scope = "host",
                        Operation = "diagnose",
                        Entity = "host",
                        Risk = ToolRoutingTaxonomy.RiskMedium,
                        Source = ToolRoutingTaxonomy.SourceExplicit,
                        DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                        DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdAd,
                        DomainIntentFamilyDisplayName = "Directory operations",
                        DomainIntentFamilyReplyExample = "directory operations",
                        DomainIntentFamilyChoiceDescription = "Directory operations scope (discovery and host diagnostics)"
                    },
                    Traits = new ToolPackToolTraitsModel {
                        ExecutionScope = "local_or_remote",
                        SupportsTargetScoping = true,
                        TargetScopeArguments = new[] { "computer_name" },
                        SupportsRemoteHostTargeting = true,
                        RemoteHostArguments = new[] { "computer_name" }
                    },
                    RepresentativeExamples = new[] {
                        "collect a remote baseline for the current host",
                        "pivot the resolved host into event evidence when deeper triage is needed"
                    },
                    Setup = new ToolPackToolSetupModel {
                        IsSetupAware = true,
                        SetupToolName = "custom_environment_discover",
                        RequirementIds = new[] { "host_access" },
                        HintKeys = new[] { "computer_name" }
                    },
                    Handoff = new ToolPackToolHandoffModel {
                        IsHandoffAware = true,
                        Routes = new[] {
                            new ToolPackToolHandoffRouteModel {
                                TargetPackId = "eventlog",
                                TargetToolName = "eventlog_channels_list",
                                BindingPairs = new[] { "meta/computer_name->machine_name" }
                            }
                        }
                    },
                    Recovery = new ToolPackToolRecoveryModel {
                        IsRecoveryAware = true,
                        SupportsTransientRetry = true,
                        MaxRetryAttempts = 2,
                        RetryableErrorCodes = new[] { "timeout" },
                        RecoveryToolNames = new[] { "custom_environment_discover" }
                    }
                }
            };
        }
    }

    private sealed class SyntheticCatalogOverlayWithoutPresentationPack : IToolPack, IToolPackCatalogProvider {
        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "customx",
            Name = "CustomX",
            Tier = ToolCapabilityTier.ReadOnly,
            Description = "Synthetic catalog overlay pack without family presentation metadata.",
            SourceKind = "builtin"
        };

        public void Register(ToolRegistry registry) {
            _ = registry;
        }

        public IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog() {
            return new[] {
                new ToolPackToolCatalogEntryModel {
                    Name = "custom_probe",
                    Routing = new ToolPackToolRoutingModel {
                        PackId = "ADPlayground",
                        Role = ToolRoutingTaxonomy.RoleOperational,
                        Source = ToolRoutingTaxonomy.SourceExplicit,
                        DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                        DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdAd
                    }
                }
            };
        }
    }

    private sealed class SyntheticGuidanceOverlayPack : IToolPack, IToolPackCatalogProvider, IToolPackGuidanceProvider {
        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "customx",
            Name = "CustomX",
            Tier = ToolCapabilityTier.ReadOnly,
            Description = "Synthetic guidance overlay pack.",
            SourceKind = "builtin"
        };

        public void Register(ToolRegistry registry) {
            _ = registry;
        }

        public IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog() {
            return new[] {
                new ToolPackToolCatalogEntryModel {
                    Name = "custom_connectivity_probe",
                    Description = "Probe custom runtime reachability."
                },
                new ToolPackToolCatalogEntryModel {
                    Name = "custom_followup",
                    Description = "Collect custom follow-up evidence."
                }
            };
        }

        public ToolPackInfoModel GetPackGuidance() {
            return new ToolPackInfoModel {
                Pack = "customx",
                Engine = "CustomX",
                Tools = new[] { "custom_connectivity_probe", "custom_followup" },
                RuntimeCapabilities = new ToolPackRuntimeCapabilitiesModel {
                    PreferredProbeTools = new[] { "custom_connectivity_probe" },
                    ProbeHelperFreshnessWindowSeconds = 120,
                    SetupHelperFreshnessWindowSeconds = 600,
                    RecipeHelperFreshnessWindowSeconds = 300
                },
                RecommendedRecipes = new[] {
                    new ToolPackRecipeModel {
                        Id = "custom_runtime_triage",
                        Summary = "Stabilize the remote endpoint before deeper follow-up.",
                        WhenToUse = "Use when runtime reachability is still uncertain.",
                        Steps = new[] {
                            new ToolPackFlowStepModel {
                                Goal = "Probe first",
                                SuggestedTools = new[] { "custom_connectivity_probe" }
                            },
                            new ToolPackFlowStepModel {
                                Goal = "Follow up",
                                SuggestedTools = new[] { "custom_followup" }
                            }
                        }
                    }
                }
            };
        }
    }

    private sealed class SyntheticMismatchedDescriptorGuidancePack : IToolPack, IToolPackGuidanceProvider {
        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "eventlog",
            Name = "EventLog",
            Tier = ToolCapabilityTier.ReadOnly,
            Description = "Synthetic mismatched guidance descriptor.",
            SourceKind = "builtin"
        };

        public void Register(ToolRegistry registry) {
            _ = registry;
        }

        public ToolPackInfoModel GetPackGuidance() {
            return new ToolPackInfoModel {
                Pack = "customx",
                RuntimeCapabilities = new ToolPackRuntimeCapabilitiesModel {
                    PreferredProbeTools = new[] { "custom_connectivity_probe" }
                },
                RecommendedRecipes = new[] {
                    new ToolPackRecipeModel {
                        Id = "custom_runtime_triage",
                        Summary = "Probe the runtime before follow-up.",
                        WhenToUse = "Use when provider identity differs from pack ownership.",
                        Steps = new[] {
                            new ToolPackFlowStepModel {
                                Goal = "Probe",
                                SuggestedTools = new[] { "custom_connectivity_probe" }
                            },
                            new ToolPackFlowStepModel {
                                Goal = "Follow up",
                                SuggestedTools = new[] { "custom_followup" }
                            }
                        }
                    }
                }
            };
        }
    }
}
