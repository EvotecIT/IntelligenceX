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
            toolDefinitions: registry.GetDefinitions(),
            executionContractApplies: false,
            hostDomainIntentBootstrapReplayUsed: false,
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
            toolDefinitions: registry.GetDefinitions(),
            executionContractApplies: true,
            hostDomainIntentBootstrapReplayUsed: false,
            priorToolCalls: 0,
            priorToolOutputs: 0);

        Assert.Equal("None", result.Kind);
        Assert.Equal("no_post_prompt_execution_selected", result.Reason);
        Assert.Null(result.ToolName);
        Assert.False(result.ExpandToFullToolAvailability);
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
        public RecoveryExecutionStubTool(string name, ToolRoutingContract routing, IReadOnlyList<string>? tags = null) {
            Definition = new ToolDefinition(name, description: "recovery execution stub", routing: routing, tags: tags);
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return Task.FromResult("""{"ok":true}""");
        }
    }
}
