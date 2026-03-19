using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class ToolContractValidationTests {

    [Fact]
    public void ToolRecoveryContract_Validate_ShouldRequirePositiveRetryAttempts_WhenRetryEnabled() {
        var contract = new ToolRecoveryContract {
            IsRecoveryAware = true,
            SupportsTransientRetry = true,
            MaxRetryAttempts = 0
        };

        var ex = Assert.Throws<InvalidOperationException>(contract.Validate);
        Assert.Contains("MaxRetryAttempts", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolSetupContract_Validate_ShouldRequireAtLeastOneSetupSignal_WhenSetupAware() {
        var contract = new ToolSetupContract {
            IsSetupAware = true
        };

        var ex = Assert.Throws<InvalidOperationException>(contract.Validate);
        Assert.Contains("setup-aware", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolHandoffContract_Validate_ShouldRejectRouteWithoutTarget() {
        var contract = new ToolHandoffContract {
            IsHandoffAware = true,
            OutboundRoutes = new[] {
                new ToolHandoffRoute {
                    TargetPackId = "dnsclientx",
                    Bindings = new[] {
                        new ToolHandoffBinding {
                            SourceField = "domain_name",
                            TargetArgument = "target"
                        }
                    }
                }
            }
        };

        var ex = Assert.Throws<InvalidOperationException>(contract.Validate);
        Assert.Contains("TargetToolName", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolHandoffContract_Validate_ShouldRejectRouteConditionWithoutExpectedValue() {
        var contract = new ToolHandoffContract {
            IsHandoffAware = true,
            OutboundRoutes = new[] {
                new ToolHandoffRoute {
                    TargetPackId = "system",
                    TargetToolName = "system_info",
                    Bindings = new[] {
                        new ToolHandoffBinding {
                            SourceField = "computer_name",
                            TargetArgument = "computer_name"
                        }
                    },
                    Conditions = new[] {
                        new ToolHandoffCondition {
                            SourceField = "probe_kind"
                        }
                    }
                }
            }
        };

        var ex = Assert.Throws<InvalidOperationException>(contract.Validate);
        Assert.Contains("ExpectedValue", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Register_ShouldRejectHandoffAwareTool_WhenSourcePackIdIsMissing() {
        var registry = new ToolRegistry();
        var definition = new ToolDefinition(
            name: "custom_probe",
            description: "Probe",
            parameters: BuildSingleStringPropertySchema("domain_name"),
            handoff: new ToolHandoffContract {
                IsHandoffAware = true,
                OutboundRoutes = new[] {
                    new ToolHandoffRoute {
                        TargetPackId = "dnsclientx",
                        TargetRole = ToolRoutingTaxonomy.RoleOperational,
                        Bindings = new[] {
                            new ToolHandoffBinding {
                                SourceField = "domain_name",
                                TargetArgument = "target"
                            }
                        }
                    }
                }
            });

        var ex = Assert.Throws<InvalidOperationException>(() => registry.Register(new StubTool(definition)));
        Assert.Contains("Routing.PackId", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Register_ShouldAcceptToolWithValidSetupHandoffAndRecoveryContracts() {
        var registry = new ToolRegistry();
        var definition = new ToolDefinition(
            name: "domaindetective_handoff_prepare",
            description: "Prepare handoff payload",
            parameters: BuildSingleStringPropertySchema("domain_name"),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                PackId = "domaindetective",
                Role = ToolRoutingTaxonomy.RoleOperational
            },
            setup: new ToolSetupContract {
                IsSetupAware = true,
                Requirements = new[] {
                    new ToolSetupRequirement {
                        RequirementId = "dns_resolver",
                        Kind = ToolSetupRequirementKinds.Connectivity,
                        IsRequired = true
                    }
                },
                SetupHintKeys = new[] { "resolver_ip" }
            },
            handoff: new ToolHandoffContract {
                IsHandoffAware = true,
                OutboundRoutes = new[] {
                    new ToolHandoffRoute {
                        TargetPackId = "active_directory",
                        TargetRole = ToolRoutingTaxonomy.RoleOperational,
                        Bindings = new[] {
                            new ToolHandoffBinding {
                                SourceField = "domain_name",
                                TargetArgument = "domain_name"
                            }
                        }
                    }
                }
            },
            recovery: new ToolRecoveryContract {
                IsRecoveryAware = true,
                SupportsTransientRetry = true,
                MaxRetryAttempts = 2,
                SupportsAlternateEngines = true,
                AlternateEngineIds = new[] { "wmi" },
                RetryableErrorCodes = new[] { "rpc_timeout" }
            });

        registry.Register(new StubTool(definition));

        Assert.True(registry.TryGetDefinition("domaindetective_handoff_prepare", out var registered));
        Assert.NotNull(registered);
        Assert.NotNull(registered!.Setup);
        Assert.NotNull(registered.Handoff);
        Assert.NotNull(registered.Recovery);
    }

    [Fact]
    public void Register_ShouldRejectInferredRoutingMetadata_WhenExplicitRoutingIsRequired() {
        var registry = new ToolRegistry {
            RequireExplicitRoutingMetadata = true
        };
        var definition = new ToolDefinition(
            name: "system_info_probe",
            description: "Probe",
            parameters: BuildSingleStringPropertySchema("computer_name"),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceInferred,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RoleOperational
            });

        var ex = Assert.Throws<InvalidOperationException>(() => registry.Register(new StubTool(definition)));
        Assert.Contains("explicit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Register_ShouldAcceptExplicitRoutingMetadata_WhenExplicitRoutingIsRequired() {
        var registry = new ToolRegistry {
            RequireExplicitRoutingMetadata = true
        };
        var definition = new ToolDefinition(
            name: "system_info_probe_explicit",
            description: "Probe",
            parameters: BuildSingleStringPropertySchema("computer_name"),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RoleOperational
            });

        registry.Register(new StubTool(definition));

        Assert.True(registry.TryGetDefinition("system_info_probe_explicit", out var registered));
        Assert.NotNull(registered);
        Assert.Equal(ToolRoutingTaxonomy.SourceExplicit, registered!.Routing!.RoutingSource, ignoreCase: true);
    }

    [Fact]
    public void Register_ShouldSupportSyntheticPackWithoutHardcodedMappings() {
        var registry = new ToolRegistry {
            RequireExplicitRoutingMetadata = true
        };
        var definition = new ToolDefinition(
            name: "synthetic_sample_inventory",
            description: "Synthetic sample inventory",
            parameters: BuildSingleStringPropertySchema("target"),
            tags: new[] { "pack:sample_pack_v2", "domain_family:corp_internal" },
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "sample_pack_v2",
                Role = ToolRoutingTaxonomy.RoleOperational,
                DomainIntentFamily = "corp_internal",
                DomainIntentActionId = "act_domain_scope_corp_internal"
            });

        registry.Register(new StubTool(definition));

        Assert.True(registry.TryGetDefinition("synthetic_sample_inventory", out var registered));
        Assert.NotNull(registered);
        Assert.Equal("sample_pack_v2", registered!.Routing!.PackId, ignoreCase: true);
        Assert.True(ToolSelectionMetadata.TryResolvePackId(registered, out var packId));
        Assert.Equal("sample_pack_v2", packId, ignoreCase: true);
        Assert.True(ToolSelectionMetadata.TryResolveDomainIntentFamily(registered, out var family));
        Assert.Equal("corp_internal", family, ignoreCase: true);
    }

    private static JsonObject BuildSingleStringPropertySchema(string propertyName) {
        return new JsonObject()
            .Add("type", "object")
            .Add(
                "properties",
                new JsonObject().Add(
                    propertyName,
                    new JsonObject().Add("type", "string")))
            .Add("additionalProperties", false);
    }

    private sealed class StubTool : ITool {
        public StubTool(ToolDefinition definition) {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            _ = arguments;
            _ = cancellationToken;
            return Task.FromResult("{}");
        }
    }
}
