using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
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
