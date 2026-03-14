using System;
using System.Collections.Generic;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Policy;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
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
