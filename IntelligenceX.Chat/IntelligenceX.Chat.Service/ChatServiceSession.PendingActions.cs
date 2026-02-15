using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const string ActionMarker = "ix:action:v1";
    private const int MaxActionParsingChars = 64 * 1024;
    private const int MaxPendingActionAssistantContextChars = 4096;
    private const int MaxFallbackChoiceActionTitleChars = 96;
    private static readonly char[] PendingActionConfirmationQuestionPunctuation = new[] { '?', '？', '¿', '؟' };
    private static readonly char[] PendingActionConfirmationDisqualifierPunctuation = new[] { ':', ';', '\uFF1A', '\uFF1B' }; // ： ；
    private static readonly char[] PendingActionConfirmationStructuredDisqualifierChars =
        new[] { '\\', '{', '}', '[', ']', '<', '>', '=' };
    private readonly record struct FallbackChoiceCandidate(string Title, bool IsNumbered, string ActionId);

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
    private readonly record struct PendingAction(string Id, string Title, string Request);

    private void RememberPendingActions(string threadId, string assistantText) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var text = assistantText ?? string.Empty;
        var markerIdx = text.IndexOf(ActionMarker, StringComparison.OrdinalIgnoreCase);
        bool fromFallbackChoices;
        List<PendingAction> actions;
        if (markerIdx < 0) {
            // Fallback: allow compact assistant choice lists (for example bullet options) to be
            // selected naturally on the next user turn, even when the model omitted ix:action blocks.
            actions = ExtractFallbackChoicePendingActions(text);
            if (actions.Count == 0) {
                // Don't clear existing pending actions on follow-up assistant messages that don't
                // include actionable markers/options.
                return;
            }

            fromFallbackChoices = true;
        } else {
            if (text.Length > MaxActionParsingChars) {
                // Keep a window around the first marker to cap worst-case parsing work.
                var start = Math.Max(0, markerIdx - 256);
                var len = Math.Min(MaxActionParsingChars, text.Length - start);
                text = text.Substring(start, len);
            }

            actions = ExtractPendingActions(text);
            fromFallbackChoices = false;
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

        // Only apply selection rewriting for explicit /act <id>, or when the message looks like a short follow-up.
        // This prevents rewriting normal user messages that happen to start with digits (e.g., "2 servers are down").
        var isExplicitAct = normalized.StartsWith("/act", StringComparison.OrdinalIgnoreCase);
        if (!isExplicitAct && !LooksLikeContinuationFollowUp(normalized)) {
            return false;
        }

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
        resolvedRequest = JsonSerializer.Serialize(new {
            ix_action_selection = new {
                id = selected.Value.Id.Trim(),
                title = selected.Value.Title.Trim(),
                request = CollapseWhitespace(request).Trim()
            }
        });
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

        // /act <id>
        if (trimmed.StartsWith("/act", StringComparison.OrdinalIgnoreCase)) {
            // Require `/act` as a standalone token; avoid accidentally treating `/actuator` etc. as an action selection.
            if (trimmed.Length > 4 && !char.IsWhiteSpace(trimmed[4])) {
                reason = "invalid_act_command_token";
                return false;
            }

            var rest = trimmed[4..].Trim();
            if (rest.Length == 0) {
                reason = "act_id_missing";
                return false;
            }
            var id = ReadFirstToken(rest);
            if (id.Length == 0) {
                reason = "act_id_missing";
                return false;
            }
            var trailing = rest[id.Length..].Trim();
            if (trailing.Length != 0) {
                reason = "act_command_has_trailing_tokens";
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

        if (TryMatchPendingActionByIntentOverlapWithReason(trimmed, actions, out var overlapMatch, out var overlapReason)) {
            match = overlapMatch;
            reason = overlapReason;
            return true;
        }

        reason = overlapReason;
        return false;
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
            for (var userIndex = 0; userIndex < userTokens.Count; userIndex++) {
                var userToken = userTokens[userIndex];
                if (!TokenOverlapsPendingActionIntent(userToken, actionTokens)) {
                    continue;
                }

                hits++;
                lastHitIndex = userIndex;
                if (userToken.Length > longestMatchedTokenLength) {
                    longestMatchedTokenLength = userToken.Length;
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
                longestMatchedTokenLength: bestLongestMatchedTokenLength)) {
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

    private static bool SingleActionIntentOverlapIsStrongEnough(int userTokenCount, int hitCount, int lastHitIndex, int longestMatchedTokenLength) {
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
            // Also require at least 3 letters/digits so punctuation-only tails never confirm.
            return lastHitIndex == 1 && longestMatchedTokenLength >= 3;
        }

        // For longer follow-ups with a single overlap hit, keep it conservative:
        // - match must be in the trailing slot (for example "... run")
        // - overlap must still cover at least one-third of meaningful tokens
        return lastHitIndex == userTokenCount - 1
               && hitCount * 3 >= userTokenCount;
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

    private static List<string> ExtractPendingActionIntentTokens(string text, int maxTokens) {
        var normalized = NormalizeCompactText(text);
        if (normalized.Length == 0 || maxTokens <= 0) {
            return new List<string>();
        }

        var tokens = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var token = new StringBuilder();

        for (var i = 0; i <= normalized.Length; i++) {
            var ch = i < normalized.Length ? normalized[i] : '\0';
            var isTokenChar = i < normalized.Length && char.IsLetterOrDigit(ch);
            if (isTokenChar) {
                token.Append(char.ToLowerInvariant(ch));
                continue;
            }

            if (token.Length == 0) {
                continue;
            }

            var value = token.ToString();
            token.Clear();

            if (!LooksLikePendingActionIntentToken(value)) {
                continue;
            }

            if (!seen.Add(value)) {
                continue;
            }

            tokens.Add(value);
            if (tokens.Count >= maxTokens) {
                break;
            }
        }

        return tokens;
    }

    private static bool LooksLikePendingActionIntentToken(string token) {
        var value = (token ?? string.Empty).Trim();
        if (value.Length == 0) {
            return false;
        }

        var hasDigit = false;
        var hasLetter = false;
        for (var i = 0; i < value.Length; i++) {
            var ch = value[i];
            if (!char.IsLetterOrDigit(ch)) {
                return false;
            }
            if (char.IsDigit(ch)) {
                hasDigit = true;
            }
            if (char.IsLetter(ch)) {
                hasLetter = true;
            }
        }

        if (hasDigit) {
            return true;
        }

        if (!hasLetter) {
            return false;
        }

        return value.Length >= 3;
    }

    private static bool TokenOverlapsPendingActionIntent(string token, IReadOnlyList<string> actionTokens) {
        var value = (token ?? string.Empty).Trim();
        if (value.Length == 0 || actionTokens.Count == 0) {
            return false;
        }

        for (var i = 0; i < actionTokens.Count; i++) {
            var actionToken = (actionTokens[i] ?? string.Empty).Trim();
            if (actionToken.Length == 0) {
                continue;
            }

            if (string.Equals(value, actionToken, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            var minSharedLength = Math.Min(value.Length, actionToken.Length);
            if (minSharedLength < 5) {
                continue;
            }

            if (value.StartsWith(actionToken, StringComparison.OrdinalIgnoreCase)
                || actionToken.StartsWith(value, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static string ReadFirstToken(string text) {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0) {
            return string.Empty;
        }
        var end = 0;
        while (end < value.Length && !char.IsWhiteSpace(value[end])) {
            end++;
        }
        return end <= 0 ? string.Empty : value.Substring(0, end).Trim();
    }

    private static bool TryParseOrdinalSelection(string text, out int value) {
        value = 0;
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        var i = 0;
        while (i < normalized.Length && char.IsDigit(normalized[i])) {
            i++;
        }
        if (i == 0) {
            return false;
        }

        var digits = normalized.Substring(0, i);
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value)) {
            return false;
        }

        var rest = normalized[i..].Trim();
        if (rest.Length == 0) {
            return true;
        }

        // Allow simple punctuation variants like "2." or "2)".
        return rest is "." or ")" or "]" or ":";
    }

    private static List<PendingAction> ExtractPendingActions(string assistantText) {
        var text = assistantText ?? string.Empty;
        if (text.Length == 0) {
            return new List<PendingAction>();
        }

        // Cheap precheck to avoid parsing when the marker isn't present.
        if (text.IndexOf(ActionMarker, StringComparison.OrdinalIgnoreCase) < 0) {
            return new List<PendingAction>();
        }

        var actions = new List<PendingAction>();
        var lines = SplitLines(text);
        var inFence = false;
        for (var i = 0; i < lines.Count; i++) {
            var line = lines[i];
            var trimmedLine = (line ?? string.Empty).Trim();
            if (trimmedLine.StartsWith("```", StringComparison.Ordinal)) {
                inFence = !inFence;
                continue;
            }

            if (inFence) {
                continue;
            }

            if (!string.Equals(trimmedLine, ActionMarker, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            // Only accept action blocks in the documented envelope ("[Action]" preceding the marker line),
            // to avoid caching echoed/quoted content.
            var k = i - 1;
            while (k >= 0 && string.IsNullOrWhiteSpace(lines[k])) {
                k--;
            }
            if (k < 0 || !string.Equals((lines[k] ?? string.Empty).Trim(), "[Action]", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            // Parse key/value lines following the marker.
            string? id = null;
            string? title = null;
            string? reply = null;
            var request = new StringBuilder();
            var sawRequest = false;

            for (var j = i + 1; j < lines.Count; j++) {
                var current = lines[j];
                if (string.IsNullOrWhiteSpace(current)) {
                    break;
                }
                if (current.StartsWith("ix:", StringComparison.OrdinalIgnoreCase)) {
                    break;
                }

                if (TryReadField(current, "id", out var field)) {
                    id = field;
                    continue;
                }
                if (TryReadField(current, "title", out field)) {
                    title = field;
                    continue;
                }
                if (TryReadField(current, "request", out field)) {
                    sawRequest = true;
                    if (!string.IsNullOrWhiteSpace(field)) {
                        if (request.Length > 0) {
                            request.Append(' ');
                        }
                        request.Append(field.Trim());
                    }
                    continue;
                }
                if (TryReadField(current, "reply", out field)) {
                    reply = field;
                    continue;
                }

                // Support multi-line request values by treating any additional non-empty lines as
                // request continuation once we've seen the request field.
                if (sawRequest) {
                    var part = current.Trim();
                    if (part.Length > 0) {
                        if (request.Length > 0) {
                            request.Append(' ');
                        }
                        request.Append(part);
                    }
                }
            }

            id = (id ?? string.Empty).Trim();
            title = (title ?? string.Empty).Trim();
            var req = request.ToString().Trim();
            reply = (reply ?? string.Empty).Trim();

            if (id.Length == 0) {
                continue;
            }
            if (!LooksLikeValidActionReply(reply, id)) {
                // Avoid caching truncated/partial action blocks that can't be reliably selected later.
                continue;
            }
            if (id.Length > 64) {
                id = id.Substring(0, 64);
            }
            if (title.Length > 200) {
                title = title.Substring(0, 200);
            }
            if (req.Length > 600) {
                req = req.Substring(0, 600);
            }

            actions.Add(new PendingAction(Id: id, Title: title, Request: req));
            if (actions.Count >= 6) {
                break;
            }
        }

        // De-dupe ids.
        if (actions.Count > 1) {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            actions = actions.Where(a => a.Id.Length > 0 && seen.Add(a.Id)).ToList();
        }

        return actions;
    }

    private static bool LooksLikeValidActionReply(string reply, string id) {
        var normalizedReply = (reply ?? string.Empty).Trim();
        var normalizedId = (id ?? string.Empty).Trim();
        if (normalizedReply.Length == 0 || normalizedId.Length == 0) {
            return false;
        }

        // Prefer explicit /act <id> to avoid false matches and to ensure action blocks are "complete".
        if (normalizedReply.StartsWith("/act", StringComparison.OrdinalIgnoreCase)) {
            var rest = normalizedReply[4..].Trim();
            var token = ReadFirstToken(rest);
            if (token.Length == 0 || !string.Equals(token, normalizedId, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            var trailing = rest[token.Length..].Trim();
            return trailing.Length == 0;
        }

        return false;
    }

    private static List<PendingAction> ExtractFallbackChoicePendingActions(string assistantText) {
        var text = assistantText ?? string.Empty;
        if (text.Length == 0) {
            return new List<PendingAction>();
        }

        var lines = SplitLines(text);
        var inFence = false;
        var candidateChoices = new List<FallbackChoiceCandidate>();
        var candidateStartLine = -1;
        for (var i = 0; i < lines.Count; i++) {
            var trimmedLine = (lines[i] ?? string.Empty).Trim();
            if (trimmedLine.StartsWith("```", StringComparison.Ordinal)) {
                inFence = !inFence;
                continue;
            }

            if (inFence) {
                continue;
            }

            if (TryExtractFallbackChoiceTitle(trimmedLine, out var title, out var isNumbered, out var actionId)) {
                if (candidateStartLine < 0) {
                    candidateStartLine = i;
                }
                candidateChoices.Add(new FallbackChoiceCandidate(title, isNumbered, actionId));
                continue;
            }

            if (ShouldBuildFallbackChoiceActions(candidateChoices, lines, candidateStartLine)) {
                return BuildFallbackChoicePendingActions(candidateChoices);
            }

            candidateChoices.Clear();
            candidateStartLine = -1;
        }

        if (ShouldBuildFallbackChoiceActions(candidateChoices, lines, candidateStartLine)) {
            return BuildFallbackChoicePendingActions(candidateChoices);
        }

        return new List<PendingAction>();
    }

    private static bool ShouldBuildFallbackChoiceActions(
        IReadOnlyList<FallbackChoiceCandidate> choices,
        IReadOnlyList<string> lines,
        int firstChoiceLine) {
        if (!LooksLikeFallbackChoicePromptContext(lines, firstChoiceLine)) {
            return false;
        }

        if (choices is null || choices.Count == 0) {
            return false;
        }

        if (choices.Count >= 2) {
            return true;
        }

        // Single-option fallback is intentionally conservative:
        // only allow it for explicitly numbered option lines (e.g., "1. ..."),
        // which usually represent an actionable "pick this" list.
        return choices.Count == 1 && choices[0].IsNumbered;
    }

    private static bool TryExtractFallbackChoiceTitle(string trimmedLine, out string title, out bool isNumbered, out string actionId) {
        title = string.Empty;
        isNumbered = false;
        actionId = string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedLine)) {
            return false;
        }

        var value = trimmedLine.Trim();
        if (value.Length == 0) {
            return false;
        }

        var startsWithBullet = false;
        if (value.StartsWith("- ", StringComparison.Ordinal)
            || value.StartsWith("* ", StringComparison.Ordinal)
            || value.StartsWith("• ", StringComparison.Ordinal)
            || value.StartsWith("– ", StringComparison.Ordinal)
            || value.StartsWith("— ", StringComparison.Ordinal)) {
            value = value.Substring(2).Trim();
            startsWithBullet = true;
        } else {
            var idx = 0;
            while (idx < value.Length && char.IsDigit(value[idx])) {
                idx++;
            }

            if (idx > 0 && idx < value.Length) {
                var marker = value[idx];
                if ((marker == '.' || marker == ')' || marker == ':' || marker == ']')
                    && idx + 1 < value.Length
                    && char.IsWhiteSpace(value[idx + 1])) {
                    value = value[(idx + 2)..].Trim();
                    startsWithBullet = true;
                    isNumbered = true;
                }
            }
        }

        if (!startsWithBullet || value.Length == 0) {
            return false;
        }

        if (TryExtractTrailingFallbackActionId(value, out var inlineActionId, out var valueWithoutInlineActionId)) {
            actionId = inlineActionId;
            value = valueWithoutInlineActionId;
        }

        if (value.Length == 0) {
            return false;
        }

        if (value.Length > MaxFallbackChoiceActionTitleChars) {
            return false;
        }

        if (value.IndexOf(':', StringComparison.Ordinal) >= 0
            || value.IndexOf("http", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf('{', StringComparison.Ordinal) >= 0
            || value.IndexOf('[', StringComparison.Ordinal) >= 0
            || value.IndexOf('<', StringComparison.Ordinal) >= 0) {
            return false;
        }

        var tokenCount = CountLetterDigitTokens(value, maxTokens: 12);
        if (tokenCount == 0 || tokenCount > 10) {
            return false;
        }

        var hasLetter = false;
        for (var i = 0; i < value.Length; i++) {
            if (char.IsLetter(value[i])) {
                hasLetter = true;
                break;
            }
        }

        if (!hasLetter) {
            return false;
        }

        title = CollapseWhitespace(value).Trim();
        return title.Length > 0;
    }

    private static bool TryExtractTrailingFallbackActionId(string value, out string actionId, out string cleanedTitle) {
        actionId = string.Empty;
        cleanedTitle = (value ?? string.Empty).Trim();
        if (cleanedTitle.Length == 0) {
            return false;
        }

        var actIndex = cleanedTitle.LastIndexOf("/act", StringComparison.OrdinalIgnoreCase);
        if (actIndex < 0) {
            return false;
        }

        // Prefer the common "Title (... /act id ...)" form and strip only the trailing parenthetical block.
        var openParen = cleanedTitle.LastIndexOf('(', actIndex);
        var closeParen = cleanedTitle.IndexOf(')', actIndex);
        if (openParen >= 0 && closeParen > actIndex) {
            var trailingAfterClose = cleanedTitle[(closeParen + 1)..].Trim();
            if (trailingAfterClose.Length == 0 || AllCharsAllowedInTrailingFallbackActionSuffix(trailingAfterClose)) {
                var inner = cleanedTitle.Substring(openParen + 1, closeParen - openParen - 1).Trim();
                if (TryExtractFallbackActionIdFromSegment(inner, out actionId)) {
                    cleanedTitle = cleanedTitle[..openParen].Trim();
                    return cleanedTitle.Length > 0;
                }
            }
        }

        // Fallback: inline trailing "/act id" with optional punctuation.
        if (actIndex > 0 && !char.IsWhiteSpace(cleanedTitle[actIndex - 1]) && cleanedTitle[actIndex - 1] is not '(' and not '[' and not '{') {
            return false;
        }

        var segment = cleanedTitle[actIndex..].Trim();
        if (!TryExtractFallbackActionIdFromSegment(segment, out actionId)) {
            return false;
        }

        cleanedTitle = cleanedTitle[..actIndex].TrimEnd();
        cleanedTitle = cleanedTitle.TrimEnd('-', '–', '—', '(', '[', '{').TrimEnd();
        return cleanedTitle.Length > 0;
    }

    private static bool TryExtractFallbackActionIdFromSegment(string segment, out string actionId) {
        actionId = string.Empty;
        var text = (segment ?? string.Empty).Trim();
        if (!text.StartsWith("/act", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (text.Length > 4 && !char.IsWhiteSpace(text[4])) {
            return false;
        }

        var rest = text[4..].Trim();
        if (rest.Length == 0) {
            return false;
        }

        var token = ReadFirstToken(rest);
        if (token.Length == 0) {
            return false;
        }

        var normalizedId = NormalizeFallbackActionIdToken(token);
        if (normalizedId.Length == 0) {
            return false;
        }

        var trailing = rest[token.Length..].Trim();
        if (trailing.Length > 0 && !AllCharsAllowedInTrailingFallbackActionSuffix(trailing)) {
            return false;
        }

        actionId = normalizedId;
        return true;
    }

    private static string NormalizeFallbackActionIdToken(string token) {
        var value = (token ?? string.Empty).Trim();
        if (value.Length == 0) {
            return string.Empty;
        }

        value = value.TrimStart('(', '[', '{').TrimEnd(')', ']', '}', ',', '.', ';', ':');
        if (value.Length == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder(Math.Min(value.Length, 64));
        for (var i = 0; i < value.Length && sb.Length < 64; i++) {
            var ch = value[i];
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.') {
                sb.Append(ch);
            }
        }

        return sb.ToString().Trim();
    }

    private static bool AllCharsAllowedInTrailingFallbackActionSuffix(string text) {
        for (var i = 0; i < text.Length; i++) {
            var ch = text[i];
            if (char.IsWhiteSpace(ch)) {
                continue;
            }
            if (ch is ')' or ']' or '}' or ',' or '.' or ';' or ':' or '!') {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool LooksLikeFallbackChoicePromptContext(IReadOnlyList<string> lines, int firstChoiceLine) {
        if (lines is null || lines.Count == 0 || firstChoiceLine < 0 || firstChoiceLine >= lines.Count) {
            return false;
        }

        for (var i = firstChoiceLine - 1; i >= 0 && firstChoiceLine - i <= 3; i--) {
            var trimmed = (lines[i] ?? string.Empty).Trim();
            if (trimmed.Length == 0) {
                continue;
            }

            return trimmed.EndsWith(":", StringComparison.Ordinal)
                   || trimmed.EndsWith("?", StringComparison.Ordinal)
                   || trimmed.EndsWith("：", StringComparison.Ordinal)
                   || trimmed.EndsWith("？", StringComparison.Ordinal);
        }

        return false;
    }

    private static List<PendingAction> BuildFallbackChoicePendingActions(IReadOnlyList<FallbackChoiceCandidate> choices) {
        var actions = new List<PendingAction>();
        if (choices is null || choices.Count == 0) {
            return actions;
        }

        var seenActionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < choices.Count && actions.Count < 6; i++) {
            var title = (choices[i].Title ?? string.Empty).Trim();
            if (title.Length == 0) {
                continue;
            }

            var actionId = (choices[i].ActionId ?? string.Empty).Trim();
            if (actionId.Length == 0) {
                actionId = $"choice_{actions.Count + 1:D3}";
            }

            if (!seenActionIds.Add(actionId)) {
                continue;
            }

            actions.Add(new PendingAction(
                Id: actionId,
                Title: title,
                Request: title));
        }

        return actions;
    }

    private static bool TryReadField(string line, string name, out string value) {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(line)) {
            return false;
        }

        var trimmed = line.Trim();
        var idx = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (idx < 0) {
            return false;
        }

        var key = trimmed[..idx].Trim();
        if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        value = trimmed[(idx + 1)..].Trim();
        return true;
    }


    private static string NormalizeCompactCallToActionToken(string text) {
        // Assistant CTAs often appear in prose with trailing ':' / ';' (including fullwidth variants) that users
        // should not have to repeat, and that we explicitly disqualify for confirmation.
        var token = NormalizeCompactText(text);
        if (token.Length == 0) {
            return string.Empty;
        }

        token = token.TrimEnd(':', ';', '\uFF1A', '\uFF1B');
        return token.Trim();
    }
    private static string[] ExtractPendingActionCallToActionTokens(string assistantContext) {
        var draft = assistantContext ?? string.Empty;
        if (draft.Length == 0) {
            return Array.Empty<string>();
        }

        var phrases = ExtractQuotedPhrases(draft);
        if (phrases.Count == 0) {
            return Array.Empty<string>();
        }

        var tokens = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < phrases.Count; i++) {
            var phrase = phrases[i];
            if (!LooksLikeCallToActionContext(draft, phrase, onlyBulletContext: false)) {
                continue;
            }

            var token = NormalizeCompactCallToActionToken(phrase.Value);
            if (!LooksLikeCompactCallToActionToken(token)) {
                continue;
            }

            if (!seen.Add(token)) {
                continue;
            }

            tokens.Add(token);
            if (tokens.Count >= 6) {
                break;
            }
        }

        return tokens.Count == 0 ? Array.Empty<string>() : tokens.ToArray();
    }

    private static bool LooksLikeCompactCallToActionToken(string token) {
        var value = (token ?? string.Empty).Trim();
        if (value.Length == 0 || value.Length > 96) {
            return false;
        }

        if (value.Contains('\n', StringComparison.Ordinal) || value.Contains('\r', StringComparison.Ordinal)) {
            return false;
        }

        for (var i = 0; i < value.Length; i++) {
            if (char.IsControl(value[i])) {
                return false;
            }
        }

        // Keep it lean: only short, phrase-like tokens.
        var tokens = CountLetterDigitTokens(value, maxTokens: 12);
        return tokens is > 0 and <= 8;
    }

    private static bool UserMatchesPendingActionCallToActionTokens(string userText, IReadOnlyList<string> tokens) {
        if (tokens is null || tokens.Count == 0) {
            return false;
        }

        var raw = (userText ?? string.Empty).Trim();
        if (raw.Length == 0 || raw.Length > 96) {
            return false;
        }

        // Guardrails must run on raw input (pre-normalization) to avoid normalization widening matches.
        if (raw.IndexOfAny(PendingActionConfirmationQuestionPunctuation) >= 0) {
            return false;
        }
        if (raw.IndexOfAny(PendingActionConfirmationDisqualifierPunctuation) >= 0) {
            return false;
        }
        if (LooksLikeStructuredPendingActionConfirmationInput(raw)) {
            return false;
        }

        var request = NormalizeCompactText(raw);
        if (request.Length == 0 || request.Length > 96) {
            return false;
        }

        for (var i = 0; i < tokens.Count; i++) {
            var token = (tokens[i] ?? string.Empty).Trim();
            if (!LooksLikeCompactCallToActionToken(token)) {
                continue;
            }

            if (string.Equals(request, token, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static List<string> SplitLines(string text) {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) {
            return lines;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < text.Length; i++) {
            var ch = text[i];
            if (ch == '\r') {
                continue;
            }
            if (ch == '\n') {
                lines.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(ch);
        }

        lines.Add(sb.ToString());
        return lines;
    }
}
