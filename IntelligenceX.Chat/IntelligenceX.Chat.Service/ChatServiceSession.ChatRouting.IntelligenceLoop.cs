using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;

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

    // Internal seam for deterministic chat-loop tests and shared routing behavior.
    internal static int ResolveMaxReviewPasses(ChatRequestOptions? options) {
        var configured = options?.MaxReviewPasses;
        if (!configured.HasValue || configured.Value <= 0) {
            return DefaultMaxReviewPasses;
        }

        return Math.Clamp(configured.Value, DefaultMaxReviewPasses, MaxReviewPassesLimit);
    }

    internal static int ResolveModelHeartbeatSeconds(ChatRequestOptions? options) {
        var configured = options?.ModelHeartbeatSeconds;
        if (!configured.HasValue) {
            return DefaultModelHeartbeatSeconds;
        }

        return Math.Clamp(configured.Value, 0, MaxModelHeartbeatSeconds);
    }

    internal static bool TryReadProactiveModeFromRequestText(string? requestText, out bool enabled) {
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

    internal static bool ShouldAttemptResponseQualityReview(
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

        return ContainsQuestionSignal(draft) && tokenCount <= 48 && draft.Length <= 360;
    }

    internal static string BuildResponseQualityReviewPrompt(string userRequest, string assistantDraft, bool hasToolActivity, int reviewPassNumber,
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

    internal static bool ShouldAttemptProactiveFollowUpReview(
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

    internal static string BuildProactiveFollowUpReviewPrompt(string userRequest, string assistantDraft) {
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
            - For each bullet, include signal -> why it matters -> exact next validation/fix action.
            - If confidence is uncertain, say what evidence is missing and how to collect it.
            - Prefer proactive checks that can catch hidden regressions, not just obvious follow-ups.
            - Do not invent tool outputs or claim completed actions that were not executed.
            Return only the revised assistant response text.
            """;
    }

    internal async Task RunPhaseProgressLoopAsync(
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
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var timer = new PeriodicTimer(heartbeatInterval);
        var stopHeartbeatTask = phaseTask.ContinueWith(
            static (_, state) => {
                try {
                    ((CancellationTokenSource)state!).Cancel();
                } catch {
                    // Best-effort heartbeat stop.
                }
            },
            heartbeatCts,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        try {
            while (await timer.WaitForNextTickAsync(heartbeatCts.Token).ConfigureAwait(false)) {
                if (phaseTask.IsCompleted) {
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
        } catch (OperationCanceledException) when (heartbeatCts.IsCancellationRequested) {
            // Expected when the model phase completes or the turn is canceled.
        } finally {
            await stopHeartbeatTask.ConfigureAwait(false);
        }

        await phaseTask.ConfigureAwait(false);
    }

    internal async Task<TurnInfo> RunModelPhaseWithProgressAsync(
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

    internal Task<TurnInfo> RunReviewOnlyModelPhaseWithProgressAsync(
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
        // Review-only passes are in-thread rewrites of the current draft and must never execute tools.
        return RunModelPhaseWithProgressAsync(
            client,
            writer,
            requestId,
            threadId,
            input,
            CopyChatOptionsWithoutTools(options, newThreadOverride: false),
            cancellationToken,
            phaseStatus,
            phaseMessage,
            heartbeatLabel,
            heartbeatSeconds);
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

    internal static ChatOptions CopyChatOptionsWithoutTools(ChatOptions options, bool? newThreadOverride = null) {
        var copy = CopyChatOptions(options, newThreadOverride);
        copy.Tools = null;
        copy.ToolChoice = null;
        copy.ParallelToolCalls = false;
        return copy;
    }
}
