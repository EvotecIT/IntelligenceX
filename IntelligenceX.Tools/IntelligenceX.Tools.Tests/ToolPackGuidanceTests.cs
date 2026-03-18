using IntelligenceX.Tools.Common;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class ToolPackGuidanceTests {
    [Fact]
    public void Create_ShouldNormalizeAndDeduplicateToolNames() {
        var model = ToolPackGuidance.Create(
            pack: " system ",
            engine: " ComputerX ",
            tools: new[] { "system_info", " system_info ", "SYSTEM_INFO", "system_pack_info" });

        Assert.Equal("system", model.Pack);
        Assert.Equal("ComputerX", model.Engine);
        Assert.Equal(2, model.Tools.Count);
        Assert.Contains("system_info", model.Tools);
        Assert.Contains("system_pack_info", model.Tools);
    }

    [Fact]
    public void Create_ShouldApplyDefaultOutputContractValues() {
        var model = ToolPackGuidance.Create(
            pack: "eventlog",
            engine: "EventViewerX",
            tools: new[] { "eventlog_pack_info" });

        Assert.Equal(1, model.GuidanceVersion);
        Assert.NotNull(model.OutputContract);
        Assert.Equal("_view", model.OutputContract.ViewFieldSuffix);
        Assert.Equal("Projection arguments are optional and view-only.", model.OutputContract.ViewProjectionPolicy);
        Assert.Contains("raw payload", model.OutputContract.RawPayloadPolicy);
    }

    [Fact]
    public void FlowStep_And_Capability_ShouldNormalizeToolCollections() {
        var step = ToolPackGuidance.FlowStep(
            goal: " Discover ",
            suggestedTools: new[] { "a", "A", " b ", " " },
            notes: " note ");
        var capability = ToolPackGuidance.Capability(
            id: " discovery ",
            summary: " summary ",
            primaryTools: new[] { "x", "X", " y " },
            notes: " details ");

        Assert.Equal("Discover", step.Goal);
        Assert.Equal(2, step.SuggestedTools.Count);
        Assert.Contains("a", step.SuggestedTools);
        Assert.Contains("b", step.SuggestedTools);
        Assert.Equal("note", step.Notes);

        Assert.Equal("discovery", capability.Id);
        Assert.Equal("summary", capability.Summary);
        Assert.Equal(2, capability.PrimaryTools.Count);
        Assert.Contains("x", capability.PrimaryTools);
        Assert.Contains("y", capability.PrimaryTools);
        Assert.Equal("details", capability.Notes);
    }

    [Fact]
    public void Create_ShouldExposeStructuredFlowAndCapabilities() {
        var model = ToolPackGuidance.Create(
            pack: "system",
            engine: "ComputerX",
            tools: new[] { "system_info", "system_pack_info" },
            recommendedFlow: new[] { "step one", "step two" },
            flowSteps: new[] {
                ToolPackGuidance.FlowStep("Collect baseline", new[] { "system_info" })
            },
            capabilities: new[] {
                ToolPackGuidance.Capability("host_baseline", "Baseline host inventory", new[] { "system_info" })
            });

        Assert.Single(model.RecommendedFlowSteps);
        Assert.Equal("Collect baseline", model.RecommendedFlowSteps[0].Goal);
        Assert.Single(model.Capabilities);
        Assert.Equal("host_baseline", model.Capabilities[0].Id);
        Assert.Equal("Baseline host inventory", model.Capabilities[0].Summary);
        Assert.Equal(2, model.RecommendedFlow.Count);
        Assert.NotNull(model.AutonomySummary);
        Assert.Equal(2, model.AutonomySummary.TotalTools);
    }

    [Fact]
    public void EntityHandoff_ShouldNormalizeFieldsAndTools() {
        var handoff = ToolPackGuidance.EntityHandoff(
            id: " identity_bridge ",
            summary: " bridge summary ",
            entityKinds: new[] { "user", "USER", " computer " },
            sourceTools: new[] { " eventlog_named_events_query ", "EVENTLOG_NAMED_EVENTS_QUERY", "eventlog_timeline_query" },
            targetTools: new[] { "ad_search", "AD_SEARCH", " ad_object_resolve " },
            fieldMappings: new[] {
                ToolPackGuidance.EntityFieldMapping(" events[].who ", " identity ", " trim "),
                ToolPackGuidance.EntityFieldMapping("events[].who", "identity"),
                ToolPackGuidance.EntityFieldMapping("timeline[].computer", "identities")
            },
            notes: " note ");

        Assert.Equal("identity_bridge", handoff.Id);
        Assert.Equal("bridge summary", handoff.Summary);
        Assert.Equal(new[] { "user", "computer" }, handoff.EntityKinds);
        Assert.Equal(new[] { "eventlog_named_events_query", "eventlog_timeline_query" }, handoff.SourceTools);
        Assert.Equal(new[] { "ad_search", "ad_object_resolve" }, handoff.TargetTools);
        Assert.Equal(2, handoff.FieldMappings.Count);
        Assert.Equal("events[].who", handoff.FieldMappings[0].SourceField);
        Assert.Equal("identity", handoff.FieldMappings[0].TargetArgument);
        Assert.Equal("trim", handoff.FieldMappings[0].Normalization);
        Assert.Equal("note", handoff.Notes);
    }

    [Fact]
    public void Create_ShouldExposeEntityHandoffs() {
        var model = ToolPackGuidance.Create(
            pack: "eventlog",
            engine: "EventViewerX",
            tools: new[] { "eventlog_pack_info" },
            entityHandoffs: new[] {
                ToolPackGuidance.EntityHandoff(
                    id: "identity_to_ad",
                    summary: "Forward identities to AD tools.",
                    sourceTools: new[] { "eventlog_named_events_query" },
                    targetTools: new[] { "ad_object_resolve" },
                    fieldMappings: new[] {
                        ToolPackGuidance.EntityFieldMapping("events[].who", "identities")
                    })
            });

        var handoff = Assert.Single(model.EntityHandoffs);
        Assert.Equal("identity_to_ad", handoff.Id);
        Assert.Equal("Forward identities to AD tools.", handoff.Summary);
        Assert.Equal(new[] { "eventlog_named_events_query" }, handoff.SourceTools);
        Assert.Equal(new[] { "ad_object_resolve" }, handoff.TargetTools);
        Assert.Single(handoff.FieldMappings);
    }

    [Fact]
    public void CatalogFromTools_ShouldExposeRequiredArgsAndProjectionSupport() {
        var catalog = ToolPackGuidance.CatalogFromTools(new ITool[] {
            new StubTool(new ToolDefinition(
                "stub_a",
                "Tool A",
                ToolSchema.Object(
                        ("query", ToolSchema.String()))
                    .Required("query")
                    .NoAdditionalProperties())),
            new StubTool(new ToolDefinition(
                "stub_b",
                "Tool B",
                ToolSchema.Object(
                        ("columns", ToolSchema.Array(ToolSchema.String())),
                        ("sort_by", ToolSchema.String()),
                        ("cursor", ToolSchema.String()),
                        ("page_size", ToolSchema.Integer()),
                        ("start_time_utc", ToolSchema.String()),
                        ("end_time_utc", ToolSchema.String()),
                        ("attributes", ToolSchema.Array(ToolSchema.String())),
                        ("domain_controller", ToolSchema.String()),
                        ("send", ToolSchema.Boolean()))
                    .WithAuthenticationProfileReference()
                    .WithWriteGovernanceMetadata()
                    .NoAdditionalProperties(),
                writeGovernance: new ToolWriteGovernanceContract {
                    IsWriteCapable = true,
                    RequiresGovernanceAuthorization = true,
                    GovernanceContractId = ToolWriteGovernanceContract.DefaultContractId,
                    IntentMode = ToolWriteIntentMode.BooleanFlagTrue,
                    IntentArgumentName = "send",
                    RequireExplicitConfirmation = true,
                    ConfirmationArgumentName = "send"
                },
                authentication: ToolAuthenticationConventions.ProfileReference()))
        });

        Assert.Equal(2, catalog.Count);

        var a = catalog[0];
        Assert.Equal("stub_a", a.Name);
        Assert.Equal("Tool A", a.Description);
        Assert.NotNull(a.Routing);
        Assert.Equal(ToolRoutingTaxonomy.SourceInferred, a.Routing.Source);
        Assert.False(string.IsNullOrWhiteSpace(a.Routing.Scope));
        Assert.False(string.IsNullOrWhiteSpace(a.Routing.Operation));
        Assert.False(string.IsNullOrWhiteSpace(a.Routing.Entity));
        Assert.False(string.IsNullOrWhiteSpace(a.Routing.Risk));
        Assert.Single(a.RequiredArguments);
        Assert.Contains("query", a.RequiredArguments);
        Assert.False(a.SupportsTableViewProjection);
        Assert.Single(a.Arguments);
        Assert.Equal("query", a.Arguments[0].Name);
        Assert.Equal("string", a.Arguments[0].Type);
        Assert.True(a.Arguments[0].Required);
        Assert.NotNull(a.Traits);
        Assert.Equal("local_only", a.Traits.ExecutionScope);
        Assert.False(a.Traits.SupportsTableViewProjection);
        Assert.False(a.Traits.SupportsPaging);
        Assert.False(a.Traits.SupportsTimeRange);
        Assert.False(a.Traits.SupportsDynamicAttributes);
        Assert.False(a.Traits.SupportsTargetScoping);
        Assert.False(a.Traits.SupportsRemoteHostTargeting);
        Assert.False(a.Traits.SupportsMutatingActions);
        Assert.False(a.Traits.SupportsWriteGovernanceMetadata);
        Assert.Empty(a.Traits.WriteGovernanceMetadataArguments);
        Assert.False(a.Traits.SupportsAuthentication);
        Assert.Empty(a.Traits.AuthenticationArguments);
        Assert.Empty(a.Traits.RemoteHostArguments);
        Assert.False(a.IsWriteCapable);
        Assert.False(a.RequiresWriteGovernance);
        Assert.Null(a.WriteGovernanceContractId);
        Assert.False(a.IsAuthenticationAware);
        Assert.False(a.RequiresAuthentication);
        Assert.Null(a.AuthenticationContractId);
        Assert.Null(a.AuthenticationMode);
        Assert.Empty(a.AuthenticationArguments);
        Assert.False(a.SupportsConnectivityProbe);
        Assert.Null(a.ProbeToolName);

        var b = catalog[1];
        Assert.Equal("stub_b", b.Name);
        Assert.True(b.RequiredArguments.Count == 0);
        Assert.True(b.SupportsTableViewProjection);
        Assert.Equal(17, b.Arguments.Count);
        Assert.Contains(b.Arguments, static arg => arg.Name == "columns" && arg.Type == "array<string>" && !arg.Required);
        Assert.Contains(b.Arguments, static arg => arg.Name == "sort_by" && arg.Type == "string" && !arg.Required);
        Assert.Contains(b.Arguments, static arg => arg.Name == ToolWriteGovernanceArgumentNames.OperationId && arg.Type == "string" && !arg.Required);
        Assert.NotNull(b.Traits);
        Assert.Equal("local_or_remote", b.Traits.ExecutionScope);
        Assert.True(b.Traits.SupportsTableViewProjection);
        Assert.Equal(new[] { "columns", "sort_by" }, b.Traits.TableViewArguments);
        Assert.True(b.Traits.SupportsPaging);
        Assert.Equal(new[] { "cursor", "page_size" }, b.Traits.PagingArguments);
        Assert.True(b.Traits.SupportsTimeRange);
        Assert.Equal(new[] { "start_time_utc", "end_time_utc" }, b.Traits.TimeRangeArguments);
        Assert.True(b.Traits.SupportsDynamicAttributes);
        Assert.Equal(new[] { "attributes" }, b.Traits.DynamicAttributeArguments);
        Assert.True(b.Traits.SupportsTargetScoping);
        Assert.Equal(new[] { "domain_controller" }, b.Traits.TargetScopeArguments);
        Assert.True(b.Traits.SupportsRemoteHostTargeting);
        Assert.Equal(new[] { "domain_controller" }, b.Traits.RemoteHostArguments);
        Assert.True(b.Traits.SupportsMutatingActions);
        Assert.Equal(new[] { "send" }, b.Traits.MutatingActionArguments);
        Assert.True(b.Traits.SupportsWriteGovernanceMetadata);
        Assert.Equal(ToolWriteGovernanceArgumentNames.CanonicalSchemaMetadataArguments, b.Traits.WriteGovernanceMetadataArguments);
        Assert.True(b.Traits.SupportsAuthentication);
        Assert.Equal(new[] { ToolAuthenticationArgumentNames.ProfileId }, b.Traits.AuthenticationArguments);
        Assert.True(b.IsWriteCapable);
        Assert.True(b.RequiresWriteGovernance);
        Assert.Equal(ToolWriteGovernanceContract.DefaultContractId, b.WriteGovernanceContractId);
        Assert.True(b.IsAuthenticationAware);
        Assert.True(b.RequiresAuthentication);
        Assert.Equal(ToolAuthenticationContract.DefaultContractId, b.AuthenticationContractId);
        Assert.Equal("profile_reference", b.AuthenticationMode);
        Assert.Equal(new[] { ToolAuthenticationArgumentNames.ProfileId }, b.AuthenticationArguments);
        Assert.False(b.SupportsConnectivityProbe);
        Assert.Null(b.ProbeToolName);
    }

    [Fact]
    public void CatalogFromTools_ShouldExposeConnectivityProbeMetadata() {
        var catalog = ToolPackGuidance.CatalogFromTools(new ITool[] {
            new StubTool(new ToolDefinition(
                "smtp_send",
                "SMTP send",
                ToolSchema.Object(("send", ToolSchema.Boolean())).NoAdditionalProperties(),
                authentication: ToolAuthenticationConventions.HostManaged(
                    requiresAuthentication: true,
                    supportsConnectivityProbe: true,
                    probeToolName: "email_smtp_probe")))
        });

        var item = Assert.Single(catalog);
        Assert.True(item.IsAuthenticationAware);
        Assert.True(item.RequiresAuthentication);
        Assert.True(item.SupportsConnectivityProbe);
        Assert.Equal("email_smtp_probe", item.ProbeToolName);
    }

    [Fact]
    public void CatalogFromTools_ShouldProjectOrchestrationContracts() {
        var catalog = ToolPackGuidance.CatalogFromTools(new ITool[] {
            new StubTool(new ToolDefinition(
                "custom_pack_info",
                "Custom pack info",
                ToolSchema.Object(("machine_name", ToolSchema.String())).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "CustomX",
                    Role = ToolRoutingTaxonomy.RolePackInfo,
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = "Act_Custom_Scope"
                },
                setup: new ToolSetupContract {
                    IsSetupAware = true,
                    SetupToolName = "custom_setup",
                    SetupHintKeys = new[] { "needs_auth" },
                    Requirements = new[] {
                        new ToolSetupRequirement {
                            RequirementId = "auth.session",
                            Kind = ToolSetupRequirementKinds.Authentication,
                            HintKeys = new[] { "auth_required" }
                        }
                    }
                },
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "DnsClientX",
                            TargetToolName = "dns_lookup",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            Bindings = new[] {
                                new ToolHandoffBinding { SourceField = "Host", TargetArgument = "Target" }
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
                }))
        });

        var item = Assert.Single(catalog);
        Assert.Equal("custom_pack_info", item.Name);
        Assert.True(item.IsPackInfoTool);
        Assert.False(item.IsEnvironmentDiscoverTool);
        Assert.Equal("customx", item.Routing.PackId);
        Assert.Equal(ToolRoutingTaxonomy.RolePackInfo, item.Routing.Role);
        Assert.Equal(ToolSelectionMetadata.DomainIntentFamilyAd, item.Routing.DomainIntentFamily);
        Assert.Equal("Act_Custom_Scope", item.Routing.DomainIntentActionId);
        Assert.Equal("local_or_remote", item.Traits.ExecutionScope);
        Assert.Contains("machine_name", item.Traits.RemoteHostArguments, StringComparer.OrdinalIgnoreCase);
        Assert.True(item.Setup.IsSetupAware);
        Assert.Equal("custom_setup", item.Setup.SetupToolName);
        Assert.Equal(new[] { "auth.session" }, item.Setup.RequirementIds);
        Assert.Equal(
            new[] { "auth_required", "needs_auth" },
            item.Setup.HintKeys.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));
        Assert.True(item.Handoff.IsHandoffAware);
        var handoffEdge = Assert.Single(item.Handoff.Routes);
        Assert.Equal("DnsClientX", handoffEdge.TargetPackId);
        Assert.Equal("dns_lookup", handoffEdge.TargetToolName);
        Assert.Equal(ToolRoutingTaxonomy.RoleOperational, handoffEdge.TargetRole);
        Assert.Equal(new[] { "host->target" }, handoffEdge.BindingPairs.Select(static value => value.ToLowerInvariant()));
        Assert.True(item.Recovery.IsRecoveryAware);
        Assert.True(item.Recovery.SupportsTransientRetry);
        Assert.Equal(3, item.Recovery.MaxRetryAttempts);
        Assert.Equal(
            new[] { "query_failed", "timeout" },
            item.Recovery.RetryableErrorCodes.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(new[] { "custom_discover_scope", "custom_pack_info" }, item.Recovery.RecoveryToolNames);
    }

    [Fact]
    public void CatalogFromTools_ShouldFlagEnvironmentDiscoverTools_FromExplicitRoutingRole() {
        var catalog = ToolPackGuidance.CatalogFromTools(new ITool[] {
            new StubTool(new ToolDefinition(
                "custom_scope_probe",
                "Custom environment discovery",
                ToolSchema.Object().NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "CustomX",
                    Role = ToolRoutingTaxonomy.RoleEnvironmentDiscover
                }))
        });

        var item = Assert.Single(catalog);
        Assert.False(item.IsPackInfoTool);
        Assert.True(item.IsEnvironmentDiscoverTool);
        Assert.Equal(ToolRoutingTaxonomy.RoleEnvironmentDiscover, item.Routing.Role);
    }

    [Fact]
    public void CatalogFromTools_ShouldRecognizeEventLogRemoteMachineArguments_AsRemoteHostTraits() {
        var catalog = ToolPackGuidance.CatalogFromTools(new ITool[] {
            new StubTool(new ToolDefinition(
                "eventlog_live_query",
                "Live Event Log query",
                ToolSchema.Object(
                        ("machine_name", ToolSchema.String("Remote machine.")),
                        ("machine_names", ToolSchema.Array(ToolSchema.String("Remote machines."))),
                        ("channel", ToolSchema.String("Event log channel.")))
                    .NoAdditionalProperties(),
                category: "eventlog"))
        });

        var item = Assert.Single(catalog);
        Assert.True(item.Traits.SupportsTargetScoping);
        Assert.Equal(new[] { "channel", "machine_name", "machine_names" }, item.Traits.TargetScopeArguments);
        Assert.True(item.Traits.SupportsRemoteHostTargeting);
        Assert.Equal(new[] { "machine_name", "machine_names" }, item.Traits.RemoteHostArguments);
    }

    [Fact]
    public void CatalogFromTools_ShouldRecognizeAdDomainAndForestArguments_AsTargetScopeTraits() {
        var catalog = ToolPackGuidance.CatalogFromTools(new ITool[] {
            new StubTool(new ToolDefinition(
                "ad_scope_discovery",
                "AD scope discovery",
                ToolSchema.Object(
                        ("domain_name", ToolSchema.String("Domain.")),
                        ("forest_name", ToolSchema.String("Forest.")),
                        ("domain_controller", ToolSchema.String("Domain controller.")))
                    .NoAdditionalProperties(),
                category: "active_directory"))
        });

        var item = Assert.Single(catalog);
        Assert.True(item.Traits.SupportsTargetScoping);
        Assert.Equal(new[] { "domain_name", "forest_name", "domain_controller" }, item.Traits.TargetScopeArguments);
        Assert.True(item.Traits.SupportsRemoteHostTargeting);
        Assert.Equal(new[] { "domain_controller" }, item.Traits.RemoteHostArguments);
    }

    [Fact]
    public void CatalogFromTools_ShouldPreferExplicitExecutionContractOverSchemaInference() {
        var catalog = ToolPackGuidance.CatalogFromTools(new ITool[] {
            new StubTool(new ToolDefinition(
                "remote_eventlog_snapshot",
                "Read from a remote-only event source.",
                ToolSchema.Object(
                        ("channel", ToolSchema.String("Event log channel.")),
                        ("top", ToolSchema.Integer("Limit.")))
                    .NoAdditionalProperties(),
                execution: new ToolExecutionContract {
                    IsExecutionAware = true,
                    ExecutionScope = ToolExecutionScopes.RemoteOnly,
                    TargetScopeArguments = new[] { "channel" }
                },
                category: "eventlog"))
        });

        var item = Assert.Single(catalog);
        Assert.True(item.Traits.IsExecutionAware);
        Assert.Equal(ToolExecutionContract.DefaultContractId, item.Traits.ExecutionContractId);
        Assert.Equal(ToolExecutionScopes.RemoteOnly, item.Traits.ExecutionScope);
        Assert.False(item.Traits.SupportsLocalExecution);
        Assert.True(item.Traits.SupportsRemoteExecution);
        Assert.True(item.Traits.SupportsTargetScoping);
        Assert.Equal(new[] { "channel" }, item.Traits.TargetScopeArguments);
        Assert.False(item.Traits.SupportsRemoteHostTargeting);
        Assert.Empty(item.Traits.RemoteHostArguments);
    }

    [Fact]
    public void CatalogFromTools_ShouldInferCategoryAndSelectionTags() {
        var catalog = ToolPackGuidance.CatalogFromTools(new ITool[] {
            new StubTool(new ToolDefinition(
                "eventlog_timeline_query",
                "Timeline query",
                ToolSchema.Object(
                        ("start_time_utc", ToolSchema.String()),
                        ("end_time_utc", ToolSchema.String()),
                        ("columns", ToolSchema.Array(ToolSchema.String())),
                        ("max_results", ToolSchema.Integer()),
                        ("machine_name", ToolSchema.String()))
                    .NoAdditionalProperties()))
        });

        var item = Assert.Single(catalog);
        Assert.Equal("eventlog", item.Category);
        Assert.Contains("eventlog", item.Tags);
        Assert.Contains("time_range", item.Tags);
        Assert.Contains("table_view", item.Tags);
        Assert.Contains("paging", item.Tags);
        Assert.Contains("target_scope", item.Tags);
        Assert.Equal("host", item.Routing.Scope);
        Assert.Equal("query", item.Routing.Operation);
        Assert.Equal("event", item.Routing.Entity);
        Assert.Equal(ToolRoutingTaxonomy.RiskLow, item.Routing.Risk);
        Assert.Equal(ToolRoutingTaxonomy.SourceInferred, item.Routing.Source);
    }

    [Fact]
    public void CatalogFromTools_ShouldDefaultCategoryAndNormalizeTagsAndRoutingCase() {
        var catalog = ToolPackGuidance.CatalogFromTools(new ITool[] {
            new StubTool(new ToolDefinition(
                "custom_probe",
                "Custom probe",
                ToolSchema.Object(
                        ("machine_name", ToolSchema.String()),
                        ("max_results", ToolSchema.Integer()))
                    .NoAdditionalProperties(),
                category: "  ",
                tags: new[] { "Tag", "tag", "TAG", "MixedCase" }))
        });

        var item = Assert.Single(catalog);
        Assert.Equal("general", item.Category);
        Assert.Contains("tag", item.Tags);
        Assert.Contains("mixedcase", item.Tags);
        Assert.Equal(1, item.Tags.Count(static x => string.Equals(x, "tag", StringComparison.Ordinal)));
        Assert.All(item.Tags, static x => Assert.Equal(x.ToLowerInvariant(), x));
        Assert.Equal(item.Tags.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase), item.Tags);

        Assert.Equal("host", item.Routing.Scope);
        Assert.Equal("probe", item.Routing.Operation);
        Assert.Equal("resource", item.Routing.Entity);
        Assert.Equal(ToolRoutingTaxonomy.RiskLow, item.Routing.Risk);
        Assert.Equal(ToolRoutingTaxonomy.SourceInferred, item.Routing.Source);
    }

    [Fact]
    public void CatalogFromTools_ShouldProjectSetupHandoffRecoveryAndExpandedRemoteTraits() {
        var catalog = ToolPackGuidance.CatalogFromTools(new ITool[] {
            new StubTool(new ToolDefinition(
                "eventlog_timeline_query",
                "Query event timeline from a remote host.",
                ToolSchema.Object(
                        ("machine_name", ToolSchema.String("Remote machine.")),
                        ("channel", ToolSchema.String("Channel.")))
                    .NoAdditionalProperties(),
                setup: new ToolSetupContract {
                    IsSetupAware = true,
                    SetupToolName = "eventlog_channels_list",
                    SetupHintKeys = new[] { "machine_name", "channel" }
                },
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "system",
                            TargetToolName = "system_info",
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "machine_name",
                                    TargetArgument = "computer_name"
                                }
                            }
                        }
                    }
                },
                recovery: new ToolRecoveryContract {
                    IsRecoveryAware = true,
                    SupportsTransientRetry = true,
                    MaxRetryAttempts = 1,
                    RecoveryToolNames = new[] { "eventlog_channels_list" },
                    RetryableErrorCodes = new[] { "transport_unavailable" }
                }))
        });

        var item = Assert.Single(catalog);
        Assert.Equal("local_or_remote", item.Traits.ExecutionScope);
        Assert.True(item.Traits.SupportsRemoteHostTargeting);
        Assert.Contains("machine_name", item.Traits.RemoteHostArguments, StringComparer.OrdinalIgnoreCase);
        Assert.True(item.Traits.SupportsTargetScoping);
        Assert.Contains("machine_name", item.Traits.TargetScopeArguments, StringComparer.OrdinalIgnoreCase);

        Assert.True(item.Setup.IsSetupAware);
        Assert.Equal("eventlog_channels_list", item.Setup.SetupToolName);
        Assert.Contains("channel", item.Setup.HintKeys, StringComparer.OrdinalIgnoreCase);

        Assert.True(item.Handoff.IsHandoffAware);
        var route = Assert.Single(item.Handoff.Routes);
        Assert.Equal("system", route.TargetPackId);
        Assert.Equal("system_info", route.TargetToolName);
        Assert.Contains("machine_name->computer_name", route.BindingPairs, StringComparer.OrdinalIgnoreCase);

        Assert.True(item.Recovery.IsRecoveryAware);
        Assert.True(item.Recovery.SupportsTransientRetry);
        Assert.Equal(1, item.Recovery.MaxRetryAttempts);
        Assert.Contains("eventlog_channels_list", item.Recovery.RecoveryToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("transport_unavailable", item.Recovery.RetryableErrorCodes, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_ShouldDeriveAutonomySummaryFromToolCatalog() {
        var model = ToolPackGuidance.Create(
            pack: "eventlog",
            engine: "EventViewerX",
            tools: new[] { "eventlog_pack_info", "eventlog_timeline_query", "eventlog_channels_list" },
            toolCatalog: new[] {
                new ToolPackToolCatalogEntryModel {
                    Name = "eventlog_pack_info",
                    Description = "Pack info",
                    IsEnvironmentDiscoverTool = true
                },
                new ToolPackToolCatalogEntryModel {
                    Name = "eventlog_timeline_query",
                    Description = "Timeline",
                    Traits = new ToolPackToolTraitsModel {
                        ExecutionScope = "local_or_remote",
                        SupportsRemoteHostTargeting = true,
                        RemoteHostArguments = new[] { "machine_name" }
                    },
                    Setup = new ToolPackToolSetupModel {
                        IsSetupAware = true,
                        SetupToolName = "eventlog_channels_list"
                    },
                    Handoff = new ToolPackToolHandoffModel {
                        IsHandoffAware = true,
                        Routes = new[] {
                            new ToolPackToolHandoffRouteModel {
                                TargetPackId = "system",
                                TargetToolName = "system_info",
                                BindingPairs = new[] { "machine_name->computer_name" }
                            }
                        }
                    },
                    Recovery = new ToolPackToolRecoveryModel {
                        IsRecoveryAware = true,
                        RecoveryToolNames = new[] { "eventlog_channels_list" }
                    }
                },
                new ToolPackToolCatalogEntryModel {
                    Name = "eventlog_channels_list",
                    Description = "Channels"
                }
            });

        var summary = model.AutonomySummary;
        Assert.Equal(3, summary.TotalTools);
        Assert.Equal(1, summary.RemoteCapableTools);
        Assert.Equal(new[] { "eventlog_timeline_query" }, summary.RemoteCapableToolNames);
        Assert.Equal(1, summary.TargetScopedTools);
        Assert.Equal(new[] { "eventlog_timeline_query" }, summary.TargetScopedToolNames);
        Assert.Equal(1, summary.RemoteHostTargetingTools);
        Assert.Equal(new[] { "eventlog_timeline_query" }, summary.RemoteHostTargetingToolNames);
        Assert.Equal(1, summary.SetupAwareTools);
        Assert.Equal(new[] { "eventlog_timeline_query" }, summary.SetupAwareToolNames);
        Assert.Equal(1, summary.EnvironmentDiscoverTools);
        Assert.Equal(new[] { "eventlog_pack_info" }, summary.EnvironmentDiscoverToolNames);
        Assert.Equal(1, summary.HandoffAwareTools);
        Assert.Equal(new[] { "eventlog_timeline_query" }, summary.HandoffAwareToolNames);
        Assert.Equal(1, summary.RecoveryAwareTools);
        Assert.Equal(new[] { "eventlog_timeline_query" }, summary.RecoveryAwareToolNames);
        Assert.Equal(0, summary.WriteCapableTools);
        Assert.Empty(summary.WriteCapableToolNames);
        Assert.Equal(0, summary.AuthenticationRequiredTools);
        Assert.Empty(summary.AuthenticationRequiredToolNames);
        Assert.Equal(0, summary.ProbeCapableTools);
        Assert.Empty(summary.ProbeCapableToolNames);
        Assert.Equal(1, summary.CrossPackHandoffTools);
        Assert.Equal(new[] { "eventlog_timeline_query" }, summary.CrossPackHandoffToolNames);
        Assert.Equal(new[] { "system" }, summary.CrossPackTargetPacks);
    }

    [Fact]
    public void Create_ShouldFallbackInvalidInferredRoutingRiskToLow() {
        var model = ToolPackGuidance.Create(
            pack: "system",
            engine: "ComputerX",
            tools: new[] { "system_info" },
            toolCatalog: new[] {
                new ToolPackToolCatalogEntryModel {
                    Name = "system_info",
                    Description = "System info",
                    Routing = new ToolPackToolRoutingModel {
                        Scope = "Host",
                        Operation = "Read",
                        Entity = "Host",
                        Risk = "critical",
                        Source = ToolRoutingTaxonomy.SourceInferred
                    }
                }
            });

        var item = Assert.Single(model.ToolCatalog);
        Assert.Equal("host", item.Routing.Scope);
        Assert.Equal("read", item.Routing.Operation);
        Assert.Equal("host", item.Routing.Entity);
        Assert.Equal(ToolRoutingTaxonomy.RiskLow, item.Routing.Risk);
        Assert.Equal(ToolRoutingTaxonomy.SourceInferred, item.Routing.Source);
    }

    [Fact]
    public void Create_ShouldRejectInvalidRoutingSourceValue() {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ToolPackGuidance.Create(
                pack: "system",
                engine: "ComputerX",
                tools: new[] { "system_info" },
                toolCatalog: new[] {
                    new ToolPackToolCatalogEntryModel {
                        Name = "system_info",
                        Description = "System info",
                        Routing = new ToolPackToolRoutingModel {
                            Risk = ToolRoutingTaxonomy.RiskLow,
                            Source = "manual"
                        }
                    }
                }));

        Assert.Contains("Routing source", ex.Message);
    }

    [Fact]
    public void Create_ShouldRejectInvalidExplicitRoutingRiskValue() {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ToolPackGuidance.Create(
                pack: "system",
                engine: "ComputerX",
                tools: new[] { "system_info" },
                toolCatalog: new[] {
                    new ToolPackToolCatalogEntryModel {
                        Name = "system_info",
                        Description = "System info",
                        Routing = new ToolPackToolRoutingModel {
                            Risk = "critical",
                            Source = ToolRoutingTaxonomy.SourceExplicit
                        }
                    }
                }));

        Assert.Contains("Explicit routing risk", ex.Message);
    }

    [Fact]
    public void ToolCatalogEntry_ShouldNormalizeRouting_OnAssignment() {
        var entry = new ToolPackToolCatalogEntryModel {
            Name = "system_info",
            Description = "System info",
            Routing = new ToolPackToolRoutingModel {
                Scope = " Host ",
                Operation = " Read ",
                Entity = " Resource ",
                Risk = " ",
                Source = " "
            }
        };

        Assert.Equal("host", entry.Routing.Scope);
        Assert.Equal(ToolRoutingTaxonomy.OperationRead, entry.Routing.Operation);
        Assert.Equal(ToolRoutingTaxonomy.EntityResource, entry.Routing.Entity);
        Assert.Equal(ToolRoutingTaxonomy.RiskLow, entry.Routing.Risk);
        Assert.Equal(ToolRoutingTaxonomy.SourceInferred, entry.Routing.Source);
        Assert.Equal(string.Empty, entry.Routing.PackId);
        Assert.Equal(string.Empty, entry.Routing.Role);
        Assert.Equal(string.Empty, entry.Routing.DomainIntentFamily);
        Assert.Equal(string.Empty, entry.Routing.DomainIntentActionId);
    }

    [Fact]
    public void ToolCatalogEntry_ShouldRejectInvalidRoutingSource_OnAssignment() {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ToolPackToolCatalogEntryModel {
                Name = "system_info",
                Description = "System info",
                Routing = new ToolPackToolRoutingModel {
                    Risk = ToolRoutingTaxonomy.RiskLow,
                    Source = "manual"
                }
            });

        Assert.Contains("Routing source", ex.Message);
    }

    [Fact]
    public void ToolPackInfoModel_ShouldNormalizeToolCatalog_OnAssignment() {
        var model = new ToolPackInfoModel {
            Pack = "system",
            Engine = "ComputerX",
            Tools = new[] { "system_info" },
            ToolCatalog = new[] {
                new ToolPackToolCatalogEntryModel {
                    Name = " system_info ",
                    Description = "System info",
                    Routing = new ToolPackToolRoutingModel {
                        Scope = "host",
                        Operation = "read",
                        Entity = "resource",
                        Risk = "low",
                        Source = ToolRoutingTaxonomy.SourceInferred
                    }
                },
                new ToolPackToolCatalogEntryModel {
                    Name = "SYSTEM_INFO",
                    Description = "Duplicate",
                    Routing = new ToolPackToolRoutingModel {
                        Scope = "domain",
                        Operation = "search",
                        Entity = "directory_object",
                        Risk = "high",
                        Source = ToolRoutingTaxonomy.SourceExplicit
                    }
                }
            }
        };

        var entry = Assert.Single(model.ToolCatalog);
        Assert.Equal("system_info", entry.Name);
        Assert.Equal("System info", entry.Description);
        Assert.Equal("host", entry.Routing.Scope);
        Assert.Equal("read", entry.Routing.Operation);
        Assert.Equal("resource", entry.Routing.Entity);
        Assert.Equal(ToolRoutingTaxonomy.RiskLow, entry.Routing.Risk);
        Assert.Equal(ToolRoutingTaxonomy.SourceInferred, entry.Routing.Source);
        Assert.Equal(1, model.AutonomySummary.TotalTools);
    }

    [Fact]
    public void Create_ShouldNormalizeRepresentativeExamples_InToolCatalog() {
        var model = ToolPackGuidance.Create(
            pack: "system",
            engine: "ComputerX",
            tools: new[] { "system_info" },
            toolCatalog: new[] {
                new ToolPackToolCatalogEntryModel {
                    Name = "system_info",
                    Description = "System info",
                    RepresentativeExamples = new[] {
                        " collect host inventory ",
                        "collect host inventory",
                        "summarize cpu and memory posture"
                    }
                }
            });

        var entry = Assert.Single(model.ToolCatalog);
        Assert.Equal(
            new[] {
                "collect host inventory",
                "summarize cpu and memory posture"
            },
            entry.RepresentativeExamples);
    }

    [Fact]
    public void Create_ShouldNormalizeAndSerializeDefaultRoutingValues_WhenCatalogRoutingIsBlank() {
        var model = ToolPackGuidance.Create(
            pack: "system",
            engine: "ComputerX",
            tools: new[] { "system_info" },
            toolCatalog: new[] {
                new ToolPackToolCatalogEntryModel {
                    Name = "system_info",
                    Description = "System info",
                    Routing = new ToolPackToolRoutingModel {
                        Scope = "   ",
                        Operation = " ",
                        Entity = "\t",
                        Risk = string.Empty,
                        Source = " "
                    }
                }
            });

        var entry = Assert.Single(model.ToolCatalog);
        Assert.Equal(ToolRoutingTaxonomy.ScopeGeneral, entry.Routing.Scope);
        Assert.Equal(ToolRoutingTaxonomy.OperationRead, entry.Routing.Operation);
        Assert.Equal(ToolRoutingTaxonomy.EntityResource, entry.Routing.Entity);
        Assert.Equal(ToolRoutingTaxonomy.RiskLow, entry.Routing.Risk);
        Assert.Equal(ToolRoutingTaxonomy.SourceInferred, entry.Routing.Source);

        var root = ToolJson.ToJsonObjectSnakeCase(model);
        var toolCatalog = root.GetArray("tool_catalog");
        Assert.NotNull(toolCatalog);

        var serializedEntry = Assert.Single(toolCatalog!);
        var routing = serializedEntry.AsObject()?.GetObject("routing");
        Assert.NotNull(routing);
        Assert.Equal(ToolRoutingTaxonomy.ScopeGeneral, routing!.GetString("scope"));
        Assert.Equal(ToolRoutingTaxonomy.OperationRead, routing.GetString("operation"));
        Assert.Equal(ToolRoutingTaxonomy.EntityResource, routing.GetString("entity"));
        Assert.Equal(ToolRoutingTaxonomy.RiskLow, routing.GetString("risk"));
        Assert.Equal(ToolRoutingTaxonomy.SourceInferred, routing.GetString("source"));
    }

    private sealed class StubTool : ITool {
        public StubTool(ToolDefinition definition) {
            Definition = definition;
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return Task.FromResult("{}");
        }
    }
}
