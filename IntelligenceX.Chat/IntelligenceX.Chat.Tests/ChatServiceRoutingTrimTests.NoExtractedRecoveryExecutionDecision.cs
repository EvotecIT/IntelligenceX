using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {

    [Fact]
    public void ResolveNoExtractedRecoveryPrePromptExecutionDecisionForTesting_PrefersAutoPendingActionReplay() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var result = session.ResolveNoExtractedRecoveryPrePromptExecutionDecisionForTesting(
            threadId: "thread-auto-pending",
            userRequest: "go ahead",
            assistantDraft: """
                [Action]
                ix:action:v1
                id: act_scope
                title: Run domain scope discovery
                mutating: false
                request: Run domain scope discovery now.
                reply: /act act_scope
                """,
            toolDefinitions: new[] { new ToolDefinition("ad_scope_discovery", "scope") },
            mutatingToolHintsByName: new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase),
            continuationFollowUpTurn: false,
            compactFollowUpTurn: true,
            autoPendingActionReplayUsed: false,
            hostStructuredNextActionReplayUsed: false,
            priorToolCalls: 0,
            priorToolOutputs: 0);

        Assert.Equal("AutoPendingActionReplay", result.Kind);
        Assert.Equal("single_pending_action_auto_replay", result.Reason);
        Assert.Equal("act_scope", result.ActionId);
        Assert.Null(result.ToolName);
        Assert.False(result.ExpandToFullToolAvailability);
    }

    [Fact]
    public void ResolveNoExtractedRecoveryPrePromptExecutionDecisionForTesting_PrefersAutoPendingActionReplayOverCarryoverWhenBothAreEligible() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var toolDefinitions = CreateCarryoverToolDefinitions();
        var mutabilityHints = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase) {
            ["ad_environment_discover"] = false,
            ["ad_scope_discovery"] = false
        };
        session.RememberStructuredNextActionCarryoverForTesting(
            threadId: "thread-auto-over-carryover",
            toolDefinitions: toolDefinitions,
            toolCalls: new[] {
                new ToolCallDto { CallId = "call-1", Name = "ad_environment_discover", ArgumentsJson = "{}" }
            },
            toolOutputs: new[] {
                new ToolOutputDto {
                    CallId = "call-1",
                    Ok = true,
                    Output = """
                             {"ok":true,"next_actions":[{"tool":"ad_scope_discovery","mutating":false,"reason":"limited dc inventory","arguments":{"discovery_fallback":"current_forest"}}]}
                             """
                }
            },
            mutatingToolHintsByName: mutabilityHints);

        var result = session.ResolveNoExtractedRecoveryPrePromptExecutionDecisionForTesting(
            threadId: "thread-auto-over-carryover",
            userRequest: "go ahead",
            assistantDraft: """
                [Action]
                ix:action:v1
                id: act_scope
                title: Run domain scope discovery
                mutating: false
                request: Run domain scope discovery now.
                reply: /act act_scope
                """,
            toolDefinitions: toolDefinitions,
            mutatingToolHintsByName: mutabilityHints,
            continuationFollowUpTurn: false,
            compactFollowUpTurn: true,
            autoPendingActionReplayUsed: false,
            hostStructuredNextActionReplayUsed: false,
            priorToolCalls: 0,
            priorToolOutputs: 0);

        Assert.Equal("AutoPendingActionReplay", result.Kind);
        Assert.Equal("single_pending_action_auto_replay", result.Reason);
        Assert.Equal("act_scope", result.ActionId);
    }

    [Fact]
    public void ResolveNoExtractedRecoveryPrePromptExecutionDecisionForTesting_SelectsCarryoverStructuredReplayWhenAvailable() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var toolDefinitions = CreateCarryoverToolDefinitions();
        var mutabilityHints = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase) {
            ["ad_environment_discover"] = false,
            ["ad_scope_discovery"] = false
        };
        session.RememberStructuredNextActionCarryoverForTesting(
            threadId: "thread-carryover-decision",
            toolDefinitions: toolDefinitions,
            toolCalls: new[] {
                new ToolCallDto { CallId = "call-2", Name = "ad_environment_discover", ArgumentsJson = "{}" }
            },
            toolOutputs: new[] {
                new ToolOutputDto {
                    CallId = "call-2",
                    Ok = true,
                    Output = """
                             {"ok":true,"next_actions":[{"tool":"ad_scope_discovery","mutating":false,"reason":"limited dc inventory","arguments":{"discovery_fallback":"current_forest"}}]}
                             """
                }
            },
            mutatingToolHintsByName: mutabilityHints);

        var result = session.ResolveNoExtractedRecoveryPrePromptExecutionDecisionForTesting(
            threadId: "thread-carryover-decision",
            userRequest: "continue",
            assistantDraft: "I can run the next action now.",
            toolDefinitions: toolDefinitions,
            mutatingToolHintsByName: mutabilityHints,
            continuationFollowUpTurn: true,
            compactFollowUpTurn: true,
            autoPendingActionReplayUsed: true,
            hostStructuredNextActionReplayUsed: false,
            priorToolCalls: 0,
            priorToolOutputs: 0);

        Assert.Equal("CarryoverStructuredNextActionReplay", result.Kind);
        Assert.Equal("carryover_structured_next_action_readonly_autorun", result.Reason);
        Assert.Equal("ad_scope_discovery", result.ToolName);
        Assert.True(result.ExpandToFullToolAvailability);
    }

    [Fact]
    public void ResolveNoExtractedRecoveryPrePromptExecutionDecisionForTesting_PrefersCriticalBackgroundVerificationOverCarryoverReplay() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var toolDefinitions = new[] {
            new ToolDefinition("ad_environment_discover", "discover", ToolSchema.Object().NoAdditionalProperties()),
            new ToolDefinition("ad_scope_discovery", "scope", ToolSchema.Object().NoAdditionalProperties()),
            new ToolDefinition("ad_user_lifecycle", "lifecycle", ToolSchema.Object().NoAdditionalProperties()),
            new ToolDefinition("ad_object_get", "get", ToolSchema.Object().NoAdditionalProperties())
        };
        var mutabilityHints = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase) {
            ["ad_environment_discover"] = false,
            ["ad_scope_discovery"] = false,
            ["ad_user_lifecycle"] = true,
            ["ad_object_get"] = false
        };
        session.RememberStructuredNextActionCarryoverForTesting(
            threadId: "thread-carryover-vs-background",
            toolDefinitions: toolDefinitions,
            toolCalls: new[] {
                new ToolCallDto { CallId = "call-carryover", Name = "ad_environment_discover", ArgumentsJson = "{}" }
            },
            toolOutputs: new[] {
                new ToolOutputDto {
                    CallId = "call-carryover",
                    Ok = true,
                    Output = """
                             {"ok":true,"next_actions":[{"tool":"ad_scope_discovery","mutating":false,"reason":"continue scoped inventory","arguments":{"discovery_fallback":"current_forest"}}]}
                             """
                }
            },
            mutatingToolHintsByName: mutabilityHints);
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(new[] {
            new ToolDefinition(
                "ad_user_lifecycle",
                "lifecycle",
                ToolSchema.Object().NoAdditionalProperties(),
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "active_directory",
                            TargetToolName = "ad_object_get",
                            TargetRole = ToolRoutingTaxonomy.RoleResolver,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.Critical,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "distinguished_name",
                                    TargetArgument = "identity"
                                }
                            }
                        }
                    }
                }),
            toolDefinitions[3]
        }));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId: "thread-carryover-vs-background",
            toolDefinitions: new[] {
                new ToolDefinition(
                    "ad_user_lifecycle",
                    "lifecycle",
                    ToolSchema.Object().NoAdditionalProperties(),
                    handoff: new ToolHandoffContract {
                        IsHandoffAware = true,
                        OutboundRoutes = new[] {
                            new ToolHandoffRoute {
                                TargetPackId = "active_directory",
                                TargetToolName = "ad_object_get",
                                TargetRole = ToolRoutingTaxonomy.RoleResolver,
                                FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                                FollowUpPriority = ToolHandoffFollowUpPriorities.Critical,
                                Bindings = new[] {
                                    new ToolHandoffBinding {
                                        SourceField = "distinguished_name",
                                        TargetArgument = "identity"
                                    }
                                }
                            }
                        }
                    }),
                toolDefinitions[3]
            },
            toolCalls: new[] {
                new ToolCallDto {
                    CallId = "call-ad-write",
                    Name = "ad_user_lifecycle",
                    ArgumentsJson = """{"identity":"alice","operation":"disable"}"""
                }
            },
            toolOutputs: new[] {
                new ToolOutputDto {
                    CallId = "call-ad-write",
                    Ok = true,
                    Output = """{"ok":true,"distinguished_name":"CN=alice,OU=Users,DC=contoso,DC=com"}""",
                    MetaJson = """{"write_applied":true}"""
                }
            });

        var result = session.ResolveNoExtractedRecoveryPrePromptExecutionDecisionForTesting(
            threadId: "thread-carryover-vs-background",
            userRequest: "continue",
            assistantDraft: "I can keep going with the queued scope step.",
            toolDefinitions: toolDefinitions,
            mutatingToolHintsByName: mutabilityHints,
            continuationFollowUpTurn: true,
            compactFollowUpTurn: true,
            autoPendingActionReplayUsed: true,
            hostStructuredNextActionReplayUsed: false,
            priorToolCalls: 0,
            priorToolOutputs: 0);

        Assert.Equal("BackgroundWorkReadyReplay", result.Kind);
        Assert.Equal("background_work_ready_readonly_autorun", result.Reason);
        Assert.Equal("ad_object_get", result.ToolName);
    }

    [Fact]
    public void ResolveNoExtractedRecoveryPrePromptExecutionDecisionForTesting_KeepsCarryoverAheadOfLowPriorityBackgroundEnrichment() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var toolDefinitions = new[] {
            new ToolDefinition("ad_environment_discover", "discover", ToolSchema.Object().NoAdditionalProperties()),
            new ToolDefinition("ad_scope_discovery", "scope", ToolSchema.Object().NoAdditionalProperties()),
            new ToolDefinition("remote_disk_inventory", "disk", ToolSchema.Object().NoAdditionalProperties()),
            new ToolDefinition("system_info", "system", ToolSchema.Object().NoAdditionalProperties())
        };
        var mutabilityHints = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase) {
            ["ad_environment_discover"] = false,
            ["ad_scope_discovery"] = false,
            ["remote_disk_inventory"] = false,
            ["system_info"] = false
        };
        session.RememberStructuredNextActionCarryoverForTesting(
            threadId: "thread-carryover-vs-enrichment",
            toolDefinitions: toolDefinitions,
            toolCalls: new[] {
                new ToolCallDto { CallId = "call-carryover", Name = "ad_environment_discover", ArgumentsJson = "{}" }
            },
            toolOutputs: new[] {
                new ToolOutputDto {
                    CallId = "call-carryover",
                    Ok = true,
                    Output = """
                             {"ok":true,"next_actions":[{"tool":"ad_scope_discovery","mutating":false,"reason":"continue scoped inventory","arguments":{"discovery_fallback":"current_forest"}}]}
                             """
                }
            },
            mutatingToolHintsByName: mutabilityHints);
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(new[] {
            new ToolDefinition(
                "remote_disk_inventory",
                "disk",
                ToolSchema.Object().NoAdditionalProperties(),
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "system",
                            TargetToolName = "system_info",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Enrichment,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.Low,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "computer_name"
                                }
                            }
                        }
                    }
                }),
            toolDefinitions[3]
        }));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId: "thread-carryover-vs-enrichment",
            toolDefinitions: new[] {
                new ToolDefinition(
                    "remote_disk_inventory",
                    "disk",
                    ToolSchema.Object().NoAdditionalProperties(),
                    handoff: new ToolHandoffContract {
                        IsHandoffAware = true,
                        OutboundRoutes = new[] {
                            new ToolHandoffRoute {
                                TargetPackId = "system",
                                TargetToolName = "system_info",
                                TargetRole = ToolRoutingTaxonomy.RoleOperational,
                                FollowUpKind = ToolHandoffFollowUpKinds.Enrichment,
                                FollowUpPriority = ToolHandoffFollowUpPriorities.Low,
                                Bindings = new[] {
                                    new ToolHandoffBinding {
                                        SourceField = "computer_name",
                                        TargetArgument = "computer_name"
                                    }
                                }
                            }
                        }
                    }),
                toolDefinitions[3]
            },
            toolCalls: new[] {
                new ToolCallDto {
                    CallId = "call-disk",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv42.contoso.com"}"""
                }
            },
            toolOutputs: new[] {
                new ToolOutputDto {
                    CallId = "call-disk",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var result = session.ResolveNoExtractedRecoveryPrePromptExecutionDecisionForTesting(
            threadId: "thread-carryover-vs-enrichment",
            userRequest: "continue",
            assistantDraft: "I can keep going with the queued scope step.",
            toolDefinitions: toolDefinitions,
            mutatingToolHintsByName: mutabilityHints,
            continuationFollowUpTurn: true,
            compactFollowUpTurn: true,
            autoPendingActionReplayUsed: true,
            hostStructuredNextActionReplayUsed: false,
            priorToolCalls: 0,
            priorToolOutputs: 0);

        Assert.Equal("CarryoverStructuredNextActionReplay", result.Kind);
        Assert.Equal("carryover_structured_next_action_readonly_autorun", result.Reason);
        Assert.Equal("ad_scope_discovery", result.ToolName);
    }

    [Fact]
    public void ResolveNoExtractedRecoveryPrePromptExecutionDecisionForTesting_SelectsReadyBackgroundWorkReplayWhenAvailable() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var toolDefinitions = new[] {
            new ToolDefinition("remote_disk_inventory", "disk", ToolSchema.Object().NoAdditionalProperties()),
            new ToolDefinition("system_info", "system", ToolSchema.Object().NoAdditionalProperties(), routing: CreateRecoveryRoutingContract("system", ToolRoutingTaxonomy.RoleOperational))
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(new[] {
            new ToolDefinition(
                "remote_disk_inventory",
                "disk",
                ToolSchema.Object().NoAdditionalProperties(),
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "system",
                            TargetToolName = "system_info",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "computer_name"
                                }
                            }
                        }
                    }
                }),
            toolDefinitions[1]
        }));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId: "thread-background-replay",
            toolDefinitions: new[] {
                new ToolDefinition(
                    "remote_disk_inventory",
                    "disk",
                    ToolSchema.Object().NoAdditionalProperties(),
                    handoff: new ToolHandoffContract {
                        IsHandoffAware = true,
                        OutboundRoutes = new[] {
                            new ToolHandoffRoute {
                                TargetPackId = "system",
                                TargetToolName = "system_info",
                                TargetRole = ToolRoutingTaxonomy.RoleOperational,
                                Bindings = new[] {
                                    new ToolHandoffBinding {
                                        SourceField = "computer_name",
                                        TargetArgument = "computer_name"
                                    }
                                }
                            }
                        }
                    }),
                toolDefinitions[1]
            },
            toolCalls: new[] {
                new ToolCallDto {
                    CallId = "call-disk",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv4.contoso.com"}"""
                }
            },
            toolOutputs: new[] {
                new ToolOutputDto {
                    CallId = "call-disk",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var result = session.ResolveNoExtractedRecoveryPrePromptExecutionDecisionForTesting(
            threadId: "thread-background-replay",
            userRequest: "continue",
            assistantDraft: "I can keep going with the prepared follow-up.",
            toolDefinitions: toolDefinitions,
            mutatingToolHintsByName: new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase),
            continuationFollowUpTurn: true,
            compactFollowUpTurn: true,
            autoPendingActionReplayUsed: true,
            hostStructuredNextActionReplayUsed: true,
            priorToolCalls: 0,
            priorToolOutputs: 0);

        Assert.Equal("BackgroundWorkReadyReplay", result.Kind);
        Assert.Equal("background_work_ready_readonly_autorun", result.Reason);
        Assert.Equal("system_info", result.ToolName);
        Assert.True(result.ExpandToFullToolAvailability);

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting("thread-background-replay");
        var runningItem = Assert.Single(snapshot.Items);
        Assert.Equal("running", runningItem.State);
        Assert.Equal(result.ActionId, runningItem.Id);
    }

    [Fact]
    public void ResolveNoExtractedRecoveryPrePromptExecutionDecisionForTesting_PreservesBlockedBackgroundPrerequisiteReason() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-prereq-blocked";
        var toolDefinitions = new[] {
            new ToolDefinition(
                name: "seed_eventlog_live_followup",
                description: "seed live event log follow-up",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "eventlog_live_query",
                "Inspect live event logs on a remote machine after validation and setup.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_channels_list"
                },
                setup: new ToolSetupContract {
                    IsSetupAware = true,
                    SetupToolName = "eventlog_runtime_profile_validate"
                }),
            new ToolDefinition(
                "eventlog_channels_list",
                "List available event log channels and validate access for the target machine.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties()),
            new ToolDefinition(
                "eventlog_runtime_profile_validate",
                "Validate runtime profile readiness for the target machine.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(toolDefinitions));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            toolDefinitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-live-followup",
                    Name = "seed_eventlog_live_followup",
                    ArgumentsJson = """{"computer_name":"srv-eventlog.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-live-followup",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });
        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        foreach (var helperItem in snapshot.Items) {
            if (string.Equals(helperItem.TargetToolName, "eventlog_live_query", System.StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            Assert.True(session.TrySetThreadBackgroundWorkItemStateForTesting(threadId, helperItem.Id, "failed"));
        }

        var result = session.ResolveNoExtractedRecoveryPrePromptExecutionDecisionForTesting(
            threadId: threadId,
            userRequest: "continue",
            assistantDraft: "I can keep going with the prepared follow-up.",
            toolDefinitions: toolDefinitions,
            mutatingToolHintsByName: new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase),
            continuationFollowUpTurn: true,
            compactFollowUpTurn: true,
            autoPendingActionReplayUsed: true,
            hostStructuredNextActionReplayUsed: true,
            priorToolCalls: 0,
            priorToolOutputs: 0);

        Assert.Equal("None", result.Kind);
        Assert.Equal("background_work_waiting_on_prerequisites", result.Reason);
        Assert.Null(result.ToolName);
        Assert.False(result.ExpandToFullToolAvailability);
    }

    [Fact]
    public void ResolveNoExtractedRecoveryPostPromptExecutionDecisionForTesting_SelectsDomainBootstrapFromRememberedFamily() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var registry = new ToolRegistry();
        registry.Register(new RecoveryExecutionStubTool(
            "ad_pack_info",
            CreateRecoveryRoutingContract("active_directory", ToolRoutingTaxonomy.RolePackInfo),
            tags: new[] { "domain_family:ad_domain" }));
        registry.Register(new RecoveryExecutionStubTool(
            "ad_environment_discover",
            CreateRecoveryRoutingContract("active_directory", ToolRoutingTaxonomy.RoleEnvironmentDiscover),
            tags: new[] { "domain_family:ad_domain" }));
        registry.Register(new RecoveryExecutionStubTool(
            "ad_search",
            CreateRecoveryRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational),
            tags: new[] { "domain_family:ad_domain" }));
        SetSessionRegistry(session, registry);
        session.SetPreferredDomainIntentFamilyForTesting("thread-domain-bootstrap", ToolSelectionMetadata.DomainIntentFamilyAd);

        var result = session.ResolveNoExtractedRecoveryPostPromptExecutionDecisionForTesting(
            threadId: "thread-domain-bootstrap",
            userRequest: "compare domain controller state",
            routedUserRequest: null,
            toolDefinitions: registry.GetDefinitions(),
            executionContractApplies: false,
            hostDomainIntentBootstrapReplayUsed: false,
            bootstrapOnlyToolActivity: false,
            priorToolCalls: 0,
            priorToolOutputs: 0);

        Assert.Equal("HostDomainIntentBootstrapReplay", result.Kind);
        Assert.Contains("domain_intent_family_ad_domain", result.Reason, System.StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ad_environment_discover", result.ToolName);
        Assert.True(result.ExpandToFullToolAvailability);
    }

    [Fact]
    public void ResolveNoExtractedRecoveryPostPromptExecutionDecisionForTesting_SkipsBootstrapWhenExecutionContractApplies() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var registry = new ToolRegistry();
        registry.Register(new RecoveryExecutionStubTool(
            "ad_environment_discover",
            CreateRecoveryRoutingContract("active_directory", ToolRoutingTaxonomy.RoleEnvironmentDiscover),
            tags: new[] { "domain_family:ad_domain" }));
        SetSessionRegistry(session, registry);
        session.SetPreferredDomainIntentFamilyForTesting("thread-domain-bootstrap-contract", ToolSelectionMetadata.DomainIntentFamilyAd);

        var result = session.ResolveNoExtractedRecoveryPostPromptExecutionDecisionForTesting(
            threadId: "thread-domain-bootstrap-contract",
            userRequest: "run it",
            routedUserRequest: null,
            toolDefinitions: registry.GetDefinitions(),
            executionContractApplies: true,
            hostDomainIntentBootstrapReplayUsed: false,
            bootstrapOnlyToolActivity: false,
            priorToolCalls: 0,
            priorToolOutputs: 0);

        Assert.Equal("None", result.Kind);
        Assert.Equal("no_post_prompt_execution_selected", result.Reason);
        Assert.Null(result.ToolName);
        Assert.False(result.ExpandToFullToolAvailability);
    }

    [Fact]
    public void ResolveNoExtractedRecoveryPostPromptExecutionDecisionForTesting_SelectsOperationalReplayAfterBootstrapOnlyActivity() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var registry = new ToolRegistry();
        registry.Register(new RecoveryExecutionStubTool(
            "ad_environment_discover",
            CreateRecoveryRoutingContract("active_directory", ToolRoutingTaxonomy.RoleEnvironmentDiscover),
            tags: new[] { "domain_family:ad_domain" }));
        registry.Register(new RecoveryExecutionStubTool(
            "ad_replication_summary",
            CreateRecoveryRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational),
            tags: new[] { "domain_family:ad_domain" }));
        registry.Register(new RecoveryExecutionStubTool(
            "ad_monitoring_probe_run",
            CreateRecoveryRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational),
            tags: new[] { "domain_family:ad_domain" }));
        SetSessionRegistry(session, registry);
        session.SetPreferredDomainIntentFamilyForTesting("thread-domain-operational", ToolSelectionMetadata.DomainIntentFamilyAd);

        var result = session.ResolveNoExtractedRecoveryPostPromptExecutionDecisionForTesting(
            threadId: "thread-domain-operational",
            userRequest: "Check AD replication forest status and summarize in UTC.",
            routedUserRequest: null,
            toolDefinitions: registry.GetDefinitions(),
            executionContractApplies: false,
            hostDomainIntentBootstrapReplayUsed: true,
            bootstrapOnlyToolActivity: true,
            priorToolCalls: 1,
            priorToolOutputs: 1);

        Assert.Equal("HostDomainIntentOperationalReplay", result.Kind);
        Assert.Contains("domain_intent_family_ad_domain_operational", result.Reason, System.StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ad_replication_summary", result.ToolName);
        Assert.True(result.ExpandToFullToolAvailability);
    }

    [Fact]
    public void TryBuildHostDomainIntentOperationalReplayCallForTesting_PrefersLdapMonitoringProbeForHostScopedFollowUp() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var registry = new ToolRegistry();
        registry.Register(new RecoveryExecutionStubTool(
            "ad_environment_discover",
            CreateRecoveryRoutingContract("active_directory", ToolRoutingTaxonomy.RoleEnvironmentDiscover),
            tags: new[] { "domain_family:ad_domain" }));
        registry.Register(new RecoveryExecutionStubTool(
            "ad_connectivity_probe",
            CreateRecoveryRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational),
            schema: ToolSchema.Object().NoAdditionalProperties(),
            tags: new[] { "domain_family:ad_domain" }));
        registry.Register(new RecoveryExecutionStubTool(
            "ad_scope_discovery",
            CreateRecoveryRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational),
            schema: ToolSchema.Object(
                    ("discovery_fallback", ToolSchema.String("fallback")),
                    ("domain_controller", ToolSchema.String("dc")))
                .Required("discovery_fallback")
                .NoAdditionalProperties(),
            tags: new[] { "domain_family:ad_domain" }));
        registry.Register(new RecoveryExecutionStubTool(
            "ad_monitoring_probe_run",
            CreateRecoveryRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational),
            schema: ToolSchema.Object(
                    ("probe_kind", ToolSchema.String("probe")),
                    ("domain_controller", ToolSchema.String("dc")),
                    ("discovery_fallback", ToolSchema.String("fallback")))
                .Required("probe_kind")
                .NoAdditionalProperties(),
            tags: new[] { "domain_family:ad_domain" }));
        SetSessionRegistry(session, registry);
        session.SetPreferredDomainIntentFamilyForTesting("thread-domain-ldap", ToolSelectionMetadata.DomainIntentFamilyAd);
        session.SeedThreadToolEvidenceEntryForTesting(
            threadId: "thread-domain-ldap",
            toolName: "ad_environment_discover",
            argumentsJson: "{}",
            output: """{"domain_controllers":["AD0.ad.evotec.xyz","AD2.ad.evotec.xyz","DC1.ad.evotec.pl"]}""",
            summaryMarkdown: string.Empty,
            seenUtcTicks: System.DateTime.UtcNow.Ticks);

        var built = session.TryBuildHostDomainIntentOperationalReplayCallForTesting(
            threadId: "thread-domain-ldap",
            userRequest: "A mozesz sprawdzic czy AD2 odpowiada LDAP i czy wszystko tam sie ladnie uklada?",
            toolDefinitions: registry.GetDefinitions(),
            out var call,
            out var reason);

        Assert.True(built);
        Assert.Equal("ad_monitoring_probe_run", call.Name);
        Assert.Contains("ldap_probe_host_inferred", reason, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"probe_kind\":\"ldap\"", call.Input, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"domain_controller\":\"AD2.ad.evotec.xyz\"", call.Input, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"discovery_fallback\":\"current_forest\"", call.Input, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildHostDomainIntentOperationalReplayCallForTesting_PrefersReplicationTopologyForArtifactOnlyVisualFollowUp() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var registry = new ToolRegistry();
        registry.Register(new RecoveryExecutionStubTool(
            "ad_environment_discover",
            CreateRecoveryRoutingContract("active_directory", ToolRoutingTaxonomy.RoleEnvironmentDiscover),
            tags: new[] { "domain_family:ad_domain" }));
        registry.Register(new RecoveryExecutionStubTool(
            "ad_replication_summary",
            CreateRecoveryRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational),
            schema: ToolSchema.Object().NoAdditionalProperties(),
            tags: new[] { "domain_family:ad_domain" }));
        registry.Register(new RecoveryExecutionStubTool(
            "ad_replikacja_wykres",
            CreateRecoveryRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational),
            schema: ToolSchema.Object().NoAdditionalProperties(),
            tags: new[] { "domain_family:ad_domain" }));
        SetSessionRegistry(session, registry);
        session.SetPreferredDomainIntentFamilyForTesting("thread-visual-topology", ToolSelectionMetadata.DomainIntentFamilyAd);
        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: "thread-visual-topology",
            intentAnchor: "Continue the same replication review.",
            domainIntentFamily: ToolSelectionMetadata.DomainIntentFamilyAd,
            recentToolNames: new[] { "ad_replikacja_forestu" },
            recentEvidenceSnippets: new[] { "ad_replikacja_forestu: forest replication is healthy." },
            priorAnswerPlanUserGoal: "Show the topology from the current replication evidence.",
            priorAnswerPlanPrimaryArtifact: "table",
            priorAnswerPlanPreferredToolNames: new[] { "ad_replikacja_forestu" });

        var built = session.TryBuildHostDomainIntentOperationalReplayCallForTesting(
            threadId: "thread-visual-topology",
            userRequest: "Pokaz to na wykresie topologii replikacji.",
            toolDefinitions: registry.GetDefinitions(),
            out var call,
            out var reason);

        Assert.True(built);
        Assert.Equal("ad_replikacja_wykres", call.Name);
        Assert.Contains("artifact_visual_replication_topology", reason, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildHostDomainIntentOperationalReplayCallForTesting_FallsBackToReplicationSummaryWhenTopologyToolUnavailable() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var registry = new ToolRegistry();
        registry.Register(new RecoveryExecutionStubTool(
            "ad_environment_discover",
            CreateRecoveryRoutingContract("active_directory", ToolRoutingTaxonomy.RoleEnvironmentDiscover),
            tags: new[] { "domain_family:ad_domain" }));
        registry.Register(new RecoveryExecutionStubTool(
            "ad_replication_summary",
            CreateRecoveryRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational),
            schema: ToolSchema.Object().NoAdditionalProperties(),
            tags: new[] { "domain_family:ad_domain" }));
        SetSessionRegistry(session, registry);
        session.SetPreferredDomainIntentFamilyForTesting("thread-visual-topology-fallback", ToolSelectionMetadata.DomainIntentFamilyAd);
        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: "thread-visual-topology-fallback",
            intentAnchor: "Continue the same replication review.",
            domainIntentFamily: ToolSelectionMetadata.DomainIntentFamilyAd,
            recentToolNames: new[] { "ad_replikacja_forestu" },
            recentEvidenceSnippets: new[] { "ad_replikacja_forestu: forest replication is healthy." },
            priorAnswerPlanUserGoal: "Show the topology from the current replication evidence.",
            priorAnswerPlanPrimaryArtifact: "table",
            priorAnswerPlanPreferredToolNames: new[] { "ad_replikacja_forestu" });

        var built = session.TryBuildHostDomainIntentOperationalReplayCallForTesting(
            threadId: "thread-visual-topology-fallback",
            userRequest: "Pokaz to na wykresie topologii replikacji.",
            toolDefinitions: registry.GetDefinitions(),
            out var call,
            out var reason);

        Assert.True(built);
        Assert.Equal("ad_replication_summary", call.Name);
        Assert.Contains("artifact_visual_replication_summary", reason, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveNoExtractedRecoveryPostPromptExecutionDecisionForTesting_PrefersFreshVisibleRequestOverCarryForwardRoutedRequest() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var registry = new ToolRegistry();
        registry.Register(new RecoveryExecutionStubTool(
            "ad_environment_discover",
            CreateRecoveryRoutingContract("active_directory", ToolRoutingTaxonomy.RoleEnvironmentDiscover),
            tags: new[] { "domain_family:ad_domain" }));
        registry.Register(new RecoveryExecutionStubTool(
            "ad_monitoring_probe_run",
            CreateRecoveryRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational),
            schema: ToolSchema.Object(
                    ("probe_kind", ToolSchema.String("probe")),
                    ("domain_controller", ToolSchema.String("dc")),
                    ("discovery_fallback", ToolSchema.String("fallback")))
                .Required("probe_kind")
                .NoAdditionalProperties(),
            tags: new[] { "domain_family:ad_domain" }));
        SetSessionRegistry(session, registry);
        session.SetPreferredDomainIntentFamilyForTesting("thread-fresh-visible", ToolSelectionMetadata.DomainIntentFamilyAd);
        session.SeedThreadToolEvidenceEntryForTesting(
            threadId: "thread-fresh-visible",
            toolName: "ad_environment_discover",
            argumentsJson: "{}",
            output: """{"domain_controllers":["AD2.ad.evotec.xyz"]}""",
            summaryMarkdown: string.Empty,
            seenUtcTicks: System.DateTime.UtcNow.Ticks);

        var result = session.ResolveNoExtractedRecoveryPostPromptExecutionDecisionForTesting(
            threadId: "thread-fresh-visible",
            userRequest: "aale to chyba masz toole do event logow?",
            routedUserRequest: "A mozesz sprawdzic czy AD2 odpowiada LDAP i czy wszystko tam sie ladnie uklada?",
            toolDefinitions: registry.GetDefinitions(),
            executionContractApplies: false,
            hostDomainIntentBootstrapReplayUsed: true,
            bootstrapOnlyToolActivity: true,
            priorToolCalls: 1,
            priorToolOutputs: 1);

        Assert.Equal("None", result.Kind);
        Assert.Equal("no_post_prompt_execution_selected", result.Reason);
        Assert.Null(result.ToolName);
    }

    private static IReadOnlyList<ToolDefinition> CreateCarryoverToolDefinitions() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        return new List<ToolDefinition> {
            new("ad_environment_discover", "discover", schema),
            new("ad_scope_discovery", "scope", schema)
        };
    }

    private static ToolRoutingContract CreateRecoveryRoutingContract(string packId, string role) {
        return new ToolRoutingContract {
            IsRoutingAware = true,
            RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
            PackId = packId,
            Role = role
        };
    }

    private sealed class RecoveryExecutionStubTool : ITool {
        public RecoveryExecutionStubTool(
            string name,
            ToolRoutingContract routing,
            JsonObject? schema = null,
            IReadOnlyList<string>? tags = null) {
            Definition = new ToolDefinition(
                name,
                description: "recovery execution stub",
                parameters: schema,
                routing: routing,
                tags: tags);
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return Task.FromResult("""{"ok":true}""");
        }
    }
}
