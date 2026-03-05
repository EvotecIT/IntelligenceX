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
    private static int ResolveMaxCandidateToolsLimit(int? requestedLimit, int totalToolCount) {
        var candidate = requestedLimit.GetValueOrDefault(0);
        if (candidate <= 0) {
            candidate = Math.Clamp((int)Math.Ceiling(totalToolCount * 0.45d), 10, 28);
        }

        return Math.Clamp(candidate, 4, Math.Max(4, totalToolCount));
    }

    private static string ExtractPrimaryUserRequest(string requestText) {
        var text = (requestText ?? string.Empty).Trim();
        if (text.Length == 0) {
            return string.Empty;
        }

        if (TryReadContinuationContractFromRequestText(text, out _, out var continuationFollowUp)) {
            return NormalizeRoutingUserText(continuationFollowUp);
        }

        var match = UserRequestSectionRegex.Match(text);
        if (match.Success && match.Groups.Count > 1) {
            var value = match.Groups["value"].Value;
            if (!string.IsNullOrWhiteSpace(value)) {
                return value.Trim();
            }
        }

        return NormalizeRoutingUserText(text);
    }

    private static string ExtractIntentUserText(string requestText) {
        var text = (requestText ?? string.Empty).Trim();
        if (text.Length == 0) {
            return string.Empty;
        }

        if (TryReadContinuationContractFromRequestText(text, out var continuationIntentAnchor, out _)
            && continuationIntentAnchor.Length > 0) {
            return NormalizeIntentUserText(continuationIntentAnchor);
        }

        var match = UserRequestSectionRegex.Match(text);
        if (match.Success && match.Groups.Count > 1) {
            var value = match.Groups["value"].Value;
            if (!string.IsNullOrWhiteSpace(value)) {
                text = value.Trim();
            }
        }

        return NormalizeIntentUserText(text);
    }

    private static string NormalizeRoutingUserText(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        // Strip code fences and inline code so routing focuses on intent, not pasted snippets.
        normalized = StripCodeFences(normalized);
        normalized = StripInlineCode(normalized);
        normalized = CollapseWhitespace(normalized);
        // Never fall back to the original text here: it may contain the very content we intentionally stripped.
        return normalized;
    }

    private static string NormalizeIntentUserText(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        // Keep intent relatively faithful while still removing markdown delimiters.
        var withoutInlineDelimiters = StripInlineCode(normalized);
        var strippedFences = StripCodeFences(withoutInlineDelimiters);
        var collapsed = CollapseWhitespace(strippedFences);
        if (collapsed.Length > 0) {
            return collapsed;
        }

        // If stripping fences wiped out everything (e.g., an all-code message), keep a compact version of the
        // original content but remove fence markers so follow-ups can still anchor on *some* context.
        return CollapseWhitespace(withoutInlineDelimiters.Replace("```", " ", StringComparison.Ordinal));
    }

    private static string StripCodeFences(string text) {
        if (string.IsNullOrWhiteSpace(text) || text.IndexOf("```", StringComparison.Ordinal) < 0) {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        var idx = 0;
        while (idx < text.Length) {
            var start = text.IndexOf("```", idx, StringComparison.Ordinal);
            if (start < 0) {
                sb.Append(text, idx, text.Length - idx);
                break;
            }

            sb.Append(text, idx, start - idx);

            var end = text.IndexOf("```", start + 3, StringComparison.Ordinal);
            if (end < 0) {
                var tail = ExtractUnclosedFenceTail(text, fenceStartIndex: start + 3);
                if (!string.IsNullOrWhiteSpace(tail)) {
                    if (sb.Length > 0 && !char.IsWhiteSpace(sb[^1])) {
                        sb.Append(' ');
                    }
                    sb.Append(tail);
                }
                break;
            }

            idx = end + 3;
        }

        return sb.ToString();
    }

    private static string ExtractUnclosedFenceTail(string text, int fenceStartIndex) {
        if (string.IsNullOrWhiteSpace(text) || fenceStartIndex < 0 || fenceStartIndex >= text.Length) {
            return string.Empty;
        }

        // If a user forgets the closing fence, they often keep typing a short instruction after the code.
        // Try to salvage the last non-empty line if it looks like natural language rather than a command.
        var idx = text.Length;
        while (idx > fenceStartIndex) {
            var lineStart = text.LastIndexOf('\n', idx - 1);
            if (lineStart < fenceStartIndex) {
                lineStart = fenceStartIndex - 1;
            }

            var rawLine = text.Substring(lineStart + 1, idx - (lineStart + 1)).Trim();
            if (rawLine.Length == 0) {
                idx = lineStart;
                continue;
            }

            var candidate = CollapseWhitespace(StripInlineCode(rawLine));
            if (candidate.Length == 0 || candidate.Length > 96) {
                return string.Empty;
            }
            if (LooksLikeCodeTail(candidate)) {
                return string.Empty;
            }
            return candidate;
        }

        return string.Empty;
    }

    private static bool LooksLikeCodeTail(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return false;
        }

        var hasUpper = false;
        for (var i = 0; i < text.Length; i++) {
            var ch = text[i];
            if (ch is '{' or '}' or ';' or '(' or ')' or '[' or ']' or '$' or '=' or '|' or '<' or '>') {
                return true;
            }
            if (ch is '\r' or '\n' or '\t') {
                return true;
            }
            if (char.IsUpper(ch)) {
                hasUpper = true;
            }
        }

        // Common for cmdlets/functions: `Get-Thing` (hyphen + upper). Allow "forest-wide" (no upper).
        return hasUpper && text.Contains('-', StringComparison.Ordinal);
    }

    private static string StripInlineCode(string text) {
        if (string.IsNullOrWhiteSpace(text) || text.IndexOf('`', StringComparison.Ordinal) < 0) {
            return text;
        }

        // Inline code often wraps important tokens (paths/hostnames). Keep the content and drop delimiters.
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++) {
            var ch = text[i];
            if (ch == '`') {
                // Replace with whitespace so we don't accidentally concatenate tokens.
                sb.Append(' ');
                continue;
            }
            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static string CollapseWhitespace(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        var prevSpace = false;
        for (var i = 0; i < text.Length; i++) {
            var ch = text[i];
            if (char.IsWhiteSpace(ch)) {
                if (!prevSpace) {
                    sb.Append(' ');
                    prevSpace = true;
                }
                continue;
            }

            prevSpace = false;
            sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    private static bool LooksLikeStructuredIntentPayload(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (TryParseExplicitActSelection(normalized, out _, out _)
            || TryReadActionSelectionIntent(normalized, out _, out _)
            || TryParseDomainIntentMarkerSelection(normalized, DomainIntentMarker, out _)
            || TryParseDomainIntentChoiceMarkerSelection(normalized, out _)
            || TryParseDomainIntentFamilyFromActionSelectionPayload(normalized, out _)
            || TryParseDomainIntentFamilyFromDomainScopePayload(normalized, out _)) {
            return true;
        }

        if (TryExtractActionSelectionPayloadJson(normalized, out var payload)
            && (TryParseDomainIntentFamilyFromActionSelectionPayload(payload, out _)
                || TryParseDomainIntentFamilyFromDomainScopePayload(payload, out _))) {
            return true;
        }

        return false;
    }

    private static bool TryReadContinuationContractFromRequestText(string? requestText, out string intentAnchor, out string followUp) {
        intentAnchor = string.Empty;
        followUp = string.Empty;
        var text = requestText ?? string.Empty;
        if (text.Length == 0) {
            return false;
        }

        var scanLength = Math.Min(MaxContinuationContractScanChars, text.Length);
        if (scanLength <= 0) {
            return false;
        }

        var scan = text.AsSpan(0, scanLength);
        var markerSeen = false;
        var enabled = false;
        var enabledSeen = false;
        while (!scan.IsEmpty) {
            var lineBreakIndex = scan.IndexOfAny('\r', '\n');
            ReadOnlySpan<char> line;
            if (lineBreakIndex < 0) {
                line = scan;
                scan = ReadOnlySpan<char>.Empty;
            } else {
                line = scan.Slice(0, lineBreakIndex);
                var nextIndex = lineBreakIndex + 1;
                if (nextIndex < scan.Length && scan[lineBreakIndex] == '\r' && scan[nextIndex] == '\n') {
                    nextIndex++;
                }

                scan = scan.Slice(nextIndex);
            }

            var trimmed = line.Trim();
            if (trimmed.IsEmpty) {
                continue;
            }

            var normalizedLine = NormalizeContinuationContractLine(trimmed);
            if (normalizedLine.IsEmpty) {
                continue;
            }

            if (!markerSeen) {
                // Allow structural wrappers (for example quote/code-fence/list prefixes) before
                // the marker, but fail closed when any non-wrapper content appears first.
                if (normalizedLine.Equals(ContinuationContractMarker, StringComparison.OrdinalIgnoreCase)) {
                    markerSeen = true;
                    continue;
                }

                if (IsContinuationContractWrapperLine(trimmed)) {
                    continue;
                }

                return false;
            }

            if (normalizedLine.StartsWith("ix:", StringComparison.OrdinalIgnoreCase)
                && normalizedLine.IndexOf(ContinuationContractMarker, StringComparison.OrdinalIgnoreCase) < 0) {
                break;
            }

            if (TryParseBooleanStructuredField(normalizedLine, "enabled", out var parsedEnabled)) {
                enabled = parsedEnabled;
                enabledSeen = true;
                continue;
            }

            if (TryParseStringStructuredField(normalizedLine, "intent_anchor", out var parsedIntentAnchor)) {
                intentAnchor = CollapseWhitespace(parsedIntentAnchor);
                continue;
            }

            if (TryParseStringStructuredField(normalizedLine, "follow_up", out var parsedFollowUp)) {
                followUp = CollapseWhitespace(parsedFollowUp);
            }
        }

        if (!enabledSeen || !enabled || followUp.Length == 0) {
            intentAnchor = string.Empty;
            followUp = string.Empty;
            return false;
        }

        if (intentAnchor.Length > MaxContinuationContractFieldChars) {
            intentAnchor = intentAnchor.Substring(0, MaxContinuationContractFieldChars);
        }
        if (followUp.Length > MaxContinuationContractFieldChars) {
            followUp = followUp.Substring(0, MaxContinuationContractFieldChars);
        }

        return true;
    }

    private static ReadOnlySpan<char> NormalizeContinuationContractLine(ReadOnlySpan<char> line) {
        var normalized = line.Trim();
        var previousLength = -1;
        while (!normalized.IsEmpty && normalized.Length != previousLength) {
            previousLength = normalized.Length;

            if (normalized[0] == '>') {
                normalized = normalized.Slice(1).TrimStart();
                continue;
            }

            if (normalized.Length >= 2
                && (normalized[0] == '-' || normalized[0] == '*' || normalized[0] == '+')
                && char.IsWhiteSpace(normalized[1])) {
                normalized = normalized.Slice(1).TrimStart();
                continue;
            }
        }

        return normalized;
    }

    private static bool IsContinuationContractWrapperLine(ReadOnlySpan<char> line) {
        var normalized = NormalizeContinuationContractLine(line);
        if (normalized.IsEmpty) {
            return true;
        }

        if (normalized.StartsWith("```", StringComparison.Ordinal)
            || normalized.StartsWith("~~~", StringComparison.Ordinal)) {
            return true;
        }

        return false;
    }

    private static bool TryParseBooleanStructuredField(ReadOnlySpan<char> line, string key, out bool value) {
        value = false;
        if (!TryParseStringStructuredField(line, key, out var parsedValue)) {
            return false;
        }

        if (parsedValue.Equals("true", StringComparison.OrdinalIgnoreCase)) {
            value = true;
            return true;
        }

        if (parsedValue.Equals("false", StringComparison.OrdinalIgnoreCase)) {
            value = false;
            return true;
        }

        return false;
    }

    private static bool TryParseStringStructuredField(ReadOnlySpan<char> line, string key, out string value) {
        value = string.Empty;
        var trimmed = line.Trim();
        if (!trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var remainder = trimmed.Slice(key.Length).TrimStart();
        if (remainder.IsEmpty || (remainder[0] != ':' && remainder[0] != '\uFF1A')) {
            return false;
        }

        var rawValue = remainder.Slice(1).Trim();
        if (rawValue.Length >= 2) {
            var startsWithDoubleQuote = rawValue[0] == '"';
            var startsWithSingleQuote = rawValue[0] == '\'';
            if ((startsWithDoubleQuote && rawValue[^1] == '"') || (startsWithSingleQuote && rawValue[^1] == '\'')) {
                rawValue = rawValue.Slice(1, rawValue.Length - 2).Trim();
            }
        }

        value = rawValue.ToString();
        return true;
    }

    private static string BuildContinuationContractEnvelope(string routedUserRequest, string followUpRequest) {
        var routed = (routedUserRequest ?? string.Empty).Trim();
        if (routed.Length == 0) {
            return string.Empty;
        }

        var followUp = CollapseWhitespace((followUpRequest ?? string.Empty).Trim());
        if (followUp.Length == 0) {
            return string.Empty;
        }

        var intentAnchor = routed;
        var followUpLabelIndex = routed.LastIndexOf("\nFollow-up:", StringComparison.OrdinalIgnoreCase);
        if (followUpLabelIndex > 0) {
            intentAnchor = routed[..followUpLabelIndex].Trim();
        }
        intentAnchor = CollapseWhitespace(intentAnchor);
        if (intentAnchor.Length == 0) {
            intentAnchor = routed;
        }
        if (intentAnchor.Length > MaxContinuationContractFieldChars) {
            intentAnchor = intentAnchor.Substring(0, MaxContinuationContractFieldChars);
        }
        if (followUp.Length > MaxContinuationContractFieldChars) {
            followUp = followUp.Substring(0, MaxContinuationContractFieldChars);
        }

        return
            routed
            + "\n\n"
            + ContinuationContractMarker
            + "\n"
            + "enabled: true\n"
            + "intent_anchor: "
            + intentAnchor
            + "\n"
            + "follow_up: "
            + followUp;
    }

}
