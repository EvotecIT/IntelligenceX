using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const string ActionMarker = "ix:action:v1";
    private const int MaxActionParsingChars = 64 * 1024;
    private const int MaxPendingActionAssistantContextChars = 4096;
    private const int MaxFallbackChoiceActionTitleChars = 96;
    private static readonly Regex LooseActionBlockRegex = new(
        @"(?is)(?:^|\n)\s*id\s*:?\s*(?<id>[^\r\n]+)\s*\r?\n\s*title\s*:?\s*(?<title>[^\r\n]*)\s*\r?\n\s*request\s*:?\s*(?<request>.*?)\r?\n\s*reply\s*:?\s*(?<reply>[^\r\n]+)",
        RegexOptions.CultureInvariant);
    private static readonly char[] PendingActionConfirmationQuestionPunctuation = new[] { '?', '？', '¿', '؟' };
    private static readonly char[] PendingActionConfirmationDisqualifierPunctuation = new[] { ':', ';', '\uFF1A', '\uFF1B' }; // ： ；
    private static readonly char[] PendingActionConfirmationStructuredDisqualifierChars =
        new[] { '\\', '{', '}', '[', ']', '<', '>', '=' };
    private readonly record struct FallbackChoiceCandidate(string Title, bool IsNumbered, string ActionId);
    private readonly record struct PendingAction(string Id, string Title, string Request, ActionMutability Mutability);

    private static bool LooksLikeStructuredPendingActionConfirmationInput(string userText) {
        // Confirmation is safety-sensitive. If the user message looks like a command/payload rather than
        // an intentional "echo this phrase" response, do not treat it as confirmation.
        //
        // This is language-agnostic (structural/syntactic), and complements the exact-equality token matching.
        var trimmed = (userText ?? string.Empty).Trim();
        if (trimmed.Length == 0) {
            return false;
        }

        if (trimmed.StartsWith("/", StringComparison.Ordinal) || trimmed.StartsWith("-", StringComparison.Ordinal)) {
            return true;
        }

        if (trimmed.Contains("://", StringComparison.Ordinal)) {
            return true;
        }

        if (trimmed.IndexOfAny(PendingActionConfirmationStructuredDisqualifierChars) >= 0) {
            return true;
        }

        if (trimmed.Contains('\n', StringComparison.Ordinal) || trimmed.Contains('\r', StringComparison.Ordinal)) {
            return true;
        }

        return false;
    }

    private void RememberPendingActions(string threadId, string assistantText) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var text = assistantText ?? string.Empty;
        var markerIdx = text.IndexOf(ActionMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIdx >= 0 && text.Length > MaxActionParsingChars) {
            // Keep a window around the first marker to cap worst-case parsing work.
            var start = Math.Max(0, markerIdx - 256);
            var len = Math.Min(MaxActionParsingChars, text.Length - start);
            text = text.Substring(start, len);
        }

        var actions = ExtractPendingActions(text);
        var fromFallbackChoices = false;
        if (actions.Count == 0 && markerIdx < 0) {
            // Fallback: allow compact assistant choice lists (for example bullet options) to be
            // selected naturally on the next user turn, even when the model omitted ix:action blocks.
            actions = ExtractFallbackChoicePendingActions(text);
            if (actions.Count == 0) {
                // Don't clear existing pending actions on follow-up assistant messages that don't
                // include actionable markers/options.
                return;
            }

            fromFallbackChoices = true;
        }

        var assistantContext = text.Length <= MaxPendingActionAssistantContextChars
            ? text
            : text.Substring(0, MaxPendingActionAssistantContextChars);
        var callToActionTokens = fromFallbackChoices
            ? Array.Empty<string>()
            : ExtractPendingActionCallToActionTokens(assistantContext);
        PendingAction[]? snapshotActions = null;
        long snapshotTicks = 0;
        var shouldRemoveSnapshot = false;
        lock (_toolRoutingContextLock) {
            if (actions.Count == 0) {
                _pendingActionsByThreadId.Remove(normalizedThreadId);
                _pendingActionsSeenUtcTicks.Remove(normalizedThreadId);
                _pendingActionsCallToActionTokensByThreadId.Remove(normalizedThreadId);
                shouldRemoveSnapshot = true;
            } else {
                snapshotActions = actions.ToArray();
                snapshotTicks = DateTime.UtcNow.Ticks;
                _pendingActionsByThreadId[normalizedThreadId] = snapshotActions;
                _pendingActionsSeenUtcTicks[normalizedThreadId] = snapshotTicks;
                _pendingActionsCallToActionTokensByThreadId[normalizedThreadId] =
                    callToActionTokens.Length == 0 ? Array.Empty<string>() : callToActionTokens;
                TrimWeightedRoutingContextsNoLock();
            }
        }

        if (shouldRemoveSnapshot) {
            RemovePendingActionsSnapshot(normalizedThreadId);
            return;
        }
        if (snapshotActions is not null && snapshotActions.Length > 0 && snapshotTicks > 0) {
            PersistPendingActionsSnapshot(normalizedThreadId, snapshotTicks, snapshotActions, callToActionTokens);
        }
    }

    private bool TryResolvePendingActionSelection(string threadId, string userRequest, out string resolvedRequest) {
        resolvedRequest = userRequest ?? string.Empty;

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        var isExplicitAct = TryParseExplicitActSelection(normalized, out _, out _);
        // Keep action-selection matching available for longer contextual follow-ups too.
        // Safety remains in TryMatchPendingActionWithReason:
        // - actions not explicitly marked read-only still require explicit /act or ordinal selection
        // - structured payload-like inputs are rejected
        // - ambiguous overlap does not auto-select

        PendingAction[]? actions;
        long ticks;
        string[]? callToActionTokens;
        var usedSnapshot = false;
        lock (_toolRoutingContextLock) {
            _pendingActionsByThreadId.TryGetValue(normalizedThreadId, out actions);
            ticks = _pendingActionsSeenUtcTicks.TryGetValue(normalizedThreadId, out var seen) ? seen : 0;
            _pendingActionsCallToActionTokensByThreadId.TryGetValue(normalizedThreadId, out callToActionTokens);
        }

        if (actions is null || actions.Length == 0) {
            if (!TryLoadPendingActionsSnapshot(normalizedThreadId, out var persistedTicks, out var persistedActions, out var persistedCallToActionTokens)) {
                TracePendingActionDecision(
                    userText: normalized,
                    isExplicitAct: isExplicitAct,
                    actionsCount: 0,
                    usedSnapshot: false,
                    outcome: "skip",
                    reason: "no_pending_action_context");
                return false;
            }

            usedSnapshot = true;
            actions = persistedActions;
            ticks = persistedTicks;
            callToActionTokens = persistedCallToActionTokens;

            lock (_toolRoutingContextLock) {
                _pendingActionsByThreadId[normalizedThreadId] = actions;
                _pendingActionsSeenUtcTicks[normalizedThreadId] = ticks;
                if (callToActionTokens is not null && callToActionTokens.Length > 0) {
                    _pendingActionsCallToActionTokensByThreadId[normalizedThreadId] = callToActionTokens;
                } else {
                    _pendingActionsCallToActionTokensByThreadId.Remove(normalizedThreadId);
                }
                TrimWeightedRoutingContextsNoLock();
            }
        }

        if (ticks > 0) {
            if (!TryGetUtcDateTimeFromTicks(ticks, out var seenUtc)) {
                lock (_toolRoutingContextLock) {
                    _pendingActionsByThreadId.Remove(normalizedThreadId);
                    _pendingActionsSeenUtcTicks.Remove(normalizedThreadId);
                    _pendingActionsCallToActionTokensByThreadId.Remove(normalizedThreadId);
                    TrimWeightedRoutingContextsNoLock();
                }
                RemovePendingActionsSnapshot(normalizedThreadId);
                TracePendingActionDecision(
                    userText: normalized,
                    isExplicitAct: isExplicitAct,
                    actionsCount: actions?.Length ?? 0,
                    usedSnapshot: usedSnapshot,
                    outcome: "skip",
                    reason: "pending_action_ticks_invalid");
                return false;
            }

            var now = DateTime.UtcNow;
            if (seenUtc > now) {
                lock (_toolRoutingContextLock) {
                    _pendingActionsByThreadId.Remove(normalizedThreadId);
                    _pendingActionsSeenUtcTicks.Remove(normalizedThreadId);
                    _pendingActionsCallToActionTokensByThreadId.Remove(normalizedThreadId);
                    TrimWeightedRoutingContextsNoLock();
                }
                RemovePendingActionsSnapshot(normalizedThreadId);
                TracePendingActionDecision(
                    userText: normalized,
                    isExplicitAct: isExplicitAct,
                    actionsCount: actions?.Length ?? 0,
                    usedSnapshot: usedSnapshot,
                    outcome: "skip",
                    reason: "pending_action_context_in_future");
                return false;
            }

            var age = now - seenUtc;
            if (age > PendingActionContextMaxAge) {
                lock (_toolRoutingContextLock) {
                    _pendingActionsByThreadId.Remove(normalizedThreadId);
                    _pendingActionsSeenUtcTicks.Remove(normalizedThreadId);
                    _pendingActionsCallToActionTokensByThreadId.Remove(normalizedThreadId);
                    TrimWeightedRoutingContextsNoLock();
                }
                RemovePendingActionsSnapshot(normalizedThreadId);
                TracePendingActionDecision(
                    userText: normalized,
                    isExplicitAct: isExplicitAct,
                    actionsCount: actions?.Length ?? 0,
                    usedSnapshot: usedSnapshot,
                    outcome: "skip",
                    reason: "pending_action_context_expired");
                return false;
            }
        }

        var selected = TryMatchPendingActionWithReason(
                normalized,
                actions,
                callToActionTokens ?? Array.Empty<string>(),
                out var match,
                out var matchReason)
            ? match
            : (PendingAction?)null;
        if (selected is null) {
            TracePendingActionDecision(
                userText: normalized,
                isExplicitAct: isExplicitAct,
                actionsCount: actions?.Length ?? 0,
                usedSnapshot: usedSnapshot,
                outcome: "skip",
                reason: matchReason);
            return false;
        }

        // Consume pending actions to avoid stale "1" selections hitting old choices later.
        lock (_toolRoutingContextLock) {
            _pendingActionsByThreadId.Remove(normalizedThreadId);
            _pendingActionsSeenUtcTicks.Remove(normalizedThreadId);
            _pendingActionsCallToActionTokensByThreadId.Remove(normalizedThreadId);
            TrimWeightedRoutingContextsNoLock();
        }
        RemovePendingActionsSnapshot(normalizedThreadId);

        var request = string.IsNullOrWhiteSpace(selected.Value.Request) ? selected.Value.Title : selected.Value.Request;
        if (string.IsNullOrWhiteSpace(request)) {
            TracePendingActionDecision(
                userText: normalized,
                isExplicitAct: isExplicitAct,
                actionsCount: actions?.Length ?? 0,
                usedSnapshot: usedSnapshot,
                outcome: "skip",
                reason: "matched_action_missing_request",
                selectedActionId: selected.Value.Id);
            return false;
        }

        // Hand off the selection as structured data (so downstream stages treat it as data, not a privileged block).
        resolvedRequest = BuildActionSelectionPayloadJson(
            actionId: selected.Value.Id.Trim(),
            title: selected.Value.Title.Trim(),
            request: request,
            mutability: selected.Value.Mutability);
        TracePendingActionDecision(
            userText: normalized,
            isExplicitAct: isExplicitAct,
            actionsCount: actions?.Length ?? 0,
            usedSnapshot: usedSnapshot,
            outcome: "match",
            reason: matchReason,
            selectedActionId: selected.Value.Id);
        return true;
    }

    private static bool TryBuildSinglePendingActionSelectionPayload(string assistantDraft, out string payloadJson, out string actionId) {
        payloadJson = string.Empty;
        actionId = string.Empty;

        var actions = ExtractPendingActions(assistantDraft);
        if (actions.Count == 0) {
            actions = ExtractFallbackChoicePendingActions(assistantDraft);
        }
        if (actions.Count != 1) {
            return false;
        }

        var action = actions[0];
        actionId = (action.Id ?? string.Empty).Trim();
        if (actionId.Length == 0) {
            return false;
        }

        var title = (action.Title ?? string.Empty).Trim();
        var request = string.IsNullOrWhiteSpace(action.Request) ? title : action.Request.Trim();
        if (request.Length == 0) {
            return false;
        }

        payloadJson = BuildActionSelectionPayloadJson(
            actionId: actionId,
            title: title,
            request: request,
            mutability: action.Mutability);
        return true;
    }

    private static bool TryMatchPendingAction(string userText, IReadOnlyList<PendingAction> actions, IReadOnlyList<string> callToActionTokens, out PendingAction match) {
        return TryMatchPendingActionWithReason(userText, actions, callToActionTokens, out match, out _);
    }

    private static bool TryMatchPendingActionWithReason(string userText, IReadOnlyList<PendingAction> actions, IReadOnlyList<string> callToActionTokens, out PendingAction match, out string reason) {
        match = default;
        reason = "no_match";

        // Be careful with normalization: explicit selections like `/act <id>` should treat `<id>` as an opaque token.
        // Applying FormKC to the whole input can change codepoints and prevent matching an otherwise valid ID copied
        // from the assistant output.
        var trimmed = (userText ?? string.Empty).Trim();

        if (trimmed.Length == 0) {
            reason = "empty_follow_up";
            return false;
        }

        if (actions.Count == 0) {
            reason = "no_actions_available";
            return false;
        }

        if (TryParseExplicitActSelection(trimmed, out var id, out var explicitActReason)) {
            if (id.Length == 0) {
                reason = explicitActReason;
                return false;
            }
            for (var i = 0; i < actions.Count; i++) {
                if (string.Equals(actions[i].Id, id, StringComparison.OrdinalIgnoreCase)) {
                    match = actions[i];
                    reason = "explicit_act_id";
                    return true;
                }
            }

            reason = "act_id_not_found";
            return false;
        }

        // Normalize for ordinal + implicit confirm only.
        string normalized;
        try {
            normalized = trimmed.Normalize(NormalizationForm.FormKC);
        } catch (ArgumentException) {
            normalized = trimmed;
        }

        // "1" / "2" selects by ordinal.
        if (TryParseOrdinalSelection(normalized, out var idx) && idx > 0 && idx <= actions.Count) {
            match = actions[idx - 1];
            reason = "ordinal_selection";
            return true;
        }

        if (trimmed.IndexOfAny(PendingActionConfirmationQuestionPunctuation) >= 0
            || trimmed.IndexOfAny(PendingActionConfirmationDisqualifierPunctuation) >= 0
            || LooksLikeStructuredPendingActionConfirmationInput(trimmed)) {
            reason = "follow_up_disqualified_shape";
            return false;
        }

        var allowUnknownSingleActionCompactFollowUp = actions.Count == 1
                                                      && actions[0].Mutability == ActionMutability.Unknown
                                                      && UserMatchesPendingActionCallToActionTokens(trimmed, callToActionTokens)
                                                      && IsCompactUnknownPendingActionFollowUpWithCtaContext(trimmed, callToActionTokens);

        // Fail closed for actions that are not explicitly marked read-only unless this is a compact
        // continuation follow-up in an assistant CTA context for a single unknown action.
        if (actions.Count == 1
            && RequiresExplicitPendingActionSelection(actions[0])
            && !allowUnknownSingleActionCompactFollowUp) {
            reason = "mutating_action_requires_explicit_selection";
            return false;
        }

        // If there's only one pending action, allow the user to echo an assistant-provided call-to-action phrase.
        // This is language-agnostic, avoids locale-specific phrase lists in the host, and scopes matching to the
        // assistant-provided CTA tokens (not arbitrary substrings in the assistant message).
        if (actions.Count == 1
            && !string.IsNullOrWhiteSpace(actions[0].Id)
            && callToActionTokens is { Count: > 0 }
            && UserMatchesPendingActionCallToActionTokens(trimmed, callToActionTokens)) {
            match = actions[0];
            reason = "cta_echo_selection";
            return true;
        }

        if (allowUnknownSingleActionCompactFollowUp) {
            match = actions[0];
            reason = "unknown_single_compact_follow_up_with_cta_context";
            return true;
        }

        if (TryMatchPendingActionByIntentOverlapWithReason(trimmed, actions, out var overlapMatch, out var overlapReason)) {
            if (RequiresExplicitPendingActionSelection(overlapMatch)) {
                reason = "mutating_action_requires_explicit_selection";
                return false;
            }
            match = overlapMatch;
            reason = overlapReason;
            return true;
        }

        reason = overlapReason;
        return false;
    }

    private static bool RequiresExplicitPendingActionSelection(PendingAction action) {
        return action.Mutability != ActionMutability.ReadOnly;
    }

    private static bool IsCompactUnknownPendingActionFollowUpWithCtaContext(string userText, IReadOnlyList<string> callToActionTokens) {
        if (callToActionTokens is null || callToActionTokens.Count == 0) {
            return false;
        }

        var normalized = (userText ?? string.Empty).Trim();
        if (ContainsInvalidUnicodeSequence(normalized)) {
            return false;
        }
        if (!LooksLikeContinuationFollowUp(normalized)) {
            return false;
        }

        for (var i = 0; i < callToActionTokens.Count; i++) {
            if (ContainsInvalidUnicodeSequence(callToActionTokens[i])) {
                return false;
            }
        }

        if (ContainsQuestionSignal(normalized) || LooksLikeStructuredPendingActionConfirmationInput(normalized)) {
            return false;
        }

        var tokenCount = CountLetterDigitTokens(normalized, maxTokens: 10);
        return tokenCount is >= 2 and <= 6;
    }

    private static bool TryMatchPendingActionByIntentOverlap(string userText, IReadOnlyList<PendingAction> actions, out PendingAction match) {
        return TryMatchPendingActionByIntentOverlapWithReason(userText, actions, out match, out _);
    }

    private static bool TryMatchPendingActionByIntentOverlapWithReason(string userText, IReadOnlyList<PendingAction> actions, out PendingAction match, out string reason) {
        match = default;
        reason = "intent_overlap_no_match";
        if (actions is null || actions.Count == 0) {
            reason = "intent_overlap_no_actions";
            return false;
        }

        var userTokens = ExtractPendingActionIntentTokens(userText, maxTokens: 12);
        if (userTokens.Count == 0) {
            reason = "intent_overlap_no_user_tokens";
            return false;
        }

        var bestIndex = -1;
        var bestHits = 0;
        var bestCoverage = 0d;
        var bestLastHitIndex = -1;
        var bestLongestMatchedTokenLength = 0;
        var bestLongestMatchedTokenContainsNonAscii = false;
        var tieOnBest = false;

        for (var i = 0; i < actions.Count; i++) {
            var action = actions[i];
            if (string.IsNullOrWhiteSpace(action.Id)) {
                continue;
            }

            var actionTokens = ExtractPendingActionIntentTokens(BuildPendingActionIntentText(action), maxTokens: 24);
            if (actionTokens.Count == 0) {
                continue;
            }

            var hits = 0;
            var lastHitIndex = -1;
            var longestMatchedTokenLength = 0;
            var longestMatchedTokenContainsNonAscii = false;
            for (var userIndex = 0; userIndex < userTokens.Count; userIndex++) {
                var userToken = userTokens[userIndex];
                if (!TokenOverlapsPendingActionIntent(userToken, actionTokens)) {
                    continue;
                }

                hits++;
                lastHitIndex = userIndex;
                var tokenContainsNonAscii = TokenContainsNonAscii(userToken);
                if (userToken.Length > longestMatchedTokenLength) {
                    longestMatchedTokenLength = userToken.Length;
                    longestMatchedTokenContainsNonAscii = tokenContainsNonAscii;
                } else if (userToken.Length == longestMatchedTokenLength && tokenContainsNonAscii) {
                    longestMatchedTokenContainsNonAscii = true;
                }
            }

            if (hits == 0) {
                continue;
            }

            var coverage = hits / (double)userTokens.Count;

            if (hits > bestHits || (hits == bestHits && coverage > bestCoverage)) {
                bestIndex = i;
                bestHits = hits;
                bestCoverage = coverage;
                bestLastHitIndex = lastHitIndex;
                bestLongestMatchedTokenLength = longestMatchedTokenLength;
                bestLongestMatchedTokenContainsNonAscii = longestMatchedTokenContainsNonAscii;
                tieOnBest = false;
                continue;
            }

            if (hits == bestHits && Math.Abs(coverage - bestCoverage) <= 0.0001d) {
                tieOnBest = true;
            }
        }

        if (bestIndex < 0 || tieOnBest) {
            reason = tieOnBest ? "intent_overlap_ambiguous" : "intent_overlap_no_hits";
            return false;
        }

        if (actions.Count > 1) {
            if (bestHits < 2) {
                reason = "intent_overlap_multi_too_weak";
                return false;
            }

            match = actions[bestIndex];
            reason = "intent_overlap_multi";
            return true;
        }

        if (!SingleActionIntentOverlapIsStrongEnough(
                userTokenCount: userTokens.Count,
                hitCount: bestHits,
                lastHitIndex: bestLastHitIndex,
                longestMatchedTokenLength: bestLongestMatchedTokenLength,
                longestMatchedTokenContainsNonAscii: bestLongestMatchedTokenContainsNonAscii)) {
            reason = "intent_overlap_single_too_weak";
            return false;
        }

        match = actions[bestIndex];
        reason = "intent_overlap_single";
        return true;
    }

    private static void TracePendingActionDecision(
        string userText,
        bool isExplicitAct,
        int actionsCount,
        bool usedSnapshot,
        string outcome,
        string reason,
        string? selectedActionId = null) {
        var normalized = (userText ?? string.Empty).Trim();
        var tokenCount = CountLetterDigitTokens(normalized, maxTokens: 16);
        var selected = string.IsNullOrWhiteSpace(selectedActionId) ? "-" : selectedActionId.Trim();
        var source = actionsCount <= 0 ? "none" : (usedSnapshot ? "snapshot" : "memory");
        var kind = isExplicitAct ? "explicit_act" : "follow_up";

        Console.Error.WriteLine(
            $"[pending-action] outcome={outcome} reason={reason} kind={kind} source={source} actions={Math.Max(0, actionsCount)} tokens={tokenCount} selected={selected}");
    }

    private static bool SingleActionIntentOverlapIsStrongEnough(
        int userTokenCount,
        int hitCount,
        int lastHitIndex,
        int longestMatchedTokenLength,
        bool longestMatchedTokenContainsNonAscii) {
        if (hitCount <= 0 || userTokenCount <= 0 || lastHitIndex < 0) {
            return false;
        }

        if (hitCount >= 2) {
            return true;
        }

        if (hitCount != 1) {
            return false;
        }

        if (userTokenCount == 1) {
            return true;
        }

        if (userTokenCount == 2) {
            // Require the matched token as trailing intent for short two-token follow-ups ("please run").
            // Keep script-aware minimums so short non-Latin intent tokens remain eligible while
            // punctuation-only tails still cannot confirm.
            var minTrailingTokenLength = longestMatchedTokenContainsNonAscii ? 2 : 3;
            return lastHitIndex == 1 && longestMatchedTokenLength >= minTrailingTokenLength;
        }

        // For longer follow-ups with a single overlap hit, keep it conservative:
        // - match must be in the trailing slot (for example "... run")
        // - overlap must still cover at least one-third of meaningful tokens
        return lastHitIndex == userTokenCount - 1
               && hitCount * 3 >= userTokenCount;
    }

    private static bool TokenContainsNonAscii(string token) {
        var value = token ?? string.Empty;
        for (var i = 0; i < value.Length; i++) {
            if (value[i] > 127) {
                return true;
            }
        }

        return false;
    }

    private static string BuildPendingActionIntentText(PendingAction action) {
        var title = (action.Title ?? string.Empty).Trim();
        var request = (action.Request ?? string.Empty).Trim();
        if (title.Length == 0) {
            return request;
        }
        if (request.Length == 0) {
            return title;
        }

        return title + " " + request;
    }

    private static string BuildActionSelectionPayloadJson(string actionId, string title, string request, ActionMutability mutability) {
        var normalizedRequest = CollapseWhitespace((request ?? string.Empty).Trim());
        var selection = new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["id"] = (actionId ?? string.Empty).Trim(),
            ["title"] = (title ?? string.Empty).Trim(),
            ["request"] = normalizedRequest
        };

        if (mutability == ActionMutability.Mutating) {
            selection["mutating"] = true;
        } else if (mutability == ActionMutability.ReadOnly) {
            selection["mutating"] = false;
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["ix_action_selection"] = selection
        });
    }


}
