using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private static List<PendingAction> ExtractPendingActions(string assistantText) {
        var text = assistantText ?? string.Empty;
        if (text.Length == 0) {
            return new List<PendingAction>();
        }

        // Cheap precheck to avoid parsing when neither the standard marker nor a loose action shape is present.
        if (text.IndexOf(ActionMarker, StringComparison.OrdinalIgnoreCase) < 0
            && !LooksLikeLooseActionBlockCandidate(text)) {
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
            bool? mutating = null;
            bool? readOnly = null;
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
                if (TryReadField(current, "mutating", out field)) {
                    if (TryParseProtocolBoolean(field, out var parsedMutating)) {
                        mutating = parsedMutating;
                    }
                    continue;
                }
                if (TryReadField(current, "readonly", out field)) {
                    if (TryParseProtocolBoolean(field, out var parsedReadOnly)) {
                        readOnly = parsedReadOnly;
                    }
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

            actions.Add(new PendingAction(
                Id: id,
                Title: title,
                Request: req,
                Mutability: ResolveActionMutability(mutating, readOnly)));
            if (actions.Count >= 6) {
                break;
            }
        }

        // De-dupe ids.
        if (actions.Count > 1) {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            actions = actions.Where(a => a.Id.Length > 0 && seen.Add(a.Id)).ToList();
        }

        if (actions.Count == 0 && text.IndexOf(ActionMarker, StringComparison.OrdinalIgnoreCase) < 0) {
            actions = ExtractLoosePendingActions(text);
        }

        return actions;
    }

    private static bool LooksLikeLooseActionBlockCandidate(string text) {
        var value = text ?? string.Empty;
        if (value.Length == 0) {
            return false;
        }

        if (value.IndexOf("/act", StringComparison.OrdinalIgnoreCase) < 0) {
            return false;
        }

        return value.IndexOf("id", StringComparison.OrdinalIgnoreCase) >= 0
               && value.IndexOf("request", StringComparison.OrdinalIgnoreCase) >= 0
               && value.IndexOf("reply", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static List<PendingAction> ExtractLoosePendingActions(string assistantText) {
        var text = assistantText ?? string.Empty;
        if (text.Length == 0) {
            return new List<PendingAction>();
        }

        var actions = new List<PendingAction>();
        var matches = LooseActionBlockRegex.Matches(text);
        for (var i = 0; i < matches.Count && actions.Count < 6; i++) {
            var match = matches[i];
            if (!match.Success) {
                continue;
            }
            if (IsInsideCodeFence(text, match.Index)) {
                continue;
            }

            var id = (match.Groups["id"].Value ?? string.Empty).Trim();
            var title = (match.Groups["title"].Value ?? string.Empty).Trim();
            var request = CollapseWhitespace((match.Groups["request"].Value ?? string.Empty).Trim());
            var reply = (match.Groups["reply"].Value ?? string.Empty).Trim();

            if (id.Length == 0) {
                continue;
            }
            if (!LooksLikeValidActionReply(reply, id)) {
                continue;
            }
            if (id.Length > 64) {
                id = id[..64];
            }
            if (title.Length > 200) {
                title = title[..200];
            }
            if (request.Length > 600) {
                request = request[..600];
            }

            actions.Add(new PendingAction(
                Id: id,
                Title: title,
                Request: request,
                Mutability: ActionMutability.Unknown));
        }

        if (actions.Count > 1) {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            actions = actions.Where(a => a.Id.Length > 0 && seen.Add(a.Id)).ToList();
        }

        return actions;
    }

    private static bool IsInsideCodeFence(string text, int index) {
        var value = text ?? string.Empty;
        if (value.Length == 0 || index <= 0) {
            return false;
        }

        var prefixLength = Math.Min(index, value.Length);
        var lines = SplitLines(value[..prefixLength]);
        var inFence = false;
        for (var i = 0; i < lines.Count; i++) {
            var trimmed = (lines[i] ?? string.Empty).Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal)) {
                inFence = !inFence;
            }
        }

        return inFence;
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
        // If a single bullet carries an explicit inline action id (/act <id>),
        // it's also safe to route as an actionable follow-up.
        return choices.Count == 1
               && (choices[0].IsNumbered || !string.IsNullOrWhiteSpace(choices[0].ActionId));
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
                Request: title,
                Mutability: ActionMutability.ReadOnly));
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


}
