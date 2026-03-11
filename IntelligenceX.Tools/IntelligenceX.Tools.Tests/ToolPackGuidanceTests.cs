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
        Assert.NotNull(item.Orchestration);
        Assert.Equal("customx", item.Orchestration.PackId);
        Assert.Equal(ToolRoutingTaxonomy.RolePackInfo, item.Orchestration.Role);
        Assert.Equal(ToolRoutingTaxonomy.SourceExplicit, item.Orchestration.RoutingSource);
        Assert.True(item.Orchestration.IsRoutingAware);
        Assert.Equal(ToolSelectionMetadata.DomainIntentFamilyAd, item.Orchestration.DomainIntentFamily);
        Assert.Equal("act_custom_scope", item.Orchestration.DomainIntentActionId);
        Assert.True(item.Orchestration.IsSetupAware);
        Assert.Equal(1, item.Orchestration.SetupRequirementCount);
        Assert.Equal(ToolSetupContract.DefaultContractId, item.Orchestration.SetupContractId);
        Assert.Equal("custom_setup", item.Orchestration.SetupToolName);
        Assert.Equal(new[] { "auth.session" }, item.Orchestration.SetupRequirementIds);
        Assert.Equal(new[] { ToolSetupRequirementKinds.Authentication }, item.Orchestration.SetupRequirementKinds);
        Assert.Equal(new[] { "auth_required", "needs_auth" }, item.Orchestration.SetupHintKeys);
        Assert.True(item.Orchestration.IsHandoffAware);
        Assert.Equal(1, item.Orchestration.HandoffRouteCount);
        Assert.Equal(1, item.Orchestration.HandoffBindingCount);
        Assert.Equal(ToolHandoffContract.DefaultContractId, item.Orchestration.HandoffContractId);
        var handoffEdge = Assert.Single(item.Orchestration.HandoffEdges);
        Assert.Equal("dnsclientx", handoffEdge.TargetPackId);
        Assert.Equal("dns_lookup", handoffEdge.TargetToolName);
        Assert.Equal(ToolRoutingTaxonomy.RoleOperational, handoffEdge.TargetRole);
        Assert.Equal(1, handoffEdge.BindingCount);
        Assert.Equal(new[] { "host->target" }, handoffEdge.BindingPairs);
        Assert.True(item.Orchestration.IsRecoveryAware);
        Assert.True(item.Orchestration.SupportsTransientRetry);
        Assert.Equal(3, item.Orchestration.MaxRetryAttempts);
        Assert.Equal(ToolRecoveryContract.DefaultContractId, item.Orchestration.RecoveryContractId);
        Assert.True(item.Orchestration.SupportsAlternateEngines);
        Assert.Equal(2, item.Orchestration.AlternateEngineCount);
        Assert.Equal(2, item.Orchestration.RecoveryToolCount);
        Assert.Equal(new[] { "query_failed", "timeout" }, item.Orchestration.RetryableErrorCodes);
        Assert.Equal(new[] { "cim", "wmi" }, item.Orchestration.AlternateEngineIds);
        Assert.Equal(new[] { "custom_discover_scope", "custom_pack_info" }, item.Orchestration.RecoveryToolNames);
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
