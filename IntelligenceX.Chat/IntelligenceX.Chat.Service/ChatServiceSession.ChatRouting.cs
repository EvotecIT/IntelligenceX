using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JsonValueKind = System.Text.Json.JsonValueKind;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const string ParallelToolModeAuto = "auto";
    private const string ParallelToolModeForceSerial = "force_serial";
    private const string ParallelToolModeAllowParallel = "allow_parallel";
    private const string ResponseReviewMarker = "ix:response-review:v1";
    private const string ProactiveModeMarker = "ix:proactive-mode:v1";
    private const string ProactiveFollowUpMarker = "ix:proactive-followup:v1";
    private const int DefaultMaxReviewPasses = 1;
    private const int MaxReviewPassesLimit = 3;
    private const int DefaultModelHeartbeatSeconds = 8;
    private const int MaxModelHeartbeatSeconds = 60;

    private sealed record ChatTurnRunResult(
        ChatResultMessage Result,
        TurnUsage? Usage,
        int ToolCallsCount,
        int ToolRounds,
        int ProjectionFallbackCount,
        IReadOnlyList<ToolErrorMetricDto> ToolErrors);

    private static (bool ParallelTools, bool AllowMutatingParallel, string Mode) ResolveParallelToolExecutionMode(ChatRequestOptions? options,
        bool serviceDefaultParallelTools) {
        var requestedParallelTools = options?.ParallelTools ?? serviceDefaultParallelTools;
        var mode = NormalizeParallelToolMode(options?.ParallelToolMode);
        return mode switch {
            ParallelToolModeForceSerial => (false, false, ParallelToolModeForceSerial),
            ParallelToolModeAllowParallel => (true, true, ParallelToolModeAllowParallel),
            _ => (requestedParallelTools, false, ParallelToolModeAuto)
        };
    }

    private static string NormalizeParallelToolMode(string? mode) {
        var normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            "allow_parallel" => ParallelToolModeAllowParallel,
            "allow-parallel" => ParallelToolModeAllowParallel,
            "allowparallel" => ParallelToolModeAllowParallel,
            "on" => ParallelToolModeAllowParallel,
            "force_serial" => ParallelToolModeForceSerial,
            "force-serial" => ParallelToolModeForceSerial,
            "forceserial" => ParallelToolModeForceSerial,
            "serial" => ParallelToolModeForceSerial,
            "off" => ParallelToolModeForceSerial,
            _ => ParallelToolModeAuto
        };
    }

    private static int ResolveMaxReviewPasses(ChatRequestOptions? options) {
        var configured = options?.MaxReviewPasses;
        if (!configured.HasValue || configured.Value <= 0) {
            return DefaultMaxReviewPasses;
        }

        return Math.Clamp(configured.Value, 0, MaxReviewPassesLimit);
    }

    private static int ResolveModelHeartbeatSeconds(ChatRequestOptions? options) {
        var configured = options?.ModelHeartbeatSeconds;
        if (!configured.HasValue) {
            return DefaultModelHeartbeatSeconds;
        }

        return Math.Clamp(configured.Value, 0, MaxModelHeartbeatSeconds);
    }

    private static bool TryReadProactiveModeFromRequestText(string requestText, out bool enabled) {
        enabled = false;
        var text = requestText ?? string.Empty;
        if (text.Length == 0) {
            return false;
        }

        var markerIndex = text.IndexOf(ProactiveModeMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0) {
            return false;
        }

        var tailLength = Math.Min(280, text.Length - markerIndex);
        if (tailLength <= 0) {
            return false;
        }

        var tail = text.Substring(markerIndex, tailLength);
        if (tail.IndexOf("enabled: true", StringComparison.OrdinalIgnoreCase) >= 0) {
            enabled = true;
            return true;
        }

        if (tail.IndexOf("enabled: false", StringComparison.OrdinalIgnoreCase) >= 0) {
            enabled = false;
            return true;
        }

        return false;
    }

    private static bool ShouldAttemptResponseQualityReview(
        string userRequest,
        string assistantDraft,
        bool executionContractApplies,
        bool hasToolActivity,
        int reviewPassesUsed,
        int maxReviewPasses) {
        if (maxReviewPasses <= 0 || reviewPassesUsed >= maxReviewPasses) {
            return false;
        }

        if (executionContractApplies && !hasToolActivity) {
            return false;
        }

        var request = (userRequest ?? string.Empty).Trim();
        var draft = (assistantDraft ?? string.Empty).Trim();
        if (request.Length == 0 || draft.Length == 0 || draft.Length > 2400) {
            return false;
        }

        if (draft.Contains(ResponseReviewMarker, StringComparison.OrdinalIgnoreCase)
            || draft.Contains(ExecutionCorrectionMarker, StringComparison.OrdinalIgnoreCase)
            || draft.Contains(ExecutionWatchdogMarker, StringComparison.OrdinalIgnoreCase)
            || draft.Contains(ExecutionContractMarker, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var tokenCount = CountLetterDigitTokens(draft, maxTokens: 96);
        if (tokenCount <= 0) {
            return false;
        }

        if (tokenCount <= 18 && draft.Length <= 220) {
            return true;
        }

        if (!hasToolActivity && tokenCount <= 36 && draft.Length <= 320) {
            return true;
        }

        return draft.Contains('?', StringComparison.Ordinal) && tokenCount <= 48 && draft.Length <= 360;
    }

    private static string BuildResponseQualityReviewPrompt(string userRequest, string assistantDraft, bool hasToolActivity, int reviewPassNumber,
        int maxReviewPasses) {
        var requestText = TrimForPrompt(userRequest, 520);
        var draftText = TrimForPrompt(assistantDraft, 1600);
        var toolActivityHint = hasToolActivity ? "present" : "none";
        var pass = Math.Max(1, reviewPassNumber);
        var maxPasses = Math.Max(pass, maxReviewPasses);
        return $$"""
            [Response quality review]
            {{ResponseReviewMarker}}
            Review pass {{pass}}/{{maxPasses}}.

            User request:
            {{requestText}}

            Current assistant draft:
            {{draftText}}

            Tool activity this turn: {{toolActivityHint}}.

            Rewrite the assistant response so it is helpful, direct, and action-oriented.
            Do not invent tool outputs.
            If a blocker exists, state the exact blocker and the minimal missing input.
            Return only the revised assistant response text.
            """;
    }

    private static bool ShouldAttemptProactiveFollowUpReview(
        bool proactiveModeEnabled,
        bool hasToolActivity,
        bool proactiveFollowUpUsed,
        string assistantDraft) {
        if (!proactiveModeEnabled || !hasToolActivity || proactiveFollowUpUsed) {
            return false;
        }

        var draft = (assistantDraft ?? string.Empty).Trim();
        if (draft.Length == 0 || draft.Length > 2800) {
            return false;
        }

        if (draft.Contains(ProactiveFollowUpMarker, StringComparison.OrdinalIgnoreCase)
            || draft.Contains(ResponseReviewMarker, StringComparison.OrdinalIgnoreCase)
            || draft.Contains(ExecutionContractMarker, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return ExtractPendingActions(draft).Count == 0;
    }

    private static string BuildProactiveFollowUpReviewPrompt(string userRequest, string assistantDraft) {
        var requestText = TrimForPrompt(userRequest, 520);
        var draftText = TrimForPrompt(assistantDraft, 1800);
        return $$"""
            [Proactive follow-up review]
            {{ProactiveFollowUpMarker}}
            Expand the response with proactive intelligence based on current tool findings.

            User request:
            {{requestText}}

            Current assistant draft:
            {{draftText}}

            Requirements:
            - Keep all existing factual findings that are already supported by tool output.
            - Add a short "Potential issues to verify" section (1-3 bullets).
            - Add a short "Recommended next fixes" section (1-3 bullets).
            - Do not invent tool outputs or claim completed actions that were not executed.
            Return only the revised assistant response text.
            """;
    }

    private async Task RunPhaseProgressLoopAsync(
        StreamWriter writer,
        string requestId,
        string threadId,
        string phaseStatus,
        string? phaseMessage,
        string heartbeatLabel,
        int heartbeatSeconds,
        CancellationToken cancellationToken,
        Task phaseTask) {
        var status = string.IsNullOrWhiteSpace(phaseStatus) ? "thinking" : phaseStatus.Trim();
        if (!string.IsNullOrWhiteSpace(phaseMessage)) {
            await TryWriteStatusAsync(writer, requestId, threadId, status: status, message: phaseMessage).ConfigureAwait(false);
        }

        if (heartbeatSeconds <= 0) {
            await phaseTask.ConfigureAwait(false);
            return;
        }

        var heartbeatInterval = TimeSpan.FromSeconds(Math.Max(1, heartbeatSeconds));
        var sw = Stopwatch.StartNew();
        var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        while (!phaseTask.IsCompleted) {
            var heartbeatDelayTask = Task.Delay(heartbeatInterval);
            var completedTask = await Task.WhenAny(phaseTask, heartbeatDelayTask, cancellationTask).ConfigureAwait(false);
            if (ReferenceEquals(completedTask, phaseTask) || ReferenceEquals(completedTask, cancellationTask)) {
                break;
            }

            var elapsedSeconds = Math.Max(1, (int)Math.Round(sw.Elapsed.TotalSeconds));
            await TryWriteStatusAsync(
                    writer,
                    requestId,
                    threadId,
                    status: "phase_heartbeat",
                    durationMs: sw.ElapsedMilliseconds,
                    message: $"{heartbeatLabel}... ({elapsedSeconds}s)")
                .ConfigureAwait(false);
        }

        await phaseTask.ConfigureAwait(false);
    }

    private async Task<TurnInfo> RunModelPhaseWithProgressAsync(
        IntelligenceXClient client,
        StreamWriter writer,
        string requestId,
        string threadId,
        ChatInput input,
        ChatOptions options,
        CancellationToken cancellationToken,
        string phaseStatus,
        string phaseMessage,
        string heartbeatLabel,
        int heartbeatSeconds) {
        var chatTask = ChatWithToolSchemaRecoveryAsync(client, input, options, cancellationToken);
        await RunPhaseProgressLoopAsync(
                writer,
                requestId,
                threadId,
                phaseStatus,
                phaseMessage,
                heartbeatLabel,
                heartbeatSeconds,
                cancellationToken,
                chatTask)
            .ConfigureAwait(false);
        return await chatTask.ConfigureAwait(false);
    }

    private static ChatOptions CopyChatOptions(ChatOptions options, bool? newThreadOverride = null) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        var copy = options.Clone();
        if (newThreadOverride.HasValue) {
            copy.NewThread = newThreadOverride.Value;
        }
        return copy;
    }

    private async Task<ChatTurnRunResult> RunChatOnCurrentThreadAsync(IntelligenceXClient client, StreamWriter writer, ChatRequest request, string threadId,
        CancellationToken cancellationToken) {
        var toolCalls = new List<ToolCallDto>();
        var toolOutputs = new List<ToolOutputDto>();
        var toolRounds = 0;
        var projectionFallbackCount = 0;

        IReadOnlyList<ToolDefinition> toolDefs = _registry.GetDefinitions();
        if (request.Options?.DisabledTools is { Length: > 0 } disabledTools && toolDefs.Count > 0) {
            var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < disabledTools.Length; i++) {
                if (!string.IsNullOrWhiteSpace(disabledTools[i])) {
                    disabled.Add(disabledTools[i].Trim());
                }
            }

            if (disabled.Count > 0) {
                var filtered = new List<ToolDefinition>(toolDefs.Count);
                for (var i = 0; i < toolDefs.Count; i++) {
                    if (!disabled.Contains(toolDefs[i].Name)) {
                        filtered.Add(toolDefs[i]);
                    }
                }
                toolDefs = filtered;
            }
        }
        toolDefs = SanitizeToolDefinitions(toolDefs);
        var fullToolDefs = toolDefs.Count == 0 ? Array.Empty<ToolDefinition>() : toolDefs.ToArray();
        var originalToolCount = toolDefs.Count;
        var routingInsights = new List<ToolRoutingInsight>();
        var weightedToolRouting = request.Options?.WeightedToolRouting ?? true;
        var maxCandidateTools = request.Options?.MaxCandidateTools;
        var userRequest = ExtractPrimaryUserRequest(request.Text);
        var userIntent = ExtractIntentUserText(request.Text);
        RememberUserIntent(threadId, userIntent);
        var routedUserRequest = ExpandContinuationUserRequest(threadId, userRequest);
        var executionContractApplies = ShouldEnforceExecuteOrExplainContract(routedUserRequest);
        var proactiveModeEnabled = TryReadProactiveModeFromRequestText(request.Text, out var proactiveMode) && proactiveMode;
        var usedContinuationSubset = false;
        if (weightedToolRouting && toolDefs.Count > 0) {
            if (!executionContractApplies) {
                if (!TryGetContinuationToolSubset(threadId, userRequest, toolDefs, out var continuationSubset)) {
                    var routed = await SelectWeightedToolSubsetAsync(
                            client,
                            threadId,
                            toolDefs,
                            routedUserRequest,
                            maxCandidateTools,
                            cancellationToken)
                        .ConfigureAwait(false);
                    toolDefs = routed.Definitions;
                    routingInsights = routed.Insights;
                } else {
                    toolDefs = continuationSubset;
                    routingInsights = BuildContinuationRoutingInsights(toolDefs);
                    usedContinuationSubset = true;
                }
            } else {
                // Explicit action-selection turns should preserve the full tool set to maximize
                // first-pass execution reliability.
                routingInsights = new List<ToolRoutingInsight>();
            }
            RememberWeightedToolSubset(threadId, toolDefs, originalToolCount);
        }

        var (parallelTools, allowMutatingParallel, parallelToolMode) = ResolveParallelToolExecutionMode(request.Options, _options.ParallelTools);
        var maxRounds = request.Options?.MaxToolRounds ?? _options.MaxToolRounds;
        var turnTimeoutSeconds = request.Options?.TurnTimeoutSeconds ?? _options.TurnTimeoutSeconds;
        var toolTimeoutSeconds = request.Options?.ToolTimeoutSeconds ?? _options.ToolTimeoutSeconds;
        using var turnCts = CreateTimeoutCts(cancellationToken, turnTimeoutSeconds);
        var turnToken = turnCts?.Token ?? cancellationToken;

        var options = new ChatOptions {
            Model = request.Options?.Model ?? _options.Model,
            Instructions = string.IsNullOrWhiteSpace(_instructions) ? null : _instructions,
            ParallelToolCalls = parallelTools,
            Tools = toolDefs.Count == 0 ? null : toolDefs,
            ToolChoice = toolDefs.Count == 0 ? null : ToolChoice.Auto
        };
        var planExecuteReviewLoop = request.Options?.PlanExecuteReviewLoop ?? true;
        var maxReviewPasses = ResolveMaxReviewPasses(request.Options);
        var modelHeartbeatSeconds = ResolveModelHeartbeatSeconds(request.Options);

        if (!string.Equals(parallelToolMode, ParallelToolModeAuto, StringComparison.Ordinal)) {
            await TryWriteStatusAsync(
                    writer,
                    request.RequestId,
                    threadId,
                    status: "tool_parallel_mode",
                    message: $"Tool parallel mode: {parallelToolMode}.")
                .ConfigureAwait(false);
        }

        if (weightedToolRouting && originalToolCount > 0 && toolDefs.Count > 0 && toolDefs.Count < originalToolCount) {
            await TryWriteStatusAsync(
                    writer,
                    request.RequestId,
                    threadId,
                    status: "routing",
                    message: $"Tool routing selected {toolDefs.Count} of {originalToolCount} tools for this turn.")
                .ConfigureAwait(false);
            await EmitRoutingInsightsAsync(writer, request.RequestId, threadId, routingInsights).ConfigureAwait(false);
        }

        TurnInfo turn = await RunModelPhaseWithProgressAsync(
                client,
                writer,
                request.RequestId,
                threadId,
                ChatInput.FromText(request.Text),
                CopyChatOptions(options),
                turnToken,
                phaseStatus: planExecuteReviewLoop ? "phase_plan" : "thinking",
                phaseMessage: planExecuteReviewLoop ? "Planning next steps with available tools..." : "Reasoning with available tools...",
                heartbeatLabel: planExecuteReviewLoop ? "Planning next steps" : "Reasoning",
                heartbeatSeconds: modelHeartbeatSeconds)
            .ConfigureAwait(false);
        var reviewPassesUsed = 0;
        var executionNudgeUsed = false;
        var toolReceiptCorrectionUsed = false;
        var noToolExecutionWatchdogUsed = false;
        var executionContractEscapeUsed = false;
        var continuationSubsetEscapeUsed = false;
        var autoPendingActionReplayUsed = false;
        var proactiveFollowUpUsed = false;

        for (var round = 0; round < Math.Max(1, maxRounds); round++) {
            var extracted = ToolCallParser.Extract(turn);
            if (extracted.Count == 0) {
                var text = EasyChatResult.FromTurn(turn).Text ?? string.Empty;

                if (!autoPendingActionReplayUsed
                    && toolCalls.Count == 0
                    && toolOutputs.Count == 0
                    && LooksLikeContinuationFollowUp(userRequest)
                    && TryBuildSinglePendingActionSelectionPayload(text, out var autoSelectionPayload, out var autoActionId)) {
                    autoPendingActionReplayUsed = true;
                    routedUserRequest = autoSelectionPayload;
                    executionContractApplies = ShouldEnforceExecuteOrExplainContract(routedUserRequest);

                    turn = await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            ChatInput.FromText(autoSelectionPayload),
                            CopyChatOptions(options, newThreadOverride: false),
                            turnToken,
                            phaseStatus: planExecuteReviewLoop ? "phase_execute" : "thinking",
                            phaseMessage: $"Executing follow-up action {autoActionId} directly.",
                            heartbeatLabel: "Executing selected action",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    continue;
                }

                var shouldAttemptExecutionNudge = false;
                var executionNudgeReason = executionNudgeUsed
                    ? "execution_nudge_already_used"
                    : "execution_nudge_not_evaluated";
                if (!executionNudgeUsed) {
                    shouldAttemptExecutionNudge = EvaluateToolExecutionNudgeDecision(
                        userRequest: routedUserRequest,
                        assistantDraft: text,
                        toolsAvailable: toolDefs.Count > 0,
                        priorToolCalls: toolCalls.Count,
                        assistantDraftToolCalls: extracted.Count,
                        usedContinuationSubset: usedContinuationSubset,
                        out executionNudgeReason);
                }

                if (shouldAttemptExecutionNudge) {
                    TraceToolExecutionNudgeDecision(
                        userRequest: routedUserRequest,
                        usedContinuationSubset: usedContinuationSubset,
                        toolsAvailable: toolDefs.Count > 0,
                        priorToolCalls: toolCalls.Count,
                        assistantDraftToolCalls: extracted.Count,
                        executionNudgeAlreadyUsed: executionNudgeUsed,
                        shouldAttemptNudge: shouldAttemptExecutionNudge,
                        reason: executionNudgeReason);
                    executionNudgeUsed = true;
                    var nudgePrompt = BuildToolExecutionNudgePrompt(routedUserRequest, text);
                    turn = await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            ChatInput.FromText(nudgePrompt),
                            CopyChatOptions(options, newThreadOverride: false),
                            turnToken,
                            phaseStatus: planExecuteReviewLoop ? "phase_plan" : "thinking",
                            phaseMessage: "Re-planning to execute available tools in this turn.",
                            heartbeatLabel: "Re-planning execution",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    continue;
                }

                TraceToolExecutionNudgeDecision(
                    userRequest: routedUserRequest,
                    usedContinuationSubset: usedContinuationSubset,
                    toolsAvailable: toolDefs.Count > 0,
                    priorToolCalls: toolCalls.Count,
                    assistantDraftToolCalls: extracted.Count,
                    executionNudgeAlreadyUsed: executionNudgeUsed,
                    shouldAttemptNudge: false,
                    reason: executionNudgeReason);

                if (!toolReceiptCorrectionUsed
                    && ShouldAttemptToolReceiptCorrection(
                        userRequest: routedUserRequest,
                        assistantDraft: text,
                        tools: toolDefs,
                        priorToolCalls: toolCalls.Count,
                        priorToolOutputs: toolOutputs.Count,
                        assistantDraftToolCalls: extracted.Count)) {
                    toolReceiptCorrectionUsed = true;
                    var correctionPrompt = BuildToolReceiptCorrectionPrompt(routedUserRequest, text);
                    turn = await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            ChatInput.FromText(correctionPrompt),
                            CopyChatOptions(options, newThreadOverride: false),
                            turnToken,
                            phaseStatus: planExecuteReviewLoop ? "phase_plan" : "thinking",
                            phaseMessage: "Re-planning to correct an inconsistent tool receipt in this turn.",
                            heartbeatLabel: "Re-planning tool receipt",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    continue;
                }

                var shouldAttemptWatchdog = ShouldAttemptNoToolExecutionWatchdog(
                    userRequest: routedUserRequest,
                    assistantDraft: text,
                    toolsAvailable: toolDefs.Count > 0,
                    priorToolCalls: toolCalls.Count,
                    priorToolOutputs: toolOutputs.Count,
                    assistantDraftToolCalls: extracted.Count,
                    executionNudgeUsed: executionNudgeUsed,
                    toolReceiptCorrectionUsed: toolReceiptCorrectionUsed,
                    watchdogAlreadyUsed: noToolExecutionWatchdogUsed,
                    out var watchdogReason);
                TraceNoToolExecutionWatchdogDecision(
                    userRequest: routedUserRequest,
                    executionContractApplies: executionContractApplies,
                    toolsAvailable: toolDefs.Count > 0,
                    priorToolCalls: toolCalls.Count,
                    priorToolOutputs: toolOutputs.Count,
                    assistantDraftToolCalls: extracted.Count,
                    executionNudgeUsed: executionNudgeUsed,
                    toolReceiptCorrectionUsed: toolReceiptCorrectionUsed,
                    watchdogAlreadyUsed: noToolExecutionWatchdogUsed,
                    shouldRetry: shouldAttemptWatchdog,
                    reason: watchdogReason);
                if (shouldAttemptWatchdog) {
                    noToolExecutionWatchdogUsed = true;
                    var watchdogPrompt = BuildNoToolExecutionWatchdogPrompt(routedUserRequest, text);
                    turn = await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            ChatInput.FromText(watchdogPrompt),
                            CopyChatOptions(options, newThreadOverride: false),
                            turnToken,
                            phaseStatus: planExecuteReviewLoop ? "phase_review" : "thinking",
                            phaseMessage: "Re-validating tool execution for this turn.",
                            heartbeatLabel: "Re-validating execution",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    continue;
                }

                var hasToolActivity = toolCalls.Count > 0 || toolOutputs.Count > 0;
                if (executionContractApplies
                    && !hasToolActivity
                    && !executionContractEscapeUsed
                    && fullToolDefs.Length > 0) {
                    executionContractEscapeUsed = true;
                    toolDefs = fullToolDefs;
                    options.Tools = fullToolDefs;
                    options.ToolChoice = ToolChoice.Auto;
                    usedContinuationSubset = false;
                    RememberWeightedToolSubset(threadId, toolDefs, originalToolCount);

                    var escapePrompt = BuildExecutionContractEscapePrompt(routedUserRequest, text);
                    turn = await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            ChatInput.FromText(escapePrompt),
                            CopyChatOptions(options, newThreadOverride: false),
                            turnToken,
                            phaseStatus: planExecuteReviewLoop ? "phase_plan" : "thinking",
                            phaseMessage: "Selected action had no tool activity; retrying with full tool availability.",
                            heartbeatLabel: "Re-planning with full tools",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    continue;
                }

                var shouldAttemptContinuationSubsetEscape = ShouldAttemptContinuationSubsetEscape(
                    executionContractApplies: executionContractApplies,
                    usedContinuationSubset: usedContinuationSubset,
                    continuationSubsetEscapeUsed: continuationSubsetEscapeUsed,
                    toolsAvailable: fullToolDefs.Length > 0,
                    priorToolCalls: toolCalls.Count,
                    priorToolOutputs: toolOutputs.Count,
                    out _);
                if (shouldAttemptContinuationSubsetEscape) {
                    continuationSubsetEscapeUsed = true;
                    toolDefs = fullToolDefs;
                    options.Tools = fullToolDefs;
                    options.ToolChoice = ToolChoice.Auto;
                    usedContinuationSubset = false;
                    RememberWeightedToolSubset(threadId, toolDefs, originalToolCount);

                    var subsetEscapePrompt = BuildContinuationSubsetEscapePrompt(routedUserRequest, text);
                    turn = await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            ChatInput.FromText(subsetEscapePrompt),
                            CopyChatOptions(options, newThreadOverride: false),
                            turnToken,
                            phaseStatus: planExecuteReviewLoop ? "phase_plan" : "thinking",
                            phaseMessage: "Follow-up subset had no tool activity; retrying with full tool availability.",
                            heartbeatLabel: "Expanding follow-up tools",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    continue;
                }

                if (executionContractApplies && !hasToolActivity) {
                    var blockerReason = noToolExecutionWatchdogUsed
                        ? "no_tool_calls_after_watchdog_retry"
                        : $"execution_contract_unmet_{watchdogReason}";
                    text = BuildExecutionContractBlockerText(
                        userRequest: routedUserRequest,
                        assistantDraft: text,
                        reason: blockerReason);
                }

                if (planExecuteReviewLoop
                    && ShouldAttemptResponseQualityReview(
                        userRequest: routedUserRequest,
                        assistantDraft: text,
                        executionContractApplies: executionContractApplies,
                        hasToolActivity: hasToolActivity,
                        reviewPassesUsed: reviewPassesUsed,
                        maxReviewPasses: maxReviewPasses)) {
                    reviewPassesUsed++;
                    var reviewPrompt = BuildResponseQualityReviewPrompt(
                        userRequest: routedUserRequest,
                        assistantDraft: text,
                        hasToolActivity: hasToolActivity,
                        reviewPassNumber: reviewPassesUsed,
                        maxReviewPasses: maxReviewPasses);
                    turn = await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            ChatInput.FromText(reviewPrompt),
                            CopyChatOptions(options, newThreadOverride: false),
                            turnToken,
                            phaseStatus: "phase_review",
                            phaseMessage: $"Reviewing response quality ({reviewPassesUsed}/{maxReviewPasses})...",
                            heartbeatLabel: "Reviewing response",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    continue;
                }

                if (ShouldAttemptProactiveFollowUpReview(
                        proactiveModeEnabled: proactiveModeEnabled,
                        hasToolActivity: hasToolActivity,
                        proactiveFollowUpUsed: proactiveFollowUpUsed,
                        assistantDraft: text)) {
                    proactiveFollowUpUsed = true;
                    var proactivePrompt = BuildProactiveFollowUpReviewPrompt(routedUserRequest, text);
                    turn = await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            ChatInput.FromText(proactivePrompt),
                            CopyChatOptions(options, newThreadOverride: false),
                            turnToken,
                            phaseStatus: "phase_review",
                            phaseMessage: "Generating proactive next checks and fixes...",
                            heartbeatLabel: "Preparing proactive follow-up",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    continue;
                }

                text = AppendTurnCompletionNotice(text, turn);

                // Capture pending actions from the finalized assistant text so confirmation routing stays aligned
                // with what the user actually sees (including contract fallback substitutions).
                RememberPendingActions(threadId, text);

                if (_options.Redact) {
                    text = RedactText(text);
                }

                var result = new ChatResultMessage {
                    Kind = ChatServiceMessageKind.Response,
                    RequestId = request.RequestId,
                    ThreadId = threadId,
                    Text = text,
                    Tools = toolCalls.Count == 0 && toolOutputs.Count == 0
                        ? null
                        : new ToolRunDto { Calls = toolCalls.ToArray(), Outputs = toolOutputs.ToArray() }
                };
                return new ChatTurnRunResult(
                    Result: result,
                    Usage: turn.Usage,
                    ToolCallsCount: toolCalls.Count,
                    ToolRounds: toolRounds,
                    ProjectionFallbackCount: projectionFallbackCount,
                    ToolErrors: BuildToolErrorMetrics(toolCalls, toolOutputs));
            }

            toolRounds++;
            if (planExecuteReviewLoop) {
                await TryWriteStatusAsync(
                        writer,
                        request.RequestId,
                        threadId,
                        status: "phase_execute",
                        message: $"Executing {extracted.Count} planned tool call(s)...")
                    .ConfigureAwait(false);
            }

            foreach (var call in extracted) {
                await TryWriteStatusAsync(writer, request.RequestId, threadId, status: "tool_call", toolName: call.Name, toolCallId: call.CallId)
                    .ConfigureAwait(false);
                toolCalls.Add(new ToolCallDto {
                    CallId = call.CallId,
                    Name = call.Name,
                    ArgumentsJson = call.Arguments is null ? "{}" : JsonLite.Serialize(call.Arguments)
                });
            }

            var mutatingToolHints = BuildMutatingToolHintsByName(toolDefs);
            var executed = await ExecuteToolsAsync(writer, request.RequestId, threadId, extracted, parallelTools, allowMutatingParallel,
                    mutatingToolHints, toolTimeoutSeconds, turnToken)
                .ConfigureAwait(false);
            UpdateToolRoutingStats(extracted, executed);
            foreach (var output in executed) {
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

            var next = new ChatInput();
            foreach (var output in executed) {
                next.AddToolOutput(output.CallId, output.Output);
            }
            turn = await RunModelPhaseWithProgressAsync(
                    client,
                    writer,
                    request.RequestId,
                    threadId,
                    next,
                    CopyChatOptions(options, newThreadOverride: false),
                    turnToken,
                    phaseStatus: planExecuteReviewLoop ? "phase_review" : "thinking",
                    phaseMessage: planExecuteReviewLoop
                        ? $"Reviewing {executed.Count} tool result(s) and deciding next steps..."
                        : $"Analyzing {executed.Count} tool result(s)...",
                    heartbeatLabel: "Reviewing tool results",
                    heartbeatSeconds: modelHeartbeatSeconds)
                .ConfigureAwait(false);
        }

        throw new InvalidOperationException($"Tool runner exceeded max rounds ({maxRounds}).");
    }

    private static void TraceNoToolExecutionWatchdogDecision(
        string userRequest,
        bool executionContractApplies,
        bool toolsAvailable,
        int priorToolCalls,
        int priorToolOutputs,
        int assistantDraftToolCalls,
        bool executionNudgeUsed,
        bool toolReceiptCorrectionUsed,
        bool watchdogAlreadyUsed,
        bool shouldRetry,
        string reason) {
        if (!executionContractApplies) {
            return;
        }

        var normalized = (userRequest ?? string.Empty).Trim();
        var tokenCount = CountLetterDigitTokens(normalized, maxTokens: 16);
        var outcome = shouldRetry ? "retry" : "skip";
        var watchdogState = watchdogAlreadyUsed ? "used" : "unused";
        var nudgeState = executionNudgeUsed ? "used" : "unused";
        var receiptState = toolReceiptCorrectionUsed ? "used" : "unused";
        Console.Error.WriteLine(
            $"[tool-watchdog] outcome={outcome} reason={reason} contract=true watchdog={watchdogState} nudge={nudgeState} receipt={receiptState} tools={toolsAvailable} prior_calls={Math.Max(0, priorToolCalls)} prior_outputs={Math.Max(0, priorToolOutputs)} draft_calls={Math.Max(0, assistantDraftToolCalls)} tokens={tokenCount}");
    }

    private static void TraceToolExecutionNudgeDecision(
        string userRequest,
        bool usedContinuationSubset,
        bool toolsAvailable,
        int priorToolCalls,
        int assistantDraftToolCalls,
        bool executionNudgeAlreadyUsed,
        bool shouldAttemptNudge,
        string reason) {
        var normalized = (userRequest ?? string.Empty).Trim();
        var isFollowUp = LooksLikeContinuationFollowUp(normalized);
        var isActionPayload = LooksLikeActionSelectionPayload(normalized);
        if (!isFollowUp && !isActionPayload) {
            return;
        }

        var tokenCount = CountLetterDigitTokens(normalized, maxTokens: 16);
        var kind = isActionPayload ? "action_payload" : "follow_up";
        var routing = usedContinuationSubset ? "subset" : "full";
        var outcome = shouldAttemptNudge ? "retry" : "skip";
        var nudgeState = executionNudgeAlreadyUsed ? "used" : "unused";

        Console.Error.WriteLine(
            $"[tool-nudge] outcome={outcome} reason={reason} kind={kind} routing={routing} nudge={nudgeState} tools={toolsAvailable} prior_calls={Math.Max(0, priorToolCalls)} draft_calls={Math.Max(0, assistantDraftToolCalls)} tokens={tokenCount}");
    }

    private static IReadOnlyList<ToolErrorMetricDto> BuildToolErrorMetrics(
        IReadOnlyList<ToolCallDto> calls,
        IReadOnlyList<ToolOutputDto> outputs) {
        if (calls.Count == 0 || outputs.Count == 0) {
            return Array.Empty<ToolErrorMetricDto>();
        }

        var nameByCallId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < calls.Count; i++) {
            var call = calls[i];
            var callId = (call.CallId ?? string.Empty).Trim();
            var toolName = (call.Name ?? string.Empty).Trim();
            if (callId.Length == 0 || toolName.Length == 0) {
                continue;
            }

            nameByCallId[callId] = toolName;
        }

        if (nameByCallId.Count == 0) {
            return Array.Empty<ToolErrorMetricDto>();
        }

        var counts = new Dictionary<(string ToolName, string ErrorCode), int>();
        for (var i = 0; i < outputs.Count; i++) {
            var output = outputs[i];
            var callId = (output.CallId ?? string.Empty).Trim();
            if (callId.Length == 0 || !nameByCallId.TryGetValue(callId, out var toolName)) {
                continue;
            }

            var errorCode = NormalizeToolErrorCode(output);
            if (errorCode.Length == 0) {
                continue;
            }

            var key = (toolName, errorCode);
            counts.TryGetValue(key, out var count);
            counts[key] = count + 1;
        }

        if (counts.Count == 0) {
            return Array.Empty<ToolErrorMetricDto>();
        }

        return counts
            .Select(pair => new ToolErrorMetricDto {
                ToolName = pair.Key.ToolName,
                ErrorCode = pair.Key.ErrorCode,
                Count = pair.Value
            })
            .OrderByDescending(metric => metric.Count)
            .ThenBy(metric => metric.ToolName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(metric => metric.ErrorCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeToolErrorCode(ToolOutputDto output) {
        var errorCode = (output.ErrorCode ?? string.Empty).Trim();
        if (errorCode.Length > 0) {
            return errorCode;
        }

        if (output.Ok is false || !string.IsNullOrWhiteSpace(output.Error)) {
            return "tool_error";
        }

        return string.Empty;
    }

    private static string AppendTurnCompletionNotice(string text, TurnInfo turn) {
        var status = (turn.Status ?? string.Empty).Trim();
        if (status.Length == 0 || string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)) {
            return text;
        }

        var reason = ResolveTurnCompletionReason(turn);
        var details = reason.Length == 0
            ? $"status '{status}'"
            : $"status '{status}' (reason: {reason})";
        var notice = $"Partial response: model returned {details}. Share your next step to resume.";

        var body = (text ?? string.Empty).TrimEnd();
        if (body.Length == 0) {
            return notice;
        }

        if (body.IndexOf("Partial response:", StringComparison.OrdinalIgnoreCase) >= 0) {
            return body;
        }

        return body + Environment.NewLine + Environment.NewLine + notice;
    }

    private static string ResolveTurnCompletionReason(TurnInfo turn) {
        try {
            var response = turn.Raw.GetObject("response");
            if (response is null) {
                return string.Empty;
            }

            var incompleteDetails = response.GetObject("incomplete_details");
            var reason = (incompleteDetails?.GetString("reason") ?? string.Empty).Trim();
            if (reason.Length > 0) {
                return reason;
            }

            return (response.GetString("status_details") ?? string.Empty).Trim();
        } catch {
            return string.Empty;
        }
    }

    private static IReadOnlyList<ToolDefinition> SanitizeToolDefinitions(IReadOnlyList<ToolDefinition> definitions) {
        if (definitions.Count == 0) {
            return Array.Empty<ToolDefinition>();
        }

        var sanitized = new List<ToolDefinition>(definitions.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null) {
                continue;
            }

            var normalizedName = (definition.Name ?? string.Empty).Trim();
            if (normalizedName.Length == 0 || !seen.Add(normalizedName)) {
                continue;
            }

            sanitized.Add(definition);
        }

        return sanitized.Count == 0 ? Array.Empty<ToolDefinition>() : sanitized;
    }

    private async Task<(IReadOnlyList<ToolDefinition> Definitions, List<ToolRoutingInsight> Insights)> SelectWeightedToolSubsetAsync(
        IntelligenceXClient client,
        string threadId,
        IReadOnlyList<ToolDefinition> definitions,
        string requestText,
        int? maxCandidateTools,
        CancellationToken cancellationToken) {
        if (definitions.Count <= 12) {
            return (definitions, new List<ToolRoutingInsight>());
        }

        var userRequest = ExtractPrimaryUserRequest(requestText);
        if (ShouldSkipWeightedRouting(userRequest)) {
            return (definitions, new List<ToolRoutingInsight>());
        }

        var limit = ResolveMaxCandidateToolsLimit(maxCandidateTools, definitions.Count);
        if (limit >= definitions.Count) {
            return (definitions, new List<ToolRoutingInsight>());
        }

        var planned = await TrySelectToolsViaModelPlannerAsync(client, threadId, userRequest, definitions, limit, cancellationToken).ConfigureAwait(false);
        if (planned.Count > 0) {
            var selected = EnsureMinimumToolSelection(userRequest, definitions, planned, limit);
            if (selected.Count > 0 && selected.Count < definitions.Count) {
                var plannerInsights = BuildModelRoutingInsights(selected, plannedCount: planned.Count);
                return (selected, plannerInsights);
            }
        }

        var fallback = SelectWeightedToolSubset(definitions, userRequest, maxCandidateTools, out var fallbackInsights);
        return (fallback, fallbackInsights);
    }

    private IReadOnlyList<ToolDefinition> SelectWeightedToolSubset(IReadOnlyList<ToolDefinition> definitions, string requestText, int? maxCandidateTools,
        out List<ToolRoutingInsight> insights) {
        insights = new List<ToolRoutingInsight>();
        if (definitions.Count <= 12) {
            return definitions;
        }

        var userRequest = ExtractPrimaryUserRequest(requestText);
        if (ShouldSkipWeightedRouting(userRequest)) {
            return definitions;
        }

        var limit = ResolveMaxCandidateToolsLimit(maxCandidateTools, definitions.Count);
        if (limit >= definitions.Count) {
            return definitions;
        }

        var routingTokens = TokenizeRoutingTokens(userRequest, maxTokens: 16);
        var routingTokenSupport = routingTokens.Length == 0 ? Array.Empty<int>() : new int[routingTokens.Length];
        string[]? toolSearchTexts = null;
        if (routingTokens.Length > 0) {
            toolSearchTexts = new string[definitions.Count];
            for (var i = 0; i < definitions.Count; i++) {
                toolSearchTexts[i] = BuildToolRoutingSearchText(definitions[i]);
            }

            for (var t = 0; t < routingTokens.Length; t++) {
                var token = routingTokens[t];
                if (token.Length == 0) {
                    continue;
                }

                var support = 0;
                for (var i = 0; i < toolSearchTexts.Length; i++) {
                    if (toolSearchTexts[i].IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                        support++;
                    }
                }

                routingTokenSupport[t] = support;
            }
        }

        // Tokens that show up in most tools are noise (ex: "get", "list"). Filter them out per-turn.
        var maxTokenSupport = Math.Max(1, (int)Math.Ceiling(definitions.Count * 0.55d));

        var scored = new List<ToolScore>(definitions.Count);
        var hasSignal = false;
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            var score = 0d;
            var tokenHits = 0;
            var directNameMatch = userRequest.IndexOf(definition.Name, StringComparison.OrdinalIgnoreCase) >= 0;
            if (directNameMatch) {
                score += 6d;
            }

            if (routingTokens.Length > 0) {
                var searchText = toolSearchTexts?[i] ?? BuildToolRoutingSearchText(definition);
                for (var t = 0; t < routingTokens.Length; t++) {
                    if (routingTokenSupport[t] > maxTokenSupport) {
                        continue;
                    }

                    var token = routingTokens[t];
                    if (token.Length == 0) {
                        continue;
                    }

                    if (searchText.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                        tokenHits++;
                    }
                }

                if (tokenHits > 0) {
                    score += tokenHits * 1.25d;
                }
            }

            var adjustment = ReadToolRoutingAdjustment(definition.Name);
            score += adjustment;
            if (score > 0.01d) {
                hasSignal = true;
            }

            scored.Add(new ToolScore(
                Definition: definition,
                Score: score,
                DirectNameMatch: directNameMatch,
                TokenHits: tokenHits,
                Adjustment: adjustment));
        }

        if (!hasSignal) {
            return definitions;
        }

        scored.Sort(static (a, b) => {
            var scoreCompare = b.Score.CompareTo(a.Score);
            if (scoreCompare != 0) {
                return scoreCompare;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(a.Definition.Name, b.Definition.Name);
        });

        if (scored[0].Score < 1d) {
            return definitions;
        }

        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedDefs = new List<ToolDefinition>(Math.Min(limit, definitions.Count));
        for (var i = 0; i < scored.Count && selectedDefs.Count < limit; i++) {
            var definition = scored[i].Definition;
            if (!selected.Add(definition.Name)) {
                continue;
            }
            selectedDefs.Add(definition);
        }

        if (selectedDefs.Count == 0) {
            return definitions;
        }

        var minSelection = Math.Min(definitions.Count, Math.Max(8, Math.Min(limit, 12)));
        if (selectedDefs.Count < minSelection) {
            for (var i = selectedDefs.Count; i < scored.Count && selectedDefs.Count < minSelection; i++) {
                var definition = scored[i].Definition;
                if (!selected.Add(definition.Name)) {
                    continue;
                }
                selectedDefs.Add(definition);
            }
        }

        if (selectedDefs.Count >= definitions.Count) {
            return definitions;
        }

        insights = BuildRoutingInsights(scored, selectedDefs);
        return selectedDefs;
    }

    private static List<ToolRoutingInsight> BuildRoutingInsights(IReadOnlyList<ToolScore> scored, IReadOnlyList<ToolDefinition> selectedDefs) {
        if (selectedDefs.Count == 0 || scored.Count == 0) {
            return new List<ToolRoutingInsight>();
        }

        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < selectedDefs.Count; i++) {
            selectedNames.Add(selectedDefs[i].Name);
        }

        var maxScore = scored[0].Score <= 0 ? 1d : scored[0].Score;
        var insights = new List<ToolRoutingInsight>();
        for (var i = 0; i < scored.Count; i++) {
            var toolScore = scored[i];
            if (!selectedNames.Contains(toolScore.Definition.Name)) {
                continue;
            }

            var confidenceValue = Math.Clamp(toolScore.Score / maxScore, 0d, 1d);
            var confidence = confidenceValue >= 0.72d ? "high" : confidenceValue >= 0.45d ? "medium" : "low";
            var reasons = new List<string>();
            if (toolScore.DirectNameMatch) {
                reasons.Add("direct name match");
            }
            if (toolScore.TokenHits > 0) {
                reasons.Add("token match");
            }
            if (toolScore.Adjustment > 0.2d) {
                reasons.Add("recent tool success");
            } else if (toolScore.Adjustment < -0.2d) {
                reasons.Add("recent tool failures");
            }

            if (reasons.Count == 0) {
                reasons.Add("general relevance");
            }

            insights.Add(new ToolRoutingInsight(
                ToolName: toolScore.Definition.Name,
                Confidence: confidence,
                Score: Math.Round(toolScore.Score, 3),
                Reason: string.Join(", ", reasons)));
        }

        insights.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        if (insights.Count > 12) {
            insights.RemoveRange(12, insights.Count - 12);
        }

        return insights;
    }

    private static string[] TokenizeRoutingTokens(string text, int maxTokens) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0 || maxTokens <= 0) {
            return Array.Empty<string>();
        }

        var tokens = new List<string>(Math.Min(12, maxTokens));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inToken = false;
        var tokenStart = 0;
        for (var i = 0; i <= normalized.Length; i++) {
            var ch = i < normalized.Length ? normalized[i] : '\0';
            var isTokenChar = i < normalized.Length && char.IsLetterOrDigit(ch);
            if (isTokenChar) {
                if (!inToken) {
                    inToken = true;
                    tokenStart = i;
                }
                continue;
            }

            if (!inToken) {
                continue;
            }

            var token = normalized.Substring(tokenStart, i - tokenStart).Normalize(NormalizationForm.FormKC).Trim();
            inToken = false;
            if (token.Length == 0) {
                continue;
            }

            var lower = token.ToLowerInvariant();
            var hasNonAscii = false;
            for (var t = 0; t < lower.Length; t++) {
                if (lower[t] > 127) {
                    hasNonAscii = true;
                    break;
                }
            }

            var minLen = hasNonAscii ? 2 : 3;
            if (lower.Length < minLen) {
                continue;
            }

            if (seen.Add(lower)) {
                tokens.Add(lower);
                if (tokens.Count >= maxTokens) {
                    break;
                }
            }
        }

        return tokens.Count == 0 ? Array.Empty<string>() : tokens.ToArray();
    }

    private static string BuildToolRoutingSearchText(ToolDefinition definition) {
        if (definition is null) {
            return string.Empty;
        }

        var sb = new StringBuilder(256);
        sb.Append(definition.Name);
        if (!string.IsNullOrWhiteSpace(definition.Description)) {
            sb.Append(' ').Append(definition.Description!.Trim());
        }

        if (definition.Tags.Count > 0) {
            for (var i = 0; i < definition.Tags.Count; i++) {
                var tag = (definition.Tags[i] ?? string.Empty).Trim();
                if (tag.Length == 0) {
                    continue;
                }
                sb.Append(' ').Append(tag);
            }
        }

        if (definition.Aliases.Count > 0) {
            for (var i = 0; i < definition.Aliases.Count; i++) {
                var alias = definition.Aliases[i];
                if (alias is null || string.IsNullOrWhiteSpace(alias.Name)) {
                    continue;
                }
                sb.Append(' ').Append(alias.Name.Trim());
            }
        }

        var schemaArguments = ExtractToolSchemaPropertyNames(definition, maxCount: 12, out var hasTableViewProjection);
        for (var i = 0; i < schemaArguments.Length; i++) {
            sb.Append(' ').Append(schemaArguments[i]);
        }

        var requiredArguments = ExtractToolSchemaRequiredNames(definition, maxCount: 8);
        if (requiredArguments.Length > 0) {
            sb.Append(" required");
            for (var i = 0; i < requiredArguments.Length; i++) {
                sb.Append(' ').Append(requiredArguments[i]);
            }
        }

        if (hasTableViewProjection) {
            sb.Append(" table view projection columns sort_by sort_direction top");
        }

        return sb.ToString();
    }

    private static string[] ExtractToolSchemaPropertyNames(ToolDefinition definition, int maxCount, out bool hasTableViewProjection) {
        hasTableViewProjection = false;
        if (definition?.Parameters is null || maxCount <= 0) {
            return Array.Empty<string>();
        }

        var properties = definition.Parameters.GetObject("properties");
        if (properties is null || properties.Count == 0) {
            return Array.Empty<string>();
        }

        hasTableViewProjection = HasTableViewProjectionArguments(properties);

        var names = new List<string>(Math.Min(maxCount, properties.Count));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in properties) {
            var name = NormalizeToolSchemaToken(kv.Key);
            if (name.Length == 0 || !seen.Add(name)) {
                continue;
            }

            names.Add(name);
            if (names.Count >= maxCount) {
                break;
            }
        }

        return names.Count == 0 ? Array.Empty<string>() : names.ToArray();
    }

    private static string[] ExtractToolSchemaRequiredNames(ToolDefinition definition, int maxCount) {
        if (definition?.Parameters is null || maxCount <= 0) {
            return Array.Empty<string>();
        }

        var required = definition.Parameters.GetArray("required");
        if (required is null || required.Count == 0) {
            return Array.Empty<string>();
        }

        var names = new List<string>(Math.Min(maxCount, required.Count));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < required.Count && names.Count < maxCount; i++) {
            var value = NormalizeToolSchemaToken(required[i]?.AsString());
            if (value.Length == 0 || !seen.Add(value)) {
                continue;
            }

            names.Add(value);
        }

        return names.Count == 0 ? Array.Empty<string>() : names.ToArray();
    }

    private static bool HasTableViewProjectionArguments(JsonObject properties) {
        return properties.TryGetValue("columns", out _)
               || properties.TryGetValue("sort_by", out _)
               || properties.TryGetValue("sort_direction", out _)
               || properties.TryGetValue("top", out _);
    }

    private static string NormalizeToolSchemaToken(string? token) {
        var value = (token ?? string.Empty).Trim();
        if (value.Length == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++) {
            var c = value[i];
            if (char.IsLetterOrDigit(c) || c is '_' or '-') {
                sb.Append(c);
            } else if (char.IsWhiteSpace(c)) {
                sb.Append('_');
            }
        }

        return sb.ToString().Trim('_');
    }

}
