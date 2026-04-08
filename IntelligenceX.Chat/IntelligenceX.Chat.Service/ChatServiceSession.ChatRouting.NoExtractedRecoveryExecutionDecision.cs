using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private enum NoExtractedRecoveryExecutionDecisionKind {
        None = 0,
        AutoPendingActionReplay,
        CarryoverStructuredNextActionReplay,
        BackgroundWorkReadyReplay,
        HostDomainIntentBootstrapReplay,
        HostDomainIntentOperationalReplay
    }

    private readonly record struct NoExtractedRecoveryExecutionDecision(
        NoExtractedRecoveryExecutionDecisionKind Kind,
        string Reason,
        bool ExpandToFullToolAvailability,
        string ReplayPayload,
        string ActionId,
        string BackgroundWorkItemId,
        ToolCall? ToolCall,
        string ExecutePhaseMessage,
        string ReviewPhaseMessage,
        string ReviewHeartbeatLabel,
        bool RememberSuccessfulPreflightCalls) {
        public static NoExtractedRecoveryExecutionDecision None(string reason) =>
            new(
                NoExtractedRecoveryExecutionDecisionKind.None,
                reason,
                ExpandToFullToolAvailability: false,
                ReplayPayload: string.Empty,
                ActionId: string.Empty,
                BackgroundWorkItemId: string.Empty,
                ToolCall: null,
                ExecutePhaseMessage: string.Empty,
                ReviewPhaseMessage: string.Empty,
                ReviewHeartbeatLabel: string.Empty,
                RememberSuccessfulPreflightCalls: false);

        public static NoExtractedRecoveryExecutionDecision AutoPendingActionReplay(string reason, string replayPayload, string actionId) =>
            new(
                NoExtractedRecoveryExecutionDecisionKind.AutoPendingActionReplay,
                reason,
                ExpandToFullToolAvailability: false,
                ReplayPayload: replayPayload,
                ActionId: actionId,
                BackgroundWorkItemId: string.Empty,
                ToolCall: null,
                ExecutePhaseMessage: $"Executing follow-up action {actionId} directly.",
                ReviewPhaseMessage: string.Empty,
                ReviewHeartbeatLabel: string.Empty,
                RememberSuccessfulPreflightCalls: false);

        public static NoExtractedRecoveryExecutionDecision CarryoverStructuredNextActionReplay(string reason, ToolCall toolCall) =>
            new(
                NoExtractedRecoveryExecutionDecisionKind.CarryoverStructuredNextActionReplay,
                reason,
                ExpandToFullToolAvailability: true,
                ReplayPayload: string.Empty,
                ActionId: string.Empty,
                BackgroundWorkItemId: string.Empty,
                ToolCall: toolCall,
                ExecutePhaseMessage: $"Executing queued read-only follow-up action ({toolCall.Name})...",
                ReviewPhaseMessage: "Reviewing queued follow-up action results...",
                ReviewHeartbeatLabel: "Reviewing queued action",
                RememberSuccessfulPreflightCalls: false);

        public static NoExtractedRecoveryExecutionDecision BackgroundWorkReadyReplay(string reason, string backgroundWorkItemId, ToolCall toolCall) =>
            new(
                NoExtractedRecoveryExecutionDecisionKind.BackgroundWorkReadyReplay,
                reason,
                ExpandToFullToolAvailability: true,
                ReplayPayload: string.Empty,
                ActionId: string.Empty,
                BackgroundWorkItemId: backgroundWorkItemId,
                ToolCall: toolCall,
                ExecutePhaseMessage: $"Executing prepared background follow-up ({toolCall.Name})...",
                ReviewPhaseMessage: "Reviewing prepared background follow-up results...",
                ReviewHeartbeatLabel: "Reviewing background follow-up",
                RememberSuccessfulPreflightCalls: false);

        public static NoExtractedRecoveryExecutionDecision HostDomainIntentBootstrapReplay(string reason, ToolCall toolCall) =>
            new(
                NoExtractedRecoveryExecutionDecisionKind.HostDomainIntentBootstrapReplay,
                reason,
                ExpandToFullToolAvailability: true,
                ReplayPayload: string.Empty,
                ActionId: string.Empty,
                BackgroundWorkItemId: string.Empty,
                ToolCall: toolCall,
                ExecutePhaseMessage: $"Executing domain-scope bootstrap ({toolCall.Name})...",
                ReviewPhaseMessage: "Continuing domain-scoped task after bootstrap...",
                ReviewHeartbeatLabel: "Continuing domain task",
                RememberSuccessfulPreflightCalls: true);

        public static NoExtractedRecoveryExecutionDecision HostDomainIntentOperationalReplay(string reason, ToolCall toolCall) =>
            new(
                NoExtractedRecoveryExecutionDecisionKind.HostDomainIntentOperationalReplay,
                reason,
                ExpandToFullToolAvailability: true,
                ReplayPayload: string.Empty,
                ActionId: string.Empty,
                BackgroundWorkItemId: string.Empty,
                ToolCall: toolCall,
                ExecutePhaseMessage: $"Executing domain-scoped operational follow-up ({toolCall.Name})...",
                ReviewPhaseMessage: "Reviewing domain-scoped operational results...",
                ReviewHeartbeatLabel: "Reviewing domain result",
                RememberSuccessfulPreflightCalls: false);
    }

    private NoExtractedRecoveryExecutionDecision ResolveNoExtractedRecoveryPrePromptExecutionDecision(
        string threadId,
        string userRequest,
        string assistantDraft,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyDictionary<string, bool> mutatingToolHintsByName,
        bool continuationFollowUpTurn,
        bool compactFollowUpTurn,
        bool autoPendingActionReplayUsed,
        bool hostStructuredNextActionReplayUsed,
        bool explicitToolReferenceInUserRequest,
        int priorToolCalls,
        int priorToolOutputs) {
        if (!autoPendingActionReplayUsed
            && priorToolCalls == 0
            && priorToolOutputs == 0
            && !explicitToolReferenceInUserRequest
            && LooksLikeContinuationFollowUp(userRequest)
            && TryBuildSinglePendingActionSelectionPayload(assistantDraft, out var autoSelectionPayload, out var autoActionId)) {
            return NoExtractedRecoveryExecutionDecision.AutoPendingActionReplay(
                reason: "single_pending_action_auto_replay",
                replayPayload: autoSelectionPayload,
                actionId: autoActionId);
        }

        if (!hostStructuredNextActionReplayUsed
            && !explicitToolReferenceInUserRequest
            && ShouldAttemptCarryoverStructuredNextActionReplay(
                continuationFollowUpTurn: continuationFollowUpTurn,
                compactFollowUpTurn: compactFollowUpTurn,
                userRequest: userRequest,
                assistantDraft: assistantDraft)
            && priorToolCalls == 0
            && priorToolOutputs == 0) {
            var carryoverHostHintInput = BuildCarryoverHostHintInput(userRequest, assistantDraft);
            if (TryBuildCarryoverStructuredNextActionToolCallCore(
                    threadId: threadId,
                    replayDecisionUserRequest: userRequest,
                    hostHintUserRequest: carryoverHostHintInput,
                    toolDefinitions: toolDefinitions,
                    mutatingToolHintsByName: mutatingToolHintsByName,
                    out var carryoverStructuredNextActionCall,
                    out var carryoverStructuredNextActionReason)) {
                if (TryPreviewReadyBackgroundWorkReplayCandidate(
                        threadId: threadId,
                        userRequest: userRequest,
                        toolDefinitions: toolDefinitions,
                        mutatingToolHintsByName: mutatingToolHintsByName,
                        out var backgroundReplayCandidate,
                        out var backgroundReplayReason)
                    && ShouldPreferBackgroundWorkReplayOverCarryover(backgroundReplayCandidate.Item)) {
                    return NoExtractedRecoveryExecutionDecision.BackgroundWorkReadyReplay(
                        backgroundReplayReason,
                        backgroundReplayCandidate.ItemId,
                        backgroundReplayCandidate.ToolCall);
                }

                return NoExtractedRecoveryExecutionDecision.CarryoverStructuredNextActionReplay(
                    carryoverStructuredNextActionReason,
                    carryoverStructuredNextActionCall);
            }
        }

        if (!explicitToolReferenceInUserRequest
            && ShouldAttemptCarryoverStructuredNextActionReplay(
                continuationFollowUpTurn: continuationFollowUpTurn,
                compactFollowUpTurn: compactFollowUpTurn,
                userRequest: userRequest,
                assistantDraft: assistantDraft)
            && priorToolCalls == 0
            && priorToolOutputs == 0) {
            var backgroundWorkReason = string.Empty;
            if (TryBuildReadyBackgroundWorkToolCall(
                    threadId: threadId,
                    userRequest: userRequest,
                    toolDefinitions: toolDefinitions,
                    mutatingToolHintsByName: mutatingToolHintsByName,
                    toolCall: out var backgroundWorkToolCall,
                    itemId: out var backgroundWorkItemId,
                    reason: out backgroundWorkReason)) {
                return NoExtractedRecoveryExecutionDecision.BackgroundWorkReadyReplay(
                    backgroundWorkReason,
                    backgroundWorkItemId,
                    backgroundWorkToolCall);
            }

            if (string.Equals(backgroundWorkReason, "background_work_waiting_on_prerequisites", StringComparison.OrdinalIgnoreCase)) {
                return NoExtractedRecoveryExecutionDecision.None(backgroundWorkReason);
            }
        }

        return NoExtractedRecoveryExecutionDecision.None("no_pre_prompt_execution_selected");
    }

    private static bool ShouldPreferBackgroundWorkReplayOverCarryover(ThreadBackgroundWorkItem item) {
        var normalizedPriority = ToolHandoffFollowUpPriorities.Normalize(item.FollowUpPriority);
        var normalizedKind = ToolHandoffFollowUpKinds.Normalize(item.FollowUpKind);
        if (normalizedPriority >= ToolHandoffFollowUpPriorities.High) {
            return true;
        }

        return string.Equals(normalizedKind, ToolHandoffFollowUpKinds.Verification, StringComparison.OrdinalIgnoreCase)
               && normalizedPriority >= ToolHandoffFollowUpPriorities.Normal;
    }

    private NoExtractedRecoveryExecutionDecision ResolveNoExtractedRecoveryPostPromptExecutionDecision(
        string threadId,
        string visibleUserRequest,
        string routedUserRequest,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        bool executionContractApplies,
        bool hostDomainIntentBootstrapReplayUsed,
        bool bootstrapOnlyToolActivity,
        int priorToolCalls,
        int priorToolOutputs) {
        var recoveryDecisionUserRequest = string.IsNullOrWhiteSpace(visibleUserRequest)
            ? routedUserRequest
            : visibleUserRequest;
        if (!hostDomainIntentBootstrapReplayUsed
            && !executionContractApplies
            && priorToolCalls == 0
            && priorToolOutputs == 0
            && TryBuildHostDomainIntentEnvironmentBootstrapCall(
                threadId: threadId,
                userRequest: recoveryDecisionUserRequest,
                toolDefinitions: toolDefinitions,
                out var hostDomainBootstrapCall,
                out var hostDomainBootstrapReason)) {
            return NoExtractedRecoveryExecutionDecision.HostDomainIntentBootstrapReplay(
                hostDomainBootstrapReason,
                hostDomainBootstrapCall);
        }

        if (hostDomainIntentBootstrapReplayUsed
            && bootstrapOnlyToolActivity
            && !executionContractApplies
            && TryBuildHostDomainIntentOperationalReplayCall(
                threadId: threadId,
                userRequest: recoveryDecisionUserRequest,
                toolDefinitions: toolDefinitions,
                out var hostDomainOperationalCall,
                out var hostDomainOperationalReason)) {
            return NoExtractedRecoveryExecutionDecision.HostDomainIntentOperationalReplay(
                hostDomainOperationalReason,
                hostDomainOperationalCall);
        }

        return NoExtractedRecoveryExecutionDecision.None("no_post_prompt_execution_selected");
    }

    private async Task<(TurnInfo Turn, int ToolRounds, int ProjectionFallbackCount)> ApplyNoExtractedRecoveryExecutionDecisionAsync(
        IntelligenceXClient client,
        StreamWriter writer,
        ChatRequest request,
        string threadId,
        bool continuationFollowUpTurn,
        int round,
        int maxRounds,
        bool parallelTools,
        bool allowMutatingParallel,
        bool planExecuteReviewLoop,
        int modelHeartbeatSeconds,
        int toolTimeoutSeconds,
        bool supportsSyntheticHostReplayItems,
        string routedUserRequest,
        ChatOptions options,
        IReadOnlyDictionary<string, bool> mutatingToolHintsByName,
        List<ToolCallDto> toolCalls,
        List<ToolOutputDto> toolOutputs,
        int toolRounds,
        int projectionFallbackCount,
        CancellationToken turnToken,
        NoExtractedRecoveryExecutionDecision decision) {
        switch (decision.Kind) {
            case NoExtractedRecoveryExecutionDecisionKind.AutoPendingActionReplay:
                var replayOptions = await CopyChatOptionsWithPromptAwareToolOrderingAndEmitStatusAsync(
                        writer,
                        request.RequestId,
                        threadId,
                        options,
                        decision.ReplayPayload,
                        strategy: "prompt_replay",
                        newThreadOverride: false)
                    .ConfigureAwait(false);
                return (
                    Turn: await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            ChatInput.FromText(decision.ReplayPayload),
                            replayOptions,
                            turnToken,
                            phaseStatus: planExecuteReviewLoop ? ChatStatusCodes.PhaseExecute : ChatStatusCodes.Thinking,
                            phaseMessage: decision.ExecutePhaseMessage,
                            heartbeatLabel: "Executing selected action",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false),
                    ToolRounds: toolRounds,
                    ProjectionFallbackCount: projectionFallbackCount);
            case NoExtractedRecoveryExecutionDecisionKind.CarryoverStructuredNextActionReplay:
            case NoExtractedRecoveryExecutionDecisionKind.BackgroundWorkReadyReplay:
            case NoExtractedRecoveryExecutionDecisionKind.HostDomainIntentBootstrapReplay:
            case NoExtractedRecoveryExecutionDecisionKind.HostDomainIntentOperationalReplay:
                return await ExecuteSingleRecoveryHostToolReplayAsync(
                        client,
                        writer,
                        request,
                        threadId,
                        continuationFollowUpTurn,
                        round,
                        maxRounds,
                        parallelTools,
                        allowMutatingParallel,
                        planExecuteReviewLoop,
                        modelHeartbeatSeconds,
                        toolTimeoutSeconds,
                        supportsSyntheticHostReplayItems,
                        routedUserRequest,
                        options,
                        mutatingToolHintsByName,
                        toolCalls,
                        toolOutputs,
                        toolRounds,
                        projectionFallbackCount,
                        turnToken,
                        decision)
                    .ConfigureAwait(false);
            default:
                throw new InvalidOperationException("Recovery execution decision must be non-empty before applying.");
        }
    }

    private async Task<(TurnInfo Turn, int ToolRounds, int ProjectionFallbackCount)> ExecuteSingleRecoveryHostToolReplayAsync(
        IntelligenceXClient client,
        StreamWriter writer,
        ChatRequest request,
        string threadId,
        bool continuationFollowUpTurn,
        int round,
        int maxRounds,
        bool parallelTools,
        bool allowMutatingParallel,
        bool planExecuteReviewLoop,
        int modelHeartbeatSeconds,
        int toolTimeoutSeconds,
        bool supportsSyntheticHostReplayItems,
        string routedUserRequest,
        ChatOptions options,
        IReadOnlyDictionary<string, bool> mutatingToolHintsByName,
        List<ToolCallDto> toolCalls,
        List<ToolOutputDto> toolOutputs,
        int toolRounds,
        int projectionFallbackCount,
        CancellationToken turnToken,
        NoExtractedRecoveryExecutionDecision decision) {
        var toolCall = decision.ToolCall ?? throw new InvalidOperationException("Host replay decision requires a tool call.");
        Trace.WriteLine(
            $"[{ResolveRecoveryExecutionTraceLabel(decision.Kind)}] outcome=execute reason={decision.Reason} continuation={continuationFollowUpTurn} tool={toolCall.Name} prior_calls={toolCalls.Count} prior_outputs={toolOutputs.Count}");

        toolRounds++;
        var hostRoundNumber = round + 1;
        await WriteToolRoundStartedStatusAsync(
                writer,
                request.RequestId,
                threadId,
                hostRoundNumber,
                maxRounds,
                1,
                parallelTools,
                allowMutatingParallel)
            .ConfigureAwait(false);
        if (planExecuteReviewLoop) {
            await TryWriteStatusAsync(
                    writer,
                    request.RequestId,
                    threadId,
                    status: ChatStatusCodes.PhaseExecute,
                    message: decision.ExecutePhaseMessage)
                .ConfigureAwait(false);
        }

        var backgroundWorkItems = Array.Empty<ThreadBackgroundWorkItem>();
        if (decision.Kind == NoExtractedRecoveryExecutionDecisionKind.BackgroundWorkReadyReplay
            && !string.IsNullOrWhiteSpace(decision.BackgroundWorkItemId)
            && TryGetThreadBackgroundWorkItem(threadId, decision.BackgroundWorkItemId, out var backgroundWorkItem)) {
            backgroundWorkItems = new[] { backgroundWorkItem };
        }

        if (decision.Kind == NoExtractedRecoveryExecutionDecisionKind.BackgroundWorkReadyReplay) {
            await TryWriteStatusAsync(
                    writer,
                    request.RequestId,
                    threadId,
                    status: ChatStatusCodes.BackgroundWorkRunning,
                    message: BuildBackgroundWorkRunningStatusMessage(runningCount: 1, backgroundWorkItems))
                .ConfigureAwait(false);
        }

        await TryWriteStatusAsync(
                writer,
                request.RequestId,
                threadId,
                status: ChatStatusCodes.ToolCall,
                toolName: toolCall.Name,
                toolCallId: toolCall.CallId)
            .ConfigureAwait(false);
        toolCalls.Add(new ToolCallDto {
            CallId = toolCall.CallId,
            Name = toolCall.Name,
            ArgumentsJson = toolCall.Arguments is null
                ? "{}"
                : JsonLite.Serialize(toolCall.Arguments)
        });

        var calls = new[] { toolCall };
        var outputs = await ExecuteToolsAsync(
                writer,
                request.RequestId,
                threadId,
                calls,
                parallel: false,
                allowMutatingParallel: allowMutatingParallel,
                mutatingToolHintsByName: mutatingToolHintsByName,
                toolTimeoutSeconds: toolTimeoutSeconds,
                userRequest: routedUserRequest,
                cancellationToken: turnToken)
            .ConfigureAwait(false);
        var failedCalls = CountFailedToolOutputs(outputs);
        await WriteToolRoundCompletedStatusAsync(
                writer,
                request.RequestId,
                threadId,
                hostRoundNumber,
                maxRounds,
                outputs.Count,
                failedCalls)
            .ConfigureAwait(false);
        UpdateToolRoutingStats(calls, outputs);
        if (decision.RememberSuccessfulPreflightCalls) {
            RememberSuccessfulPackPreflightCalls(threadId, calls, outputs);
            RememberFailedPackPreflightCalls(threadId, calls, outputs);
        }

        foreach (var output in outputs) {
            if (WasProjectionFallbackApplied(output)) {
                projectionFallbackCount++;
            }

            toolOutputs.Add(new ToolOutputDto {
                CallId = output.CallId,
                Output = output.Output,
                Ok = output.Ok,
                ErrorCode = output.ErrorCode,
                Error = output.Error,
                Hints = output.Hints,
                IsTransient = output.IsTransient,
                SummaryMarkdown = output.SummaryMarkdown,
                MetaJson = output.MetaJson,
                RenderJson = output.RenderJson,
                FailureJson = output.FailureJson
            });
        }

        if (decision.Kind == NoExtractedRecoveryExecutionDecisionKind.BackgroundWorkReadyReplay
            && !string.IsNullOrWhiteSpace(decision.BackgroundWorkItemId)) {
            RememberBackgroundWorkExecutionOutcome(threadId, decision.BackgroundWorkItemId, toolCall.CallId, outputs);
            if (outputs.Count > 0 && outputs[0].Ok == true) {
                if (TryGetThreadBackgroundWorkItem(threadId, decision.BackgroundWorkItemId, out var completedBackgroundWorkItem)) {
                    backgroundWorkItems = new[] { completedBackgroundWorkItem };
                }

                await TryWriteStatusAsync(
                        writer,
                        request.RequestId,
                        threadId,
                        status: ChatStatusCodes.BackgroundWorkCompleted,
                        message: BuildBackgroundWorkCompletedStatusMessage(completedCount: 1, backgroundWorkItems))
                    .ConfigureAwait(false);
            }
        }

        ChatInput nextInput;
        string? promptTextForOrdering;
        var promptOrderingStrategy = "prompt_review";
        var nextPhaseStatus = planExecuteReviewLoop ? ChatStatusCodes.PhaseReview : ChatStatusCodes.Thinking;
        if (decision.Kind == NoExtractedRecoveryExecutionDecisionKind.HostDomainIntentBootstrapReplay) {
            var continuationPrompt = BuildHostDomainBootstrapContinuationPrompt(routedUserRequest, toolCall, outputs);
            nextInput = BuildHostReplayContinuationInput(
                continuationPrompt,
                routedUserRequest,
                toolCall,
                outputs,
                supportsSyntheticHostReplayItems,
                out promptTextForOrdering);
            promptOrderingStrategy = "prompt_recovery";
            nextPhaseStatus = planExecuteReviewLoop ? ChatStatusCodes.PhasePlan : ChatStatusCodes.Thinking;
        } else {
            nextInput = BuildHostReplayReviewInput(
                toolCall,
                outputs,
                supportsSyntheticHostReplayItems,
                out promptTextForOrdering);
        }

        var reviewOptions = await CopyChatOptionsWithPromptAwareToolOrderingAndEmitStatusAsync(
                writer,
                request.RequestId,
                threadId,
                options,
                promptTextForOrdering,
                strategy: promptOrderingStrategy,
                newThreadOverride: false)
            .ConfigureAwait(false);
        var turn = await RunModelPhaseWithProgressAsync(
                client,
                writer,
                request.RequestId,
                threadId,
                nextInput,
                reviewOptions,
                turnToken,
                phaseStatus: nextPhaseStatus,
                phaseMessage: decision.ReviewPhaseMessage,
                heartbeatLabel: decision.ReviewHeartbeatLabel,
                heartbeatSeconds: modelHeartbeatSeconds)
            .ConfigureAwait(false);

        return (turn, toolRounds, projectionFallbackCount);
    }

    private static string ResolveRecoveryExecutionTraceLabel(NoExtractedRecoveryExecutionDecisionKind kind) {
        return kind switch {
            NoExtractedRecoveryExecutionDecisionKind.CarryoverStructuredNextActionReplay => "host-structured-next-action",
            NoExtractedRecoveryExecutionDecisionKind.BackgroundWorkReadyReplay => "background-work-replay",
            NoExtractedRecoveryExecutionDecisionKind.HostDomainIntentBootstrapReplay => "host-domain-bootstrap",
            NoExtractedRecoveryExecutionDecisionKind.HostDomainIntentOperationalReplay => "host-domain-operational",
            _ => "recovery-execution"
        };
    }

    internal (string Kind, string Reason, string? ActionId, string? ToolName, bool ExpandToFullToolAvailability) ResolveNoExtractedRecoveryPrePromptExecutionDecisionForTesting(
        string threadId,
        string userRequest,
        string assistantDraft,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyDictionary<string, bool> mutatingToolHintsByName,
        bool continuationFollowUpTurn,
        bool compactFollowUpTurn,
        bool autoPendingActionReplayUsed,
        bool hostStructuredNextActionReplayUsed,
        int priorToolCalls,
        int priorToolOutputs) {
        var decision = ResolveNoExtractedRecoveryPrePromptExecutionDecision(
            threadId: threadId,
            userRequest: userRequest,
            assistantDraft: assistantDraft,
            toolDefinitions: toolDefinitions,
            mutatingToolHintsByName: mutatingToolHintsByName,
            continuationFollowUpTurn: continuationFollowUpTurn,
            compactFollowUpTurn: compactFollowUpTurn,
            autoPendingActionReplayUsed: autoPendingActionReplayUsed,
            hostStructuredNextActionReplayUsed: hostStructuredNextActionReplayUsed,
            explicitToolReferenceInUserRequest: ExtractExplicitRequestedToolNames(userRequest).Length > 0,
            priorToolCalls: priorToolCalls,
            priorToolOutputs: priorToolOutputs);
        return (
            decision.Kind.ToString(),
            decision.Reason,
            string.IsNullOrWhiteSpace(decision.ActionId)
                ? string.IsNullOrWhiteSpace(decision.BackgroundWorkItemId) ? null : decision.BackgroundWorkItemId
                : decision.ActionId,
            decision.ToolCall?.Name,
            decision.ExpandToFullToolAvailability);
    }

    internal (string Kind, string Reason, string? ToolName, bool ExpandToFullToolAvailability) ResolveNoExtractedRecoveryPostPromptExecutionDecisionForTesting(
        string threadId,
        string userRequest,
        string? routedUserRequest,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        bool executionContractApplies,
        bool hostDomainIntentBootstrapReplayUsed,
        bool bootstrapOnlyToolActivity,
        int priorToolCalls,
        int priorToolOutputs) {
        var decision = ResolveNoExtractedRecoveryPostPromptExecutionDecision(
            threadId: threadId,
            visibleUserRequest: userRequest,
            routedUserRequest: string.IsNullOrWhiteSpace(routedUserRequest) ? userRequest : routedUserRequest,
            toolDefinitions: toolDefinitions,
            executionContractApplies: executionContractApplies,
            hostDomainIntentBootstrapReplayUsed: hostDomainIntentBootstrapReplayUsed,
            bootstrapOnlyToolActivity: bootstrapOnlyToolActivity,
            priorToolCalls: priorToolCalls,
            priorToolOutputs: priorToolOutputs);
        return (
            decision.Kind.ToString(),
            decision.Reason,
            decision.ToolCall?.Name,
            decision.ExpandToFullToolAvailability);
    }
}
