using System.Collections.Generic;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private enum NoExtractedToolRoundFlow {
        Continue,
        ProceedToFinalize,
        ReturnFinal
    }

    private readonly record struct NoExtractedToolRoundOutcome(NoExtractedToolRoundFlow Flow, ChatTurnRunResult? FinalResult) {
        public static NoExtractedToolRoundOutcome ContinueRound() => new(NoExtractedToolRoundFlow.Continue, null);

        public static NoExtractedToolRoundOutcome ProceedToFinalize() => new(NoExtractedToolRoundFlow.ProceedToFinalize, null);

        public static NoExtractedToolRoundOutcome ReturnFinal(ChatTurnRunResult result) => new(NoExtractedToolRoundFlow.ReturnFinal, result);
    }

    private sealed class NoExtractedToolRoundState {
        public NoExtractedToolRoundState(
            TurnInfo turn,
            string assistantDraft,
            bool controlPayloadDetected,
            string routedUserRequest,
            bool executionContractApplies,
            IReadOnlyList<ToolDefinition> toolDefs,
            ChatOptions options,
            bool usedContinuationSubset,
            int toolRounds,
            int projectionFallbackCount,
            int reviewPassesUsed,
            bool executionNudgeUsed,
            bool toolReceiptCorrectionUsed,
            bool noToolExecutionWatchdogUsed,
            bool executionContractEscapeUsed,
            bool continuationSubsetEscapeUsed,
            bool autoPendingActionReplayUsed,
            bool proactiveFollowUpUsed,
            bool localNoTextDirectRetryUsed,
            bool structuredNextActionRetryUsed,
            bool toolProgressRecoveryUsed,
            bool hostStructuredNextActionReplayUsed,
            bool packCapabilityFallbackReplayUsed,
            bool noResultPhaseLoopWatchdogUsed,
            bool interimResultSent) {
            Turn = turn;
            AssistantDraft = assistantDraft;
            ControlPayloadDetected = controlPayloadDetected;
            RoutedUserRequest = routedUserRequest;
            ExecutionContractApplies = executionContractApplies;
            ToolDefs = toolDefs;
            Options = options;
            UsedContinuationSubset = usedContinuationSubset;
            ToolRounds = toolRounds;
            ProjectionFallbackCount = projectionFallbackCount;
            ReviewPassesUsed = reviewPassesUsed;
            ExecutionNudgeUsed = executionNudgeUsed;
            ToolReceiptCorrectionUsed = toolReceiptCorrectionUsed;
            NoToolExecutionWatchdogUsed = noToolExecutionWatchdogUsed;
            ExecutionContractEscapeUsed = executionContractEscapeUsed;
            ContinuationSubsetEscapeUsed = continuationSubsetEscapeUsed;
            AutoPendingActionReplayUsed = autoPendingActionReplayUsed;
            ProactiveFollowUpUsed = proactiveFollowUpUsed;
            LocalNoTextDirectRetryUsed = localNoTextDirectRetryUsed;
            StructuredNextActionRetryUsed = structuredNextActionRetryUsed;
            ToolProgressRecoveryUsed = toolProgressRecoveryUsed;
            HostStructuredNextActionReplayUsed = hostStructuredNextActionReplayUsed;
            PackCapabilityFallbackReplayUsed = packCapabilityFallbackReplayUsed;
            NoResultPhaseLoopWatchdogUsed = noResultPhaseLoopWatchdogUsed;
            InterimResultSent = interimResultSent;
        }

        public TurnInfo Turn { get; set; }

        public string AssistantDraft { get; set; }

        public bool ControlPayloadDetected { get; set; }

        public string RoutedUserRequest { get; set; }

        public bool ExecutionContractApplies { get; set; }

        public IReadOnlyList<ToolDefinition> ToolDefs { get; set; }

        public ChatOptions Options { get; set; }

        public bool UsedContinuationSubset { get; set; }

        public int ToolRounds { get; set; }

        public int ProjectionFallbackCount { get; set; }

        public int ReviewPassesUsed { get; set; }

        public bool ExecutionNudgeUsed { get; set; }

        public bool ToolReceiptCorrectionUsed { get; set; }

        public bool NoToolExecutionWatchdogUsed { get; set; }

        public bool ExecutionContractEscapeUsed { get; set; }

        public bool ContinuationSubsetEscapeUsed { get; set; }

        public bool AutoPendingActionReplayUsed { get; set; }

        public bool ProactiveFollowUpUsed { get; set; }

        public bool LocalNoTextDirectRetryUsed { get; set; }

        public bool StructuredNextActionRetryUsed { get; set; }

        public bool ToolProgressRecoveryUsed { get; set; }

        public bool HostStructuredNextActionReplayUsed { get; set; }

        public bool PackCapabilityFallbackReplayUsed { get; set; }

        public bool NoResultPhaseLoopWatchdogUsed { get; set; }

        public bool InterimResultSent { get; set; }
    }

    private static void RestoreNoExtractedToolRoundState(
        NoExtractedToolRoundState state,
        ref TurnInfo turn,
        ref string routedUserRequest,
        ref bool executionContractApplies,
        ref IReadOnlyList<ToolDefinition> toolDefs,
        ref ChatOptions options,
        ref bool usedContinuationSubset,
        ref int toolRounds,
        ref int projectionFallbackCount,
        ref int reviewPassesUsed,
        ref bool executionNudgeUsed,
        ref bool toolReceiptCorrectionUsed,
        ref bool noToolExecutionWatchdogUsed,
        ref bool executionContractEscapeUsed,
        ref bool continuationSubsetEscapeUsed,
        ref bool autoPendingActionReplayUsed,
        ref bool proactiveFollowUpUsed,
        ref bool localNoTextDirectRetryUsed,
        ref bool structuredNextActionRetryUsed,
        ref bool toolProgressRecoveryUsed,
        ref bool hostStructuredNextActionReplayUsed,
        ref bool packCapabilityFallbackReplayUsed,
        ref bool noResultPhaseLoopWatchdogUsed,
        ref bool interimResultSent) {
        turn = state.Turn;
        routedUserRequest = state.RoutedUserRequest;
        executionContractApplies = state.ExecutionContractApplies;
        toolDefs = state.ToolDefs;
        options = state.Options;
        usedContinuationSubset = state.UsedContinuationSubset;
        toolRounds = state.ToolRounds;
        projectionFallbackCount = state.ProjectionFallbackCount;
        reviewPassesUsed = state.ReviewPassesUsed;
        executionNudgeUsed = state.ExecutionNudgeUsed;
        toolReceiptCorrectionUsed = state.ToolReceiptCorrectionUsed;
        noToolExecutionWatchdogUsed = state.NoToolExecutionWatchdogUsed;
        executionContractEscapeUsed = state.ExecutionContractEscapeUsed;
        continuationSubsetEscapeUsed = state.ContinuationSubsetEscapeUsed;
        autoPendingActionReplayUsed = state.AutoPendingActionReplayUsed;
        proactiveFollowUpUsed = state.ProactiveFollowUpUsed;
        localNoTextDirectRetryUsed = state.LocalNoTextDirectRetryUsed;
        structuredNextActionRetryUsed = state.StructuredNextActionRetryUsed;
        toolProgressRecoveryUsed = state.ToolProgressRecoveryUsed;
        hostStructuredNextActionReplayUsed = state.HostStructuredNextActionReplayUsed;
        packCapabilityFallbackReplayUsed = state.PackCapabilityFallbackReplayUsed;
        noResultPhaseLoopWatchdogUsed = state.NoResultPhaseLoopWatchdogUsed;
        interimResultSent = state.InterimResultSent;
    }
}
