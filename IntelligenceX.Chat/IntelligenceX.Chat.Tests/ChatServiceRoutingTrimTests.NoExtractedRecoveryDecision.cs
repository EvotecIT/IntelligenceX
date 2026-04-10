using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {

    [Fact]
    public void ResolveNoExtractedPromptRecoveryDecisionForTesting_PrefersExecutionNudge() {
        var result = ChatServiceSession.ResolveNoExtractedPromptRecoveryDecisionForTesting(
            userRequest: "{\"ix_action_selection\":{\"id\":\"act_001\",\"title\":\"Run forest probe\",\"request\":\"Run it.\"}}",
            assistantDraft: "Ok, doing it now.",
            executionContractApplies: false,
            usedContinuationSubset: false,
            suppressLocalToolRecoveryRetries: false,
            executionNudgeUsed: false,
            toolReceiptCorrectionUsed: false,
            watchdogAlreadyUsed: false,
            executionContractEscapeUsed: false,
            toolsAvailable: true,
            fullToolsAvailable: true,
            priorToolCalls: 0,
            priorToolOutputs: 0,
            assistantDraftToolCalls: 0,
            continuationFollowUpTurn: false,
            compactFollowUpTurn: false);

        Assert.Equal("ExecutionNudge", result.Kind);
        Assert.Equal("explicit_action_selection_payload", result.Reason);
        Assert.False(result.ExpandToFullToolAvailability);
    }

    [Fact]
    public void ResolveNoExtractedPromptRecoveryDecisionForTesting_SelectsExecutionContractEscapeAfterWatchdogBudget() {
        var result = ChatServiceSession.ResolveNoExtractedPromptRecoveryDecisionForTesting(
            userRequest: "{\"ix_action_selection\":{\"id\":\"act_001\",\"title\":\"Apply change\",\"request\":\"Apply the selected change now.\",\"mutating\":true}}",
            assistantDraft: "I am applying the selected change now.",
            executionContractApplies: true,
            usedContinuationSubset: false,
            suppressLocalToolRecoveryRetries: false,
            executionNudgeUsed: true,
            toolReceiptCorrectionUsed: false,
            watchdogAlreadyUsed: true,
            executionContractEscapeUsed: false,
            toolsAvailable: true,
            fullToolsAvailable: true,
            priorToolCalls: 0,
            priorToolOutputs: 0,
            assistantDraftToolCalls: 0,
            continuationFollowUpTurn: false,
            compactFollowUpTurn: false);

        Assert.Equal("ExecutionContractEscape", result.Kind);
        Assert.Equal("execution_contract_no_tool_activity", result.Reason);
        Assert.True(result.ExpandToFullToolAvailability);
    }

    [Fact]
    public void ResolveNoExtractedPromptRecoveryDecisionForTesting_SelectsContinuationSubsetEscapeWhenSubsetStalls() {
        var result = ChatServiceSession.ResolveNoExtractedPromptRecoveryDecisionForTesting(
            userRequest: "Check remaining controllers for the same replication issue.",
            assistantDraft: "Acknowledged.",
            executionContractApplies: false,
            usedContinuationSubset: true,
            suppressLocalToolRecoveryRetries: false,
            executionNudgeUsed: true,
            toolReceiptCorrectionUsed: false,
            watchdogAlreadyUsed: true,
            executionContractEscapeUsed: false,
            toolsAvailable: true,
            fullToolsAvailable: true,
            priorToolCalls: 0,
            priorToolOutputs: 0,
            assistantDraftToolCalls: 0,
            continuationFollowUpTurn: false,
            compactFollowUpTurn: false);

        Assert.Equal("ContinuationSubsetEscape", result.Kind);
        Assert.Equal("continuation_subset_no_tool_activity", result.Reason);
        Assert.True(result.ExpandToFullToolAvailability);
    }

    [Fact]
    public void ResolveNoExtractedPromptRecoveryDecisionForTesting_SuppressesSubsetEscapeForLocalRuntimeNoToolPaths() {
        var result = ChatServiceSession.ResolveNoExtractedPromptRecoveryDecisionForTesting(
            userRequest: "Check remaining controllers for the same replication issue.",
            assistantDraft: string.Empty,
            executionContractApplies: false,
            usedContinuationSubset: true,
            suppressLocalToolRecoveryRetries: true,
            executionNudgeUsed: false,
            toolReceiptCorrectionUsed: false,
            watchdogAlreadyUsed: false,
            executionContractEscapeUsed: false,
            toolsAvailable: false,
            fullToolsAvailable: true,
            priorToolCalls: 0,
            priorToolOutputs: 0,
            assistantDraftToolCalls: 0,
            continuationFollowUpTurn: false,
            compactFollowUpTurn: false);

        Assert.Equal("None", result.Kind);
        Assert.Equal("no_prompt_recovery_selected", result.Reason);
        Assert.False(result.ExpandToFullToolAvailability);
    }

    [Fact]
    public void ResolveNoExtractedPromptRecoveryDecisionForTesting_DoesNotNudgeArtifactOnlyTopologyFollowUp() {
        var result = ChatServiceSession.ResolveNoExtractedPromptRecoveryDecisionForTesting(
            userRequest: "Pokaz to na wykresie topologii replikacji.",
            assistantDraft: string.Empty,
            executionContractApplies: false,
            usedContinuationSubset: false,
            suppressLocalToolRecoveryRetries: false,
            executionNudgeUsed: false,
            toolReceiptCorrectionUsed: false,
            watchdogAlreadyUsed: false,
            executionContractEscapeUsed: false,
            toolsAvailable: true,
            fullToolsAvailable: true,
            priorToolCalls: 0,
            priorToolOutputs: 0,
            assistantDraftToolCalls: 0,
            continuationFollowUpTurn: true,
            compactFollowUpTurn: true);

        Assert.Equal("None", result.Kind);
        Assert.Equal("no_prompt_recovery_selected", result.Reason);
        Assert.False(result.ExpandToFullToolAvailability);
    }
}
