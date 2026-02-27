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

        var match = UserRequestSectionRegex.Match(text);
        if (match.Success && match.Groups.Count > 1) {
            var value = match.Groups["value"].Value;
            if (!string.IsNullOrWhiteSpace(value)) {
                text = value.Trim();
            }
        }

        // Keep intent relatively faithful while still removing markdown delimiters.
        var withoutInlineDelimiters = StripInlineCode(text);
        var strippedFences = StripCodeFences(withoutInlineDelimiters);
        var collapsed = CollapseWhitespace(strippedFences);
        if (collapsed.Length > 0) {
            return collapsed;
        }

        // If stripping fences wiped out everything (e.g., an all-code message), keep a compact version of the
        // original content but remove fence markers so follow-ups can still anchor on *some* context.
        return CollapseWhitespace(withoutInlineDelimiters.Replace("```", " ", StringComparison.Ordinal));
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

}
