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

        // Strip inline-code wrappers (`token`) without trying to parse markdown fully.
        if (normalized.Length >= 2 && normalized[0] == '`' && normalized[^1] == '`') {
            normalized = normalized.Substring(1, normalized.Length - 2).Trim();
        }

        // Trim light punctuation wrappers so "token?" and "\"token\"" normalize.
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

    private static bool LooksLikeFollowUpShape(string userRequest, int maxQuestionChars) {
        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (normalized.Contains('\n', StringComparison.Ordinal)) {
            return false;
        }

        if (normalized.Length > maxQuestionChars) {
            return false;
        }

        var tokenCount = CountLetterDigitTokens(normalized, maxTokens: FollowUpShapeTokenScanLimit);
        if (tokenCount == 0) {
            return false;
        }

        if (tokenCount <= FollowUpShapeShortTokenLimit
            && normalized.Length <= FollowUpShapeShortCharLimit
            && IsCompactContinuationShapeCandidate(normalized, tokenCount)) {
            return true;
        }

        return tokenCount <= FollowUpQuestionMaxTokens
               && normalized.Length <= maxQuestionChars
               && ContainsQuestionSignal(normalized);
    }

    private static bool IsCompactContinuationShapeCandidate(string normalized, int tokenCount) {
        var value = (normalized ?? string.Empty).Trim();
        if (value.Length == 0 || tokenCount <= 0) {
            return false;
        }

        if (TryParseOrdinalSelection(value, out var ordinal) && ordinal > 0) {
            return true;
        }

        var compact = NormalizeCompactToken(value.AsSpan());
        if (compact.Length == 0) {
            return false;
        }

        var containsNonAscii = false;
        for (var i = 0; i < compact.Length; i++) {
            if (compact[i] > 127) {
                containsNonAscii = true;
                break;
            }
        }

        var minCompactLength = tokenCount == 1
            ? (containsNonAscii ? 2 : 7)
            : (containsNonAscii ? 2 : 4);
        return compact.Length >= minCompactLength;
    }

    private static bool LooksLikeCompactFollowUp(string userRequest) {
        return LooksLikeFollowUpShape(userRequest, CompactFollowUpQuestionCharLimit);
    }

    private bool ShouldTreatFollowUpAsLiveExecutionRequest(string threadId, string userRequest) {
        var normalized = NormalizeCompactText(userRequest);
        if (normalized.Length == 0 || normalized.Length > 180) {
            return false;
        }

        if (TryReadContinuationContractFromRequestText(normalized, out _, out _)
            || LooksLikeActionSelectionPayload(normalized)
            || TryParseExplicitActSelection(normalized, out _, out _)
            || TryReadActionSelectionIntent(normalized, out _, out _)
            || LooksLikeExplicitToolQuestionTurn(normalized)) {
            return false;
        }

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var explicitToolReference = ExtractExplicitRequestedToolNames(normalized).Length > 0;
        var requestedArtifactIntent = ResolveRequestedArtifactIntent(normalized);
        var followUpShape = explicitToolReference
                            || LooksLikeFollowUpShape(normalized, Math.Max(ContinuationFollowUpQuestionCharLimit, 128))
                            || ContainsQuestionSignal(normalized);
        if (!followUpShape) {
            return false;
        }

        if (ShouldTreatAsPassiveCompactFollowUp(normalizedThreadId, normalized)) {
            return false;
        }

        var hasFreshEvidence = HasFreshThreadToolEvidence(normalizedThreadId);
        var hasPendingActions = HasFreshPendingActionsContext(normalizedThreadId);
        var hasCheckpoint = TryGetWorkingMemoryCheckpoint(normalizedThreadId, out var checkpoint);
        if (!hasFreshEvidence && !hasPendingActions && !hasCheckpoint) {
            return false;
        }

        // Compact artifact-only follow-ups like "show it as a topology graph" should
        // usually reuse already-visible evidence instead of forcing a fresh execution lane.
        if (!explicitToolReference
            && requestedArtifactIntent.RequiresArtifact
            && !ContainsQuestionSignal(normalized)
            && (hasFreshEvidence || hasCheckpoint)) {
            return false;
        }

        if (hasPendingActions) {
            return true;
        }

        if (!hasCheckpoint) {
            return hasFreshEvidence
                   && ContainsQuestionSignal(normalized)
                   && LooksLikeContinuationFollowUp(normalized);
        }

        if (hasCheckpoint) {
            var anchoredFollowUp = LooksLikeWorkingMemoryAnchoredContinuationFollowUp(normalized, checkpoint);
            if (explicitToolReference) {
                return true;
            }

            if (checkpoint.PriorAnswerPlanPreferCachedEvidenceReuse
                && checkpoint.PriorAnswerPlanAllowCachedEvidenceReuse
                && checkpoint.PriorAnswerPlanUnresolvedNow.Length == 0
                && anchoredFollowUp
                && !ContainsQuestionSignal(normalized)) {
                return false;
            }

            if (checkpoint.PriorAnswerPlanRequiresLiveExecution
                || checkpoint.PriorAnswerPlanMissingLiveEvidence.Length > 0
                || checkpoint.PriorAnswerPlanPreferredPackIds.Length > 0
                || checkpoint.PriorAnswerPlanPreferredToolNames.Length > 0) {
                return anchoredFollowUp;
            }

            if (anchoredFollowUp) {
                return true;
            }

            if (anchoredFollowUp) {
                return true;
            }
        }

        return hasFreshEvidence
               && ContainsQuestionSignal(normalized)
               && LooksLikeContinuationFollowUp(normalized);
    }

    private static bool LooksLikeContextualFollowUpForExecutionNudge(string userRequest, string assistantDraft) {
        // Continuation expansion may include a newline-delimited context block where the final line
        // contains a localized label/value tail (for example: "<prior intent>\n<label>: <compact reply>").
        // Normalize it so contextual-anchor checks remain effective and language-neutral.
        var request = NormalizeContextualFollowUpRequest(userRequest);
        if (request.Length == 0 || request.Length > 480) {
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

        var tokenCount = CountLetterDigitTokens(request, maxTokens: 36);
        if (tokenCount < 2 || tokenCount > 32) {
            return false;
        }

        return AssistantDraftHasContextualAnchor(request, assistantDraft);
    }

    private static string NormalizeContextualFollowUpRequest(string userRequest) {
        var raw = (userRequest ?? string.Empty).Trim();
        if (raw.Length == 0) {
            return string.Empty;
        }

        if (TryExtractContextualFollowUpTail(raw, out var followUp)) {
            return CollapseWhitespace(followUp);
        }

        return CollapseWhitespace(raw);
    }

    private static bool TryExtractContextualFollowUpTail(string rawRequest, out string followUp) {
        followUp = string.Empty;
        var raw = (rawRequest ?? string.Empty).Trim();
        if (raw.Length == 0 || raw.IndexOf('\n') < 0) {
            return false;
        }

        var lines = raw.Split('\n');
        var lastLineIndex = lines.Length - 1;
        while (lastLineIndex >= 0 && string.IsNullOrWhiteSpace(lines[lastLineIndex])) {
            lastLineIndex--;
        }

        if (lastLineIndex <= 0) {
            return false;
        }

        var hasContextLine = false;
        var hasParagraphSeparator = false;
        for (var i = 0; i < lastLineIndex; i++) {
            if (string.IsNullOrWhiteSpace(lines[i])) {
                hasParagraphSeparator = true;
                continue;
            }

            hasContextLine = true;
        }

        if (!hasContextLine) {
            return false;
        }

        var tailLine = lines[lastLineIndex].Trim();
        if (tailLine.Length == 0) {
            return false;
        }

        string candidate;
        if (TryExtractLabeledContinuationTailValue(tailLine, out var labeledValue)) {
            candidate = labeledValue;
        } else {
            // Without a label/value split, only accept explicit paragraph-style tails
            // so ordinary multi-line prompts are not rewritten aggressively.
            if (!hasParagraphSeparator) {
                return false;
            }

            candidate = tailLine;
        }

        candidate = CollapseWhitespace(candidate);
        if (!LooksLikeFollowUpShape(candidate, ContinuationFollowUpQuestionCharLimit)) {
            return false;
        }

        followUp = candidate;
        return true;
    }

    private static bool TryExtractLabeledContinuationTailValue(string tailLine, out string value) {
        value = string.Empty;
        var line = (tailLine ?? string.Empty).Trim();
        if (line.Length == 0) {
            return false;
        }

        var separatorIndex = line.IndexOfAny(CallToActionColonPunctuation);
        if (separatorIndex <= 0 || separatorIndex >= line.Length - 1) {
            return false;
        }

        var label = line[..separatorIndex].Trim();
        var candidate = line[(separatorIndex + 1)..].Trim();
        if (label.Length == 0 || candidate.Length == 0) {
            return false;
        }

        if (CountLetterDigitTokens(label, maxTokens: 4) == 0) {
            return false;
        }

        value = candidate;
        return true;
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

}
