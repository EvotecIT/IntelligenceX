using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private static bool TryGetQuotePair(char openQuote, out char closeQuote, out bool apostropheLike) {
        apostropheLike = false;
        closeQuote = '\0';
        switch (openQuote) {
            case '"':
                closeQuote = '"';
                return true;
            case '\'':
                closeQuote = '\'';
                apostropheLike = true;
                return true;
            case '\u201C': // “
                closeQuote = '\u201D'; // ”
                return true;
            case '\u2018': // ‘
                closeQuote = '\u2019'; // ’
                apostropheLike = true;
                return true;
            case '\uFF02': // ＂
                closeQuote = '\uFF02';
                return true;
            case '\uFF07': // ＇
                closeQuote = '\uFF07';
                apostropheLike = true;
                return true;
            default:
                return false;
        }
    }

    private static string NormalizeCompactText(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        // Normalize can throw on invalid Unicode (e.g., lone surrogates). This function runs on raw user/assistant input,
        // so it must be exception-safe.
        try {
            normalized = normalized.Normalize(NormalizationForm.FormKC);
        } catch (ArgumentException) {
            // Leave as-is.
        }

        // Strip inline-code wrappers (`run now`) without trying to parse markdown fully.
        if (normalized.Length >= 2 && normalized[0] == '`' && normalized[^1] == '`') {
            normalized = normalized.Substring(1, normalized.Length - 2).Trim();
        }

        // Trim light punctuation wrappers so "run now?" and "\"run now\"" normalize.
        normalized = normalized.Trim().Trim(
            '"', '\'', '.', '!', '?', ',', '(', ')', 
            '\u201C', '\u201D', // “ ”
            '\u2018', '\u2019', // ‘ ’
            '\uFF02', '\uFF07'  // ＂ ＇
        );
        if (normalized.Length == 0) {
            return string.Empty;
        }

        // Collapse whitespace to stabilize matching across minor formatting differences.
        var sb = new StringBuilder(normalized.Length);
        var inSpace = false;
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (char.IsWhiteSpace(ch)) {
                if (!inSpace) {
                    sb.Append(' ');
                    inSpace = true;
                }
                continue;
            }

            inSpace = false;
            sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    private static bool ContainsPhraseWithBoundaries(string haystack, string needle) {
        if (haystack.Length == 0 || needle.Length == 0 || needle.Length > haystack.Length) {
            return false;
        }

        var startIndex = 0;
        while (true) {
            var idx = haystack.IndexOf(needle, startIndex, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) {
                return false;
            }

            var beforeOk = idx == 0 || !char.IsLetterOrDigit(haystack[idx - 1]);
            var afterIndex = idx + needle.Length;
            var afterOk = afterIndex >= haystack.Length || !char.IsLetterOrDigit(haystack[afterIndex]);
            if (beforeOk && afterOk) {
                return true;
            }

            startIndex = idx + 1;
            if (startIndex >= haystack.Length) {
                return false;
            }
        }
    }

    private static bool LooksLikeCompactFollowUp(string userRequest) {
        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (normalized.Contains('\n', StringComparison.Ordinal)) {
            return false;
        }

        if (normalized.Length > 80) {
            return false;
        }

        var tokenCount = CountLetterDigitTokens(normalized, maxTokens: 12);
        if (tokenCount == 0) {
            return false;
        }

        if (tokenCount <= 6 && normalized.Length <= 64) {
            return true;
        }

        return tokenCount <= FollowUpQuestionMaxTokens && normalized.Length <= 80 && ContainsQuestionSignal(normalized);
    }

    private static bool LooksLikeContextualFollowUpForExecutionNudge(string userRequest, string assistantDraft) {
        var request = (userRequest ?? string.Empty).Trim();
        if (request.Length == 0 || request.Length > 240) {
            return false;
        }

        if (request.Contains('\n', StringComparison.Ordinal) || request.Contains('\r', StringComparison.Ordinal)) {
            return false;
        }

        // Avoid retrying on obviously structured/payload-like user input.
        if (request.Contains('{', StringComparison.Ordinal)
            || request.Contains('}', StringComparison.Ordinal)
            || request.Contains('[', StringComparison.Ordinal)
            || request.Contains(']', StringComparison.Ordinal)
            || request.Contains('<', StringComparison.Ordinal)
            || request.Contains('>', StringComparison.Ordinal)
            || request.Contains('|', StringComparison.Ordinal)
            || request.Contains('=', StringComparison.Ordinal)) {
            return false;
        }

        var tokenCount = CountLetterDigitTokens(request, maxTokens: 28);
        if (tokenCount < 2 || tokenCount > 24) {
            return false;
        }

        return AssistantDraftHasContextualAnchor(request, assistantDraft);
    }

    private static bool AssistantDraftHasContextualAnchor(string userRequest, string assistantDraft) {
        var request = CollapseWhitespace((userRequest ?? string.Empty).Trim());
        var draft = CollapseWhitespace((assistantDraft ?? string.Empty).Trim());
        if (request.Length == 0 || draft.Length == 0) {
            return false;
        }

        if (request.Length >= 12 && draft.IndexOf(request, StringComparison.OrdinalIgnoreCase) >= 0) {
            return true;
        }

        var requestTokens = ExtractMeaningfulTokensForContext(request, maxTokens: 24);
        if (requestTokens.Count < 2) {
            return false;
        }

        var requestUnique = new HashSet<string>(requestTokens, StringComparer.OrdinalIgnoreCase);
        var draftUnique = new HashSet<string>(ExtractMeaningfulTokensForContext(draft, maxTokens: 48), StringComparer.OrdinalIgnoreCase);
        var sharedCount = 0;
        foreach (var token in requestUnique) {
            if (draftUnique.Contains(token)) {
                sharedCount++;
            }
        }

        if (sharedCount < 2) {
            return false;
        }

        for (var i = 0; i < requestTokens.Count - 1; i++) {
            var first = requestTokens[i];
            var second = requestTokens[i + 1];
            if (first.Length < 3 || second.Length < 3) {
                continue;
            }

            if (first.Length < 4 && second.Length < 4) {
                continue;
            }

            var pair = first + " " + second;
            if (ContainsPhraseWithBoundaries(draft, pair)) {
                return true;
            }
        }

        if (requestUnique.Count >= 6 && sharedCount >= 4) {
            return true;
        }

        return false;
    }

    private static List<string> ExtractMeaningfulTokensForContext(string text, int maxTokens) {
        var value = (text ?? string.Empty).Trim();
        var tokens = new List<string>();
        if (value.Length == 0 || maxTokens <= 0) {
            return tokens;
        }

        var inToken = false;
        var tokenStart = 0;
        for (var i = 0; i <= value.Length; i++) {
            var ch = i < value.Length ? value[i] : '\0';
            var isTokenChar = i < value.Length && char.IsLetterOrDigit(ch);
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

            var token = value.Substring(tokenStart, i - tokenStart);
            inToken = false;
            if (token.Length == 0) {
                continue;
            }

            var hasNonAscii = false;
            for (var t = 0; t < token.Length; t++) {
                if (token[t] > 127) {
                    hasNonAscii = true;
                    break;
                }
            }

            var minLen = hasNonAscii ? 2 : 3;
            if (token.Length < minLen) {
                continue;
            }

            tokens.Add(token);
            if (tokens.Count >= maxTokens) {
                break;
            }
        }

        return tokens;
    }

    private static bool AssistantDraftReferencesUserRequest(string userRequest, string assistantDraft) {
        var request = (userRequest ?? string.Empty).Trim();
        var draft = (assistantDraft ?? string.Empty).Trim();
        if (request.Length == 0 || draft.Length == 0) {
            return false;
        }

        // Direct substring match is the strongest signal.
        if (request.Length >= 3 && draft.IndexOf(request, StringComparison.OrdinalIgnoreCase) >= 0) {
            return true;
        }

        // Fall back to token containment (language-agnostic): if any meaningful user token appears in the draft,
        // it is likely the assistant intended to act on that follow-up but failed to call tools.
        var inToken = false;
        var tokenStart = 0;
        var checkedTokens = 0;
        for (var i = 0; i <= request.Length; i++) {
            var ch = i < request.Length ? request[i] : '\0';
            var isTokenChar = i < request.Length && char.IsLetterOrDigit(ch);
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

            var token = request.Substring(tokenStart, i - tokenStart);
            inToken = false;
            if (token.Length == 0) {
                continue;
            }

            var hasNonAscii = false;
            for (var t = 0; t < token.Length; t++) {
                if (token[t] > 127) {
                    hasNonAscii = true;
                    break;
                }
            }

            var minLen = hasNonAscii ? 2 : 3;
            if (token.Length < minLen) {
                continue;
            }

            checkedTokens++;
            if (draft.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }

            if (checkedTokens >= 12) {
                break;
            }
        }

        return false;
    }

    private static bool LooksLikeExecutionIntentPlaceholderDraft(string userRequest, string assistantDraft) {
        var request = CollapseWhitespace((userRequest ?? string.Empty).Trim());
        var draft = CollapseWhitespace((assistantDraft ?? string.Empty).Trim());
        if (request.Length == 0 || draft.Length < 24 || draft.Length > 560) {
            return false;
        }

        if (draft.Contains('\n', StringComparison.Ordinal) || draft.Contains('\r', StringComparison.Ordinal)) {
            return false;
        }

        if (ContainsQuestionSignal(draft)) {
            return false;
        }

        if (draft.Contains("ix:action:v1", StringComparison.OrdinalIgnoreCase)
            || draft.Contains(ExecutionContractMarker, StringComparison.OrdinalIgnoreCase)
            || draft.Contains(ExecutionWatchdogMarker, StringComparison.OrdinalIgnoreCase)
            || draft.Contains(ExecutionCorrectionMarker, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (draft.Contains('|', StringComparison.Ordinal)
            || draft.Contains('{', StringComparison.Ordinal)
            || draft.Contains('}', StringComparison.Ordinal)
            || draft.Contains('[', StringComparison.Ordinal)
            || draft.Contains(']', StringComparison.Ordinal)
            || draft.Contains('<', StringComparison.Ordinal)
            || draft.Contains('>', StringComparison.Ordinal)
            || draft.Contains('=', StringComparison.Ordinal)) {
            return false;
        }

        var requestTokens = ExtractMeaningfulTokensForContext(request, maxTokens: 24);
        var draftTokens = ExtractMeaningfulTokensForContext(draft, maxTokens: 48);
        if (requestTokens.Count < 4 || draftTokens.Count < 4) {
            return false;
        }

        var requestUnique = new HashSet<string>(requestTokens, StringComparer.OrdinalIgnoreCase);
        var draftUnique = new HashSet<string>(draftTokens, StringComparer.OrdinalIgnoreCase);
        var sharedCount = 0;
        foreach (var token in requestUnique) {
            if (draftUnique.Contains(token)) {
                sharedCount++;
            }
        }

        if (sharedCount < 3) {
            return false;
        }

        var overlapRatio = requestUnique.Count == 0 ? 0d : (double)sharedCount / requestUnique.Count;
        if (overlapRatio < 0.35d) {
            return false;
        }

        // Placeholder drafts are usually request paraphrases without concrete evidence/timestamps.
        // If the draft contains multiple long digit runs, treat it as likely-result-bearing text.
        var longDigitRunCount = 0;
        var currentDigitRun = 0;
        for (var i = 0; i < draft.Length; i++) {
            if (char.IsDigit(draft[i])) {
                currentDigitRun++;
                continue;
            }

            if (currentDigitRun >= 4) {
                longDigitRunCount++;
            }
            currentDigitRun = 0;
        }
        if (currentDigitRun >= 4) {
            longDigitRunCount++;
        }

        return longDigitRunCount == 0;
    }

    private static int CountLetterDigitTokens(string text, int maxTokens) {
        var tokenCount = 0;
        var inToken = false;
        for (var i = 0; i < text.Length; i++) {
            var ch = text[i];
            if (char.IsLetterOrDigit(ch)) {
                if (!inToken) {
                    tokenCount++;
                    if (tokenCount >= maxTokens) {
                        return tokenCount;
                    }
                    inToken = true;
                }
            } else {
                inToken = false;
            }
        }

        return tokenCount;
    }

    private static string BuildToolExecutionNudgePrompt(string userRequest, string assistantDraft) {
        var requestText = string.IsNullOrWhiteSpace(userRequest) ? "(empty)" : userRequest.Trim();
        var draftText = string.IsNullOrWhiteSpace(assistantDraft) ? "(empty)" : assistantDraft.Trim();
        return $$"""
            [Execution correction]
            {{ExecutionCorrectionMarker}}
            The previous assistant draft did not execute tools.

            User request:
            {{requestText}}

            Previous assistant draft:
            {{draftText}}

            Execute available tools now when they can satisfy this request.
            Do not ask for another confirmation unless a required input cannot be inferred or discovered.
            If tools truly cannot satisfy the request, explain the exact blocker and the minimal missing input.
            """;
    }

    private static string BuildNoToolExecutionWatchdogPrompt(string userRequest, string assistantDraft) {
        var requestText = TrimForPrompt(userRequest, ToolReceiptCorrectionMaxUserRequestChars);
        var draftText = TrimForPrompt(assistantDraft, ToolReceiptCorrectionMaxDraftChars);
        return $$"""
            [Execution watchdog]
            {{ExecutionWatchdogMarker}}
            The previous retries still produced zero tool calls in this turn.

            User request:
            {{requestText}}

            Previous assistant draft:
            {{draftText}}

            If tools can satisfy this request, call them now in this turn.
            If tools cannot satisfy this request, do not imply execution. State the exact blocker and the minimal missing input.
            """;
    }

    private static string BuildExecutionContractBlockerText(string userRequest, string assistantDraft, string reason) {
        var requestText = TrimForPrompt(userRequest, 280);
        var reasonCode = string.IsNullOrWhiteSpace(reason) ? "no_tool_calls_after_retries" : reason.Trim();
        var replayActionBlock = BuildExecutionContractReplayActionBlock(userRequest, assistantDraft);
        return $$"""
            [Execution blocked]
            {{ExecutionContractMarker}}
            I do not have confirmed tool output for this selected action yet.

            Selected action request:
            {{requestText}}

            Reason code: {{reasonCode}}

            Please retry this action in this context, or use the action command below.
            {{replayActionBlock}}
            """;
    }

    private static string BuildExecutionContractEscapePrompt(string userRequest, string assistantDraft) {
        var requestText = TrimForPrompt(userRequest, ToolReceiptCorrectionMaxUserRequestChars);
        var draftText = TrimForPrompt(assistantDraft, ToolReceiptCorrectionMaxDraftChars);
        return $$"""
            [Execution contract escape]
            {{ExecutionContractEscapeMarker}}
            This action-selection turn still has zero tool calls.

            Selected action request:
            {{requestText}}

            Previous assistant draft:
            {{draftText}}

            Retry now with full tool availability for this turn.
            Requirements:
            - Call at least one relevant tool in this turn if any registered tool can satisfy the request.
            - If no tool can satisfy the request, do not claim execution. Explain the exact blocker and the minimal missing input.
            - Keep the response concise and execution-focused.
            """;
    }

    private static string BuildContinuationSubsetEscapePrompt(string userRequest, string assistantDraft) {
        var requestText = TrimForPrompt(userRequest, ToolReceiptCorrectionMaxUserRequestChars);
        var draftText = TrimForPrompt(assistantDraft, ToolReceiptCorrectionMaxDraftChars);
        return $$"""
            [Continuation subset escape]
            {{ContinuationSubsetEscapeMarker}}
            This follow-up turn reused a narrowed tool subset and still produced zero tool activity.

            User request:
            {{requestText}}

            Previous assistant draft:
            {{draftText}}

            Retry now with full tool availability for this turn.
            Requirements:
            - Call at least one relevant tool in this turn when any registered tool can satisfy the request.
            - If no tool can satisfy the request, state the exact blocker and the minimal missing input.
            - Keep the response concise and execution-focused.
            """;
    }

    private static string BuildExecutionContractReplayActionBlock(string userRequest, string assistantDraft) {
        if (!TryResolveReplayActionForExecutionContract(
                userRequest,
                assistantDraft,
                out var actionId,
                out var actionTitle,
                out var actionRequest,
                out var mutability)) {
            return string.Empty;
        }

        actionTitle = NormalizeReplayActionText(actionTitle, maxChars: 120);
        actionRequest = NormalizeReplayActionText(actionRequest, maxChars: 220);
        var mutabilityLine = mutability switch {
            ActionMutability.Mutating => "mutating: true",
            ActionMutability.ReadOnly => "mutating: false",
            _ => string.Empty
        };
        var mutabilityBlock = mutabilityLine.Length == 0 ? string.Empty : mutabilityLine + Environment.NewLine;

        return $$"""

            [Action]
            ix:action:v1
            id: {{actionId}}
            title: {{actionTitle}}
            request: {{actionRequest}}
            {{mutabilityBlock}}reply: /act {{actionId}}
            """;
    }

    private static bool TryResolveReplayActionForExecutionContract(
        string userRequest,
        string assistantDraft,
        out string actionId,
        out string actionTitle,
        out string actionRequest,
        out ActionMutability mutability) {
        if (TryParseActionSelectionForReplay(userRequest, out actionId, out actionTitle, out actionRequest, out mutability)) {
            return true;
        }

        return TryParseSinglePendingActionForReplay(assistantDraft, out actionId, out actionTitle, out actionRequest, out mutability);
    }

    private static bool TryParseSinglePendingActionForReplay(
        string assistantDraft,
        out string actionId,
        out string actionTitle,
        out string actionRequest,
        out ActionMutability mutability) {
        actionId = string.Empty;
        actionTitle = string.Empty;
        actionRequest = string.Empty;
        mutability = ActionMutability.Unknown;

        var actions = ExtractPendingActions(assistantDraft);
        if (actions.Count == 0) {
            actions = ExtractFallbackChoicePendingActions(assistantDraft);
        }
        if (actions.Count != 1) {
            return false;
        }

        var action = actions[0];
        mutability = action.Mutability;
        actionId = NormalizeReplayActionIdToken(action.Id);
        if (actionId.Length == 0) {
            return false;
        }

        actionTitle = NormalizeReplayActionText(action.Title, maxChars: 200);
        actionRequest = NormalizeReplayActionText(action.Request, maxChars: 600);
        if (actionRequest.Length == 0) {
            actionRequest = actionTitle;
        }
        if (actionRequest.Length == 0) {
            actionRequest = "Retry selected action.";
        }
        if (actionTitle.Length == 0) {
            actionTitle = actionRequest;
        }

        return true;
    }

    private static bool TryParseActionSelectionForReplay(
        string userRequest,
        out string actionId,
        out string actionTitle,
        out string actionRequest,
        out ActionMutability mutability) {
        actionId = string.Empty;
        actionTitle = string.Empty;
        actionRequest = string.Empty;
        mutability = ActionMutability.Unknown;

        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized.Length > MaxActionSelectionPayloadChars || normalized[0] != '{') {
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(normalized, ActionSelectionJsonOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                return false;
            }

            if (!doc.RootElement.TryGetProperty("ix_action_selection", out var selection) || selection.ValueKind != JsonValueKind.Object) {
                return false;
            }

            if (!selection.TryGetProperty("id", out var id)) {
                return false;
            }

            actionId = NormalizeReplayActionId(id);
            if (actionId.Length == 0) {
                return false;
            }

            actionTitle = NormalizeReplayActionText(TryReadReplayActionSelectionText(selection, "title"), maxChars: 200);
            actionRequest = NormalizeReplayActionText(TryReadReplayActionSelectionText(selection, "request"), maxChars: 600);
            mutability = ResolveActionSelectionMutability(selection);
            if (actionRequest.Length == 0) {
                actionRequest = actionTitle;
            }
            if (actionRequest.Length == 0) {
                actionRequest = "Retry selected action.";
            }
            if (actionTitle.Length == 0) {
                actionTitle = actionRequest;
            }

            return true;
        } catch (JsonException) {
            return false;
        }
    }

    private static string NormalizeReplayActionId(JsonElement id) {
        switch (id.ValueKind) {
            case JsonValueKind.String: {
                    return NormalizeReplayActionIdToken(id.GetString() ?? string.Empty);
                }
            case JsonValueKind.Number:
                if (!id.TryGetInt64(out var numericId) || numericId <= 0) {
                    return string.Empty;
                }

                return numericId.ToString();
            default:
                return string.Empty;
        }
    }

    private static string NormalizeReplayActionIdToken(string idToken) {
        var token = ReadFirstToken((idToken ?? string.Empty).Trim());
        if (token.Length == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder(Math.Min(token.Length, 64));
        for (var i = 0; i < token.Length && sb.Length < 64; i++) {
            var ch = token[i];
            if (char.IsWhiteSpace(ch) || char.IsControl(ch)) {
                continue;
            }
            if (ch == ':' || ch == ';') {
                continue;
            }

            sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    private static string TryReadReplayActionSelectionText(JsonElement selection, string propertyName) {
        if (!selection.TryGetProperty(propertyName, out var value)) {
            return string.Empty;
        }

        if (value.ValueKind == JsonValueKind.String) {
            return value.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string NormalizeReplayActionText(string text, int maxChars) {
        var normalized = CollapseWhitespace(text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        if (normalized.Length > maxChars) {
            normalized = normalized.Substring(0, maxChars);
        }

        return normalized.Trim();
    }
}
