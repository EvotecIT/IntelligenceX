using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
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
    private const string AnswerPlanMarker = "ix:answer-plan:v1";
    private const string ProactiveModeMarker = "ix:proactive-mode:v1";
    private const string ProactiveFollowUpMarker = "ix:proactive-followup:v1";
    // Keep parsing bounded while supporting larger structured request envelopes.
    private const int MaxProactiveModeScanChars = 4096;
    private sealed record ChatTurnRunResult(
        ChatResultMessage Result,
        TurnUsage? Usage,
        int ToolCallsCount,
        int ToolRounds,
        int ProjectionFallbackCount,
        IReadOnlyList<ToolErrorMetricDto> ToolErrors,
        IReadOnlyList<TurnCounterMetricDto> AutonomyCounters,
        string? ResolvedModel,
        long? WeightedSubsetSelectionMs,
        long? ResolveModelMs);
    internal sealed record ProactiveFollowUpReviewDecision(
        bool ShouldAttempt,
        string Reason,
        int PendingReadOnlyCount,
        int PendingUnknownCount,
        int PendingMutatingCount);

    private static (bool ParallelTools, bool AllowMutatingParallel, string Mode) ResolveParallelToolExecutionMode(ChatRequestOptions? options,
        bool serviceDefaultParallelTools,
        bool serviceDefaultAllowMutatingParallel) {
        var requestedParallelTools = options?.ParallelTools ?? serviceDefaultParallelTools;
        var mode = NormalizeParallelToolMode(options?.ParallelToolMode);
        return mode switch {
            ParallelToolModeForceSerial => (false, false, ParallelToolModeForceSerial),
            ParallelToolModeAllowParallel => (true, true, ParallelToolModeAllowParallel),
            _ => (requestedParallelTools, serviceDefaultAllowMutatingParallel, ParallelToolModeAuto)
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
    internal static int ResolveMaxToolRounds(ChatRequestOptions? options, int serviceDefaultMaxToolRounds) {
        var serviceDefault = Math.Max(ChatRequestOptionLimits.MinToolRounds, serviceDefaultMaxToolRounds);
        var requested = options?.MaxToolRounds ?? serviceDefault;
        return Math.Clamp(requested, ChatRequestOptionLimits.MinToolRounds, ChatRequestOptionLimits.MaxToolRounds);
    }

    // Internal seam for deterministic chat-loop tests and shared routing behavior.
    internal static int ResolveMaxReviewPasses(ChatRequestOptions? options) {
        var configured = options?.MaxReviewPasses;
        if (!configured.HasValue) {
            return ChatRequestOptionLimits.DefaultReviewPasses;
        }

        if (configured.Value <= 0) {
            return 0;
        }

        return Math.Clamp(configured.Value, 0, ChatRequestOptionLimits.MaxReviewPasses);
    }

    internal static bool IsComplexReviewCandidateRequest(string userRequest) {
        var request = (userRequest ?? string.Empty).Trim();
        if (request.Length == 0) {
            return false;
        }

        if (LooksLikeActionSelectionPayload(request)) {
            return true;
        }

        if (request.IndexOf('\n', StringComparison.Ordinal) >= 0) {
            return true;
        }

        return request.Length >= 72;
    }

    // Smart-mode guard: when we expect an analysis-style reviewed response, avoid exposing a draft
    // that will be rewritten before completion.
    internal static bool ShouldBufferDraftDeltasForSmartReview(ChatRequest request) {
        if (request is null) {
            return false;
        }

        var options = request.Options;
        if (!(options?.PlanExecuteReviewLoop ?? false)) {
            return false;
        }

        if (ResolveMaxReviewPasses(options) <= 0) {
            return false;
        }

        var userRequest = ExtractPrimaryUserRequest(request.Text);
        return IsComplexReviewCandidateRequest(userRequest);
    }

    internal static int ResolveModelHeartbeatSeconds(ChatRequestOptions? options) {
        var configured = options?.ModelHeartbeatSeconds;
        if (!configured.HasValue) {
            return ChatRequestOptionLimits.DefaultModelHeartbeatSeconds;
        }

        return Math.Clamp(configured.Value, 0, ChatRequestOptionLimits.MaxModelHeartbeatSeconds);
    }

    private static string BuildReviewPassClampMessage(int requestedReviewPasses, int effectiveReviewPasses) {
        return
            $"Requested review passes ({requestedReviewPasses}) adjusted to {effectiveReviewPasses} (supported range: 0..{ChatRequestOptionLimits.MaxReviewPasses}).";
    }

    private static string BuildModelHeartbeatClampMessage(int requestedHeartbeatSeconds, int effectiveHeartbeatSeconds) {
        return
            $"Requested model heartbeat seconds ({requestedHeartbeatSeconds}) adjusted to {effectiveHeartbeatSeconds} (supported range: 0..{ChatRequestOptionLimits.MaxModelHeartbeatSeconds}).";
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

        var scanLength = Math.Min(MaxProactiveModeScanChars, text.Length - markerIndex);
        if (scanLength <= 0) {
            return false;
        }

        var scan = text.AsSpan(markerIndex, scanLength);
        return TryReadStructuredProactiveModeEnabledValue(scan, out enabled);
    }

    private static bool TryReadStructuredProactiveModeEnabledValue(ReadOnlySpan<char> text, out bool enabled) {
        enabled = false;
        var markerLineSeen = false;
        while (!text.IsEmpty) {
            var lineBreakIndex = text.IndexOfAny('\r', '\n');
            ReadOnlySpan<char> line;
            if (lineBreakIndex < 0) {
                line = text;
                text = ReadOnlySpan<char>.Empty;
            } else {
                line = text.Slice(0, lineBreakIndex);
                var nextIndex = lineBreakIndex + 1;
                if (nextIndex < text.Length && text[lineBreakIndex] == '\r' && text[nextIndex] == '\n') {
                    nextIndex++;
                }

                text = text.Slice(nextIndex);
            }

            if (!markerLineSeen) {
                if (line.IndexOf(ProactiveModeMarker, StringComparison.OrdinalIgnoreCase) >= 0) {
                    markerLineSeen = true;
                }
                continue;
            }

            if (LooksLikeStructuredSectionHeader(line)) {
                // Stay within the proactive-mode block and avoid unrelated `enabled:` lines
                // in following structured sections.
                return false;
            }

            if (TryParseStructuredProactiveModeEnabledLine(line, out enabled)) {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeStructuredSectionHeader(ReadOnlySpan<char> line) {
        var trimmed = line.Trim();
        if (trimmed.Length < 3 || trimmed[0] != '[' || trimmed[^1] != ']') {
            return false;
        }

        for (var i = 1; i < trimmed.Length - 1; i++) {
            if (!char.IsWhiteSpace(trimmed[i])) {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseStructuredProactiveModeEnabledLine(ReadOnlySpan<char> line, out bool enabled) {
        enabled = false;
        var trimmed = line.Trim();
        if (trimmed.IsEmpty || !trimmed.StartsWith("enabled", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var afterKey = trimmed.Slice("enabled".Length).TrimStart();
        if (afterKey.IsEmpty || (afterKey[0] != ':' && afterKey[0] != '\uFF1A')) {
            return false;
        }

        var value = afterKey.Slice(1).Trim();
        if (value.Length >= 2) {
            var startsWithDoubleQuote = value[0] == '"';
            var startsWithSingleQuote = value[0] == '\'';
            if ((startsWithDoubleQuote && value[^1] == '"') || (startsWithSingleQuote && value[^1] == '\'')) {
                value = value.Slice(1, value.Length - 2).Trim();
            }
        }

        if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) {
            enabled = true;
            return true;
        }

        if (value.Equals("false", StringComparison.OrdinalIgnoreCase)) {
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

        if (!IsComplexReviewCandidateRequest(request)) {
            return false;
        }

        var tokenCount = CountLetterDigitTokens(draft, maxTokens: 96);
        if (tokenCount <= 0) {
            return false;
        }

        if (tokenCount < 24 || draft.Length < 260) {
            return false;
        }

        if (draft.Length <= 1800) {
            return true;
        }

        return ContainsQuestionSignal(draft) && draft.Length <= 2400;
    }

    internal static string BuildResponseQualityReviewPrompt(
        string userRequest,
        string assistantDraft,
        bool hasToolActivity,
        int reviewPassNumber,
        int maxReviewPasses,
        IReadOnlyList<string>? rememberedExecutionBackends = null) {
        var requestText = TrimForPrompt(userRequest, 520);
        var draftText = TrimForPrompt(ResolveReviewedAssistantDraft(assistantDraft).VisibleText, 1600);
        var toolActivityHint = hasToolActivity ? "present" : "none";
        var rememberedBackendHints = rememberedExecutionBackends is { Count: > 0 }
            ? rememberedExecutionBackends
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();
        var rememberedExecutionBackendsBlock = rememberedBackendHints.Length == 0
            ? string.Empty
            : "Remembered successful execution backends:\n"
              + string.Join(", ", rememberedBackendHints)
              + ".\n\n";
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

            {{rememberedExecutionBackendsBlock}}{{BuildAnswerPlanInstructions()}}

            Rewrite the assistant response so it is helpful, direct, and action-oriented.
            Do not invent tool outputs.
            If no tools ran in this turn, do not imply fresh execution or fresh results.
            When explaining a rule or capability constraint, summarize it abstractly instead of echoing example phrases from the user request verbatim.
            If a blocker exists, state the exact blocker and the minimal missing input.
            After the answer-plan block, return only the revised assistant response text.
            """;
    }

    internal static bool ShouldAttemptProactiveFollowUpReview(
        bool proactiveModeEnabled,
        bool hasToolActivity,
        bool proactiveFollowUpUsed,
        bool continuationFollowUpTurn,
        bool compactFollowUpTurn,
        string userRequest,
        string assistantDraft) {
        return ResolveProactiveFollowUpReviewDecision(
            proactiveModeEnabled,
            hasToolActivity,
            proactiveFollowUpUsed,
            continuationFollowUpTurn,
            compactFollowUpTurn,
            userRequest,
            assistantDraft).ShouldAttempt;
    }

    internal static ProactiveFollowUpReviewDecision ResolveProactiveFollowUpReviewDecision(
        bool proactiveModeEnabled,
        bool hasToolActivity,
        bool proactiveFollowUpUsed,
        bool continuationFollowUpTurn,
        bool compactFollowUpTurn,
        string userRequest,
        string assistantDraft,
        TurnAnswerPlan? answerPlanOverride = null) {
        if (!proactiveModeEnabled || !hasToolActivity || proactiveFollowUpUsed) {
            return new ProactiveFollowUpReviewDecision(
                ShouldAttempt: false,
                Reason: !proactiveModeEnabled
                    ? "skip_proactive_mode_disabled"
                    : !hasToolActivity
                        ? "skip_no_tool_activity"
                        : "skip_proactive_follow_up_already_used",
                PendingReadOnlyCount: 0,
                PendingUnknownCount: 0,
                PendingMutatingCount: 0);
        }

        var reviewedDraft = ResolveReviewedAssistantDraft(assistantDraft);
        var draft = reviewedDraft.VisibleText.Trim();
        var answerPlan = answerPlanOverride ?? reviewedDraft.AnswerPlan;
        if (draft.Length == 0 || draft.Length > 2800) {
            return new ProactiveFollowUpReviewDecision(
                ShouldAttempt: false,
                Reason: draft.Length == 0 ? "skip_empty_assistant_draft" : "skip_assistant_draft_too_long",
                PendingReadOnlyCount: 0,
                PendingUnknownCount: 0,
                PendingMutatingCount: 0);
        }

        if (draft.Contains(ProactiveFollowUpMarker, StringComparison.OrdinalIgnoreCase)
            || draft.Contains(ResponseReviewMarker, StringComparison.OrdinalIgnoreCase)
            || draft.Contains(ExecutionContractMarker, StringComparison.OrdinalIgnoreCase)) {
            return new ProactiveFollowUpReviewDecision(
                ShouldAttempt: false,
                Reason: "skip_proactive_or_review_marker_present",
                PendingReadOnlyCount: 0,
                PendingUnknownCount: 0,
                PendingMutatingCount: 0);
        }

        if (answerPlan.HasPlan
            && answerPlan.RequestedArtifactAlreadyVisibleAbove
            && string.IsNullOrWhiteSpace(answerPlan.RequestedArtifactVisibilityReason)) {
            return new ProactiveFollowUpReviewDecision(
                ShouldAttempt: true,
                Reason: "allow_unjustified_artifact_omission",
                PendingReadOnlyCount: 0,
                PendingUnknownCount: 0,
                PendingMutatingCount: 0);
        }

        var requestedArtifactIntent = ResolveRequestedArtifactIntent(userRequest);
        if (requestedArtifactIntent.RequiresArtifact
            && !IsRequestedArtifactRequirementSatisfied(requestedArtifactIntent, draft, answerPlan)) {
            return new ProactiveFollowUpReviewDecision(
                ShouldAttempt: true,
                Reason: "allow_requested_artifact_missing",
                PendingReadOnlyCount: 0,
                PendingUnknownCount: 0,
                PendingMutatingCount: 0);
        }

        if (answerPlan.HasPlan && !answerPlan.AdvancesCurrentAsk) {
            return new ProactiveFollowUpReviewDecision(
                ShouldAttempt: true,
                Reason: "allow_answer_plan_not_advancing_current_ask",
                PendingReadOnlyCount: 0,
                PendingUnknownCount: 0,
                PendingMutatingCount: 0);
        }

        if (answerPlan.HasPlan
            && answerPlan.RepeatsPriorVisibleContent
            && string.IsNullOrWhiteSpace(answerPlan.PriorVisibleDeltaReason)) {
            return new ProactiveFollowUpReviewDecision(
                ShouldAttempt: true,
                Reason: "allow_unjustified_prior_visible_repeat",
                PendingReadOnlyCount: 0,
                PendingUnknownCount: 0,
                PendingMutatingCount: 0);
        }

        if (answerPlan.HasPlan
            && answerPlan.ReusePriorVisuals
            && string.IsNullOrWhiteSpace(answerPlan.ReuseReason)) {
            return new ProactiveFollowUpReviewDecision(
                ShouldAttempt: true,
                Reason: "allow_unjustified_visual_reuse",
                PendingReadOnlyCount: 0,
                PendingUnknownCount: 0,
                PendingMutatingCount: 0);
        }

        if (answerPlan.HasPlan
            && answerPlan.ReusePriorVisuals
            && !answerPlan.RepeatAddsNewInformation) {
            return new ProactiveFollowUpReviewDecision(
                ShouldAttempt: true,
                Reason: "allow_redundant_visual_reuse",
                PendingReadOnlyCount: 0,
                PendingUnknownCount: 0,
                PendingMutatingCount: 0);
        }

        if (answerPlan.HasPlan
            && answerPlan.ReusePriorVisuals
            && answerPlan.RepeatAddsNewInformation
            && string.IsNullOrWhiteSpace(answerPlan.RepeatNoveltyReason)) {
            return new ProactiveFollowUpReviewDecision(
                ShouldAttempt: true,
                Reason: "allow_unjustified_visual_novelty_claim",
                PendingReadOnlyCount: 0,
                PendingUnknownCount: 0,
                PendingMutatingCount: 0);
        }

        // Follow-up turns should stay concise and conversational. Extra proactive rewrite passes
        // on continuation nudges can cause repetitive draft churn and accidental scope drift.
        if (continuationFollowUpTurn || compactFollowUpTurn) {
            return new ProactiveFollowUpReviewDecision(
                ShouldAttempt: false,
                Reason: "skip_follow_up_turn",
                PendingReadOnlyCount: 0,
                PendingUnknownCount: 0,
                PendingMutatingCount: 0);
        }

        if (LooksLikeMultilineFollowUpBlockerDraft(draft)
            || LooksLikeExecutionAcknowledgeDraft(draft)
            || LooksLikeStructuredScopeChoiceDraft(draft)) {
            // Blocker/choice drafts should terminate cleanly instead of expanding
            // into an additional proactive rewrite pass.
            return new ProactiveFollowUpReviewDecision(
                ShouldAttempt: false,
                Reason: "skip_blocker_or_structured_scope_choice",
                PendingReadOnlyCount: 0,
                PendingUnknownCount: 0,
                PendingMutatingCount: 0);
        }

        if (ContainsQuestionSignal(draft)) {
            return new ProactiveFollowUpReviewDecision(
                ShouldAttempt: false,
                Reason: "skip_question_signal_present",
                PendingReadOnlyCount: 0,
                PendingUnknownCount: 0,
                PendingMutatingCount: 0);
        }

        var pendingActions = ExtractPendingActions(draft);
        if (pendingActions.Count == 0) {
            return new ProactiveFollowUpReviewDecision(
                ShouldAttempt: true,
                Reason: "allow_no_pending_actions",
                PendingReadOnlyCount: 0,
                PendingUnknownCount: 0,
                PendingMutatingCount: 0);
        }

        var pendingReadOnlyCount = 0;
        var pendingUnknownCount = 0;
        var pendingMutatingCount = 0;
        for (var i = 0; i < pendingActions.Count; i++) {
            switch (pendingActions[i].Mutability) {
                case ActionMutability.ReadOnly:
                    pendingReadOnlyCount++;
                    break;
                case ActionMutability.Mutating:
                    pendingMutatingCount++;
                    break;
                default:
                    pendingUnknownCount++;
                    break;
            }
        }

        if (pendingMutatingCount > 0) {
            return new ProactiveFollowUpReviewDecision(
                ShouldAttempt: false,
                Reason: "skip_pending_mutating_actions",
                PendingReadOnlyCount: pendingReadOnlyCount,
                PendingUnknownCount: pendingUnknownCount,
                PendingMutatingCount: pendingMutatingCount);
        }

        return new ProactiveFollowUpReviewDecision(
            ShouldAttempt: true,
            Reason: "allow_pending_non_mutating_actions",
            PendingReadOnlyCount: pendingReadOnlyCount,
            PendingUnknownCount: pendingUnknownCount,
            PendingMutatingCount: pendingMutatingCount);
    }

}
