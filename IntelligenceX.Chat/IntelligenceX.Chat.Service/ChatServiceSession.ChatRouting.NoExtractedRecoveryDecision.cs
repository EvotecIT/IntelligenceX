using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private enum NoExtractedPromptRecoveryDecisionKind {
        None = 0,
        ExecutionNudge,
        ToolReceiptCorrection,
        ExecutionWatchdog,
        ExecutionContractEscape,
        ContinuationSubsetEscape
    }

    private readonly record struct NoExtractedPromptRecoveryDecision(
        NoExtractedPromptRecoveryDecisionKind Kind,
        string Reason,
        bool ExpandToFullToolAvailability) {
        public static NoExtractedPromptRecoveryDecision None(string reason) =>
            new(NoExtractedPromptRecoveryDecisionKind.None, reason, ExpandToFullToolAvailability: false);

        public static NoExtractedPromptRecoveryDecision ExecutionNudge(string reason) =>
            new(NoExtractedPromptRecoveryDecisionKind.ExecutionNudge, reason, ExpandToFullToolAvailability: false);

        public static NoExtractedPromptRecoveryDecision ToolReceiptCorrection(string reason) =>
            new(NoExtractedPromptRecoveryDecisionKind.ToolReceiptCorrection, reason, ExpandToFullToolAvailability: false);

        public static NoExtractedPromptRecoveryDecision ExecutionWatchdog(string reason) =>
            new(NoExtractedPromptRecoveryDecisionKind.ExecutionWatchdog, reason, ExpandToFullToolAvailability: false);

        public static NoExtractedPromptRecoveryDecision ExecutionContractEscape(string reason) =>
            new(NoExtractedPromptRecoveryDecisionKind.ExecutionContractEscape, reason, ExpandToFullToolAvailability: true);

        public static NoExtractedPromptRecoveryDecision ContinuationSubsetEscape(string reason) =>
            new(NoExtractedPromptRecoveryDecisionKind.ContinuationSubsetEscape, reason, ExpandToFullToolAvailability: true);
    }

    private static NoExtractedPromptRecoveryDecision ResolveNoExtractedPromptRecoveryDecision(
        bool suppressLocalToolRecoveryRetries,
        bool shouldAttemptExecutionNudge,
        string executionNudgeReason,
        bool shouldAttemptToolReceiptCorrection,
        bool shouldAttemptWatchdog,
        string noToolExecutionWatchdogReason,
        bool executionContractApplies,
        bool hasToolActivity,
        bool executionContractEscapeUsed,
        bool fullToolsAvailable,
        bool shouldAttemptContinuationSubsetEscape,
        string continuationSubsetEscapeReason) {
        if (shouldAttemptExecutionNudge) {
            return NoExtractedPromptRecoveryDecision.ExecutionNudge(executionNudgeReason);
        }

        if (!suppressLocalToolRecoveryRetries && shouldAttemptToolReceiptCorrection) {
            return NoExtractedPromptRecoveryDecision.ToolReceiptCorrection("tool_receipt_correction");
        }

        if (shouldAttemptWatchdog) {
            return NoExtractedPromptRecoveryDecision.ExecutionWatchdog(noToolExecutionWatchdogReason);
        }

        if (executionContractApplies
            && !hasToolActivity
            && !executionContractEscapeUsed
            && fullToolsAvailable) {
            return NoExtractedPromptRecoveryDecision.ExecutionContractEscape("execution_contract_no_tool_activity");
        }

        if (!suppressLocalToolRecoveryRetries && shouldAttemptContinuationSubsetEscape) {
            return NoExtractedPromptRecoveryDecision.ContinuationSubsetEscape(continuationSubsetEscapeReason);
        }

        return NoExtractedPromptRecoveryDecision.None("no_prompt_recovery_selected");
    }

    private async Task<TurnInfo> ApplyNoExtractedPromptRecoveryDecisionAsync(
        IntelligenceXClient client,
        StreamWriter writer,
        ChatRequest request,
        string threadId,
        ChatOptions options,
        CancellationToken turnToken,
        bool planExecuteReviewLoop,
        int modelHeartbeatSeconds,
        string userRequest,
        string assistantDraft,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        NoExtractedPromptRecoveryDecision decision) {
        var prompt = string.Empty;
        var promptTextForOrdering = userRequest;
        var phaseStatus = ChatStatusCodes.Thinking;
        var phaseMessage = string.Empty;
        var heartbeatLabel = string.Empty;

        switch (decision.Kind) {
            case NoExtractedPromptRecoveryDecisionKind.ExecutionNudge:
                prompt = BuildToolExecutionNudgePrompt(userRequest, assistantDraft, toolDefinitions);
                phaseStatus = planExecuteReviewLoop ? ChatStatusCodes.PhasePlan : ChatStatusCodes.Thinking;
                phaseMessage = "Re-planning to execute available tools in this turn.";
                heartbeatLabel = "Re-planning execution";
                break;
            case NoExtractedPromptRecoveryDecisionKind.ToolReceiptCorrection:
                prompt = BuildToolReceiptCorrectionPrompt(userRequest, assistantDraft);
                phaseStatus = planExecuteReviewLoop ? ChatStatusCodes.PhasePlan : ChatStatusCodes.Thinking;
                phaseMessage = "Re-planning to correct an inconsistent tool receipt in this turn.";
                heartbeatLabel = "Re-planning tool receipt";
                break;
            case NoExtractedPromptRecoveryDecisionKind.ExecutionWatchdog:
                prompt = BuildNoToolExecutionWatchdogPrompt(userRequest, assistantDraft, toolDefinitions);
                phaseStatus = planExecuteReviewLoop ? ChatStatusCodes.PhaseReview : ChatStatusCodes.Thinking;
                phaseMessage = "Re-validating tool execution for this turn.";
                heartbeatLabel = "Re-validating execution";
                break;
            case NoExtractedPromptRecoveryDecisionKind.ExecutionContractEscape:
                prompt = BuildExecutionContractEscapePrompt(userRequest, assistantDraft, toolDefinitions);
                phaseStatus = planExecuteReviewLoop ? ChatStatusCodes.PhasePlan : ChatStatusCodes.Thinking;
                phaseMessage = "Selected action had no tool activity; retrying with full tool availability.";
                heartbeatLabel = "Re-planning with full tools";
                break;
            case NoExtractedPromptRecoveryDecisionKind.ContinuationSubsetEscape:
                prompt = BuildContinuationSubsetEscapePrompt(userRequest, assistantDraft, toolDefinitions);
                phaseStatus = planExecuteReviewLoop ? ChatStatusCodes.PhasePlan : ChatStatusCodes.Thinking;
                phaseMessage = "Follow-up subset had no tool activity; retrying with full tool availability.";
                heartbeatLabel = "Expanding follow-up tools";
                break;
            default:
                throw new System.InvalidOperationException("Prompt recovery decision must be non-empty before applying.");
        }

        var promptOptions = await CopyChatOptionsWithPromptAwareToolOrderingAndEmitStatusAsync(
                writer,
                request.RequestId,
                threadId,
                options,
                string.IsNullOrWhiteSpace(promptTextForOrdering) ? prompt : promptTextForOrdering,
                strategy: "prompt_recovery",
                newThreadOverride: false)
            .ConfigureAwait(false);
        return await RunModelPhaseWithProgressAsync(
                client,
                writer,
                request.RequestId,
                threadId,
                ChatInput.FromText(prompt),
                promptOptions,
                turnToken,
                phaseStatus: phaseStatus,
                phaseMessage: phaseMessage,
                heartbeatLabel: heartbeatLabel,
                heartbeatSeconds: modelHeartbeatSeconds)
            .ConfigureAwait(false);
    }

    private static bool ShouldCountUnknownPendingActionEnvelopeNudge(string reason) {
        return string.Equals(reason, "single_unknown_pending_action_envelope", System.StringComparison.OrdinalIgnoreCase)
               || string.Equals(reason, "structured_draft_single_unknown_pending_action_envelope", System.StringComparison.OrdinalIgnoreCase);
    }

    internal static (string Kind, string Reason, bool ExpandToFullToolAvailability) ResolveNoExtractedPromptRecoveryDecisionForTesting(
        string userRequest,
        string assistantDraft,
        bool executionContractApplies,
        bool usedContinuationSubset,
        bool suppressLocalToolRecoveryRetries,
        bool executionNudgeUsed,
        bool toolReceiptCorrectionUsed,
        bool watchdogAlreadyUsed,
        bool executionContractEscapeUsed,
        bool toolsAvailable,
        bool fullToolsAvailable,
        int priorToolCalls,
        int priorToolOutputs,
        int assistantDraftToolCalls,
        bool continuationFollowUpTurn,
        bool compactFollowUpTurn) {
        var shouldAttemptExecutionNudge = false;
        var executionNudgeReason = executionNudgeUsed
            ? "execution_nudge_already_used"
            : "execution_nudge_not_evaluated";
        if (!suppressLocalToolRecoveryRetries && !executionNudgeUsed) {
            shouldAttemptExecutionNudge = EvaluateToolExecutionNudgeDecision(
                userRequest: userRequest,
                assistantDraft: assistantDraft,
                toolsAvailable: toolsAvailable,
                priorToolCalls: priorToolCalls,
                assistantDraftToolCalls: assistantDraftToolCalls,
                usedContinuationSubset: usedContinuationSubset,
                compactFollowUpHint: compactFollowUpTurn,
                out executionNudgeReason);
        } else if (suppressLocalToolRecoveryRetries) {
            executionNudgeReason = "local_runtime_recovery_disabled";
        }

        var shouldAttemptToolReceiptCorrection = !suppressLocalToolRecoveryRetries
                                                && !toolReceiptCorrectionUsed
                                                && ShouldAttemptToolReceiptCorrection(
                                                    userRequest: userRequest,
                                                    assistantDraft: assistantDraft,
                                                    tools: toolsAvailable ? new[] { new ToolDefinition("tool") } : System.Array.Empty<ToolDefinition>(),
                                                    priorToolCalls: priorToolCalls,
                                                    priorToolOutputs: priorToolOutputs,
                                                    assistantDraftToolCalls: assistantDraftToolCalls);

        var shouldAttemptWatchdog = false;
        var noToolExecutionWatchdogReason = suppressLocalToolRecoveryRetries
            ? "local_runtime_recovery_disabled"
            : "not_evaluated";
        if (!suppressLocalToolRecoveryRetries) {
            shouldAttemptWatchdog = ShouldAttemptNoToolExecutionWatchdog(
                userRequest: userRequest,
                assistantDraft: assistantDraft,
                toolsAvailable: toolsAvailable,
                priorToolCalls: priorToolCalls,
                priorToolOutputs: priorToolOutputs,
                assistantDraftToolCalls: assistantDraftToolCalls,
                continuationFollowUpTurn: continuationFollowUpTurn,
                compactFollowUpTurn: compactFollowUpTurn,
                executionNudgeUsed: executionNudgeUsed,
                toolReceiptCorrectionUsed: toolReceiptCorrectionUsed,
                watchdogAlreadyUsed: watchdogAlreadyUsed,
                out noToolExecutionWatchdogReason);
        }

        var shouldAttemptContinuationSubsetEscape = ShouldAttemptContinuationSubsetEscape(
            executionContractApplies: executionContractApplies,
            usedContinuationSubset: usedContinuationSubset,
            continuationSubsetEscapeUsed: false,
            toolsAvailable: fullToolsAvailable,
            priorToolCalls: priorToolCalls,
            priorToolOutputs: priorToolOutputs,
            out var continuationSubsetEscapeReason);

        var decision = ResolveNoExtractedPromptRecoveryDecision(
            suppressLocalToolRecoveryRetries: suppressLocalToolRecoveryRetries,
            shouldAttemptExecutionNudge: shouldAttemptExecutionNudge,
            executionNudgeReason: executionNudgeReason,
            shouldAttemptToolReceiptCorrection: shouldAttemptToolReceiptCorrection,
            shouldAttemptWatchdog: shouldAttemptWatchdog,
            noToolExecutionWatchdogReason: noToolExecutionWatchdogReason,
            executionContractApplies: executionContractApplies,
            hasToolActivity: priorToolCalls > 0 || priorToolOutputs > 0,
            executionContractEscapeUsed: executionContractEscapeUsed,
            fullToolsAvailable: fullToolsAvailable,
            shouldAttemptContinuationSubsetEscape: shouldAttemptContinuationSubsetEscape,
            continuationSubsetEscapeReason: continuationSubsetEscapeReason);

        return (decision.Kind.ToString(), decision.Reason, decision.ExpandToFullToolAvailability);
    }
}
