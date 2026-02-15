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
    private static readonly char[] PendingActionConfirmationQuestionPunctuation = new[] { '?', '？', '¿', '؟' };
    private static readonly char[] PendingActionConfirmationDisqualifierPunctuation = new[] { ':', ';', '\uFF1A', '\uFF1B' }; // ： ；

    private readonly record struct PendingAction(string Id, string Title, string Request);

    private void RememberPendingActions(string threadId, string assistantText) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var text = assistantText ?? string.Empty;
        var markerIdx = text.IndexOf(ActionMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIdx < 0) {
            // Don't clear existing pending actions on follow-up assistant messages that don't
            // include any action markers.
            return;
        }

        if (text.Length > MaxActionParsingChars) {
            // Keep a window around the first marker to cap worst-case parsing work.
            var start = Math.Max(0, markerIdx - 256);
            var len = Math.Min(MaxActionParsingChars, text.Length - start);
            text = text.Substring(start, len);
        }

        var assistantContext = text.Length <= MaxPendingActionAssistantContextChars
            ? text
            : text.Substring(0, MaxPendingActionAssistantContextChars);
        var callToActionTokens = ExtractPendingActionCallToActionTokens(assistantContext);

        var actions = ExtractPendingActions(text);
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
        lock (_toolRoutingContextLock) {
            _pendingActionsByThreadId.TryGetValue(normalizedThreadId, out actions);
            ticks = _pendingActionsSeenUtcTicks.TryGetValue(normalizedThreadId, out var seen) ? seen : 0;
            _pendingActionsCallToActionTokensByThreadId.TryGetValue(normalizedThreadId, out callToActionTokens);
        }

        if (actions is null || actions.Length == 0) {
            if (!TryLoadPendingActionsSnapshot(normalizedThreadId, out var persistedTicks, out var persistedActions, out var persistedCallToActionTokens)) {
                return false;
            }

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
                return false;
            }
        }

        var selected = TryMatchPendingAction(normalized, actions, callToActionTokens ?? Array.Empty<string>(), out var match)
            ? match
            : (PendingAction?)null;
        if (selected is null) {
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
        return true;
    }

    private static bool TryMatchPendingAction(string userText, IReadOnlyList<PendingAction> actions, IReadOnlyList<string> callToActionTokens, out PendingAction match) {
        match = default;

        // Be careful with normalization: explicit selections like `/act <id>` should treat `<id>` as an opaque token.
        // Applying FormKC to the whole input can change codepoints and prevent matching an otherwise valid ID copied
        // from the assistant output.
        var trimmed = (userText ?? string.Empty).Trim();

        if (trimmed.Length == 0 || actions.Count == 0) {
            return false;
        }

        // /act <id>
        if (trimmed.StartsWith("/act", StringComparison.OrdinalIgnoreCase)) {
            // Require `/act` as a standalone token; avoid accidentally treating `/actuator` etc. as an action selection.
            if (trimmed.Length > 4 && !char.IsWhiteSpace(trimmed[4])) {
                return false;
            }

            var rest = trimmed[4..].Trim();
            if (rest.Length == 0) {
                return false;
            }
            var id = ReadFirstToken(rest);
            if (id.Length == 0) {
                return false;
            }
            var trailing = rest[id.Length..].Trim();
            if (trailing.Length != 0) {
                return false;
            }

            for (var i = 0; i < actions.Count; i++) {
                if (string.Equals(actions[i].Id, id, StringComparison.OrdinalIgnoreCase)) {
                    match = actions[i];
                    return true;
                }
            }

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
            return true;
        }

        // If there's only one pending action, allow the user to echo an assistant-provided call-to-action phrase.
        // This is language-agnostic, avoids locale-specific phrase lists in the host, and scopes matching to the
        // assistant-provided CTA tokens (not arbitrary substrings in the assistant message).
        if (actions.Count == 1
            && !string.IsNullOrWhiteSpace(actions[0].Id)
            && callToActionTokens is { Count: > 0 }
            && trimmed.IndexOfAny(PendingActionConfirmationQuestionPunctuation) < 0
            && trimmed.IndexOfAny(PendingActionConfirmationDisqualifierPunctuation) < 0
            && UserMatchesPendingActionCallToActionTokens(trimmed, callToActionTokens)) {
            match = actions[0];
            return true;
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

            var token = NormalizeCompactText(phrase.Value);
            if (token.Length == 0 || token.Length > 96) {
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

    private static bool UserMatchesPendingActionCallToActionTokens(string userText, IReadOnlyList<string> tokens) {
        if (tokens is null || tokens.Count == 0) {
            return false;
        }

        var request = NormalizeCompactText(userText);
        if (request.Length == 0 || request.Length > 120) {
            return false;
        }

        for (var i = 0; i < tokens.Count; i++) {
            var token = (tokens[i] ?? string.Empty).Trim();
            if (token.Length == 0) {
                continue;
            }

            if (string.Equals(request, token, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            if (ContainsPhraseWithBoundaries(request, token)) {
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
