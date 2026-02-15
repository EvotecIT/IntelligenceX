using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace IntelligenceX.Chat.App;

internal sealed record AssistantPendingAction(string Id, string Title, string Request, string Reply);

internal static class ActionModelProtocol {
    private const string ActionHeader = "[Action]";
    private const string ActionMarker = "ix:action:v1";

    public static bool TryStripAndExtractPendingActions(
        string? assistantText,
        out IReadOnlyList<AssistantPendingAction> actions,
        out string cleanedText) {
        actions = Array.Empty<AssistantPendingAction>();
        var input = (assistantText ?? string.Empty).Trim();
        cleanedText = input;
        if (input.Length == 0) {
            return false;
        }

        var normalized = input.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        if (lines.Length == 0) {
            return false;
        }

        var removed = new bool[lines.Length];
        var extracted = new List<AssistantPendingAction>();
        var markerSeen = false;

        for (var i = 0; i < lines.Length; i++) {
            var line = (lines[i] ?? string.Empty).Trim();
            var isHeader = string.Equals(line, ActionHeader, StringComparison.OrdinalIgnoreCase);
            var isMarker = string.Equals(line, ActionMarker, StringComparison.OrdinalIgnoreCase);
            if (!isHeader && !isMarker) {
                continue;
            }

            var markerIndex = i;
            if (isHeader) {
                markerIndex = i + 1;
                while (markerIndex < lines.Length && string.IsNullOrWhiteSpace(lines[markerIndex])) {
                    markerIndex++;
                }
                if (markerIndex >= lines.Length
                    || !string.Equals((lines[markerIndex] ?? string.Empty).Trim(), ActionMarker, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
            }

            markerSeen = true;
            var id = string.Empty;
            var title = string.Empty;
            var reply = string.Empty;
            var requestBuilder = new StringBuilder();
            var sawRequest = false;
            var endExclusive = markerIndex + 1;

            for (var j = markerIndex + 1; j < lines.Length; j++) {
                var current = lines[j] ?? string.Empty;
                var trimmedCurrent = current.Trim();
                if (trimmedCurrent.Length == 0) {
                    endExclusive = j + 1;
                    break;
                }
                if (string.Equals(trimmedCurrent, ActionHeader, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(trimmedCurrent, ActionMarker, StringComparison.OrdinalIgnoreCase)
                    || trimmedCurrent.StartsWith("ix:", StringComparison.OrdinalIgnoreCase)) {
                    endExclusive = j;
                    break;
                }

                if (TryReadField(trimmedCurrent, "id", out var field)) {
                    id = field;
                    continue;
                }
                if (TryReadField(trimmedCurrent, "title", out field)) {
                    title = field;
                    continue;
                }
                if (TryReadField(trimmedCurrent, "request", out field)) {
                    sawRequest = true;
                    if (!string.IsNullOrWhiteSpace(field)) {
                        if (requestBuilder.Length > 0) {
                            requestBuilder.Append(' ');
                        }
                        requestBuilder.Append(field.Trim());
                    }
                    continue;
                }
                if (TryReadField(trimmedCurrent, "reply", out field)) {
                    reply = field;
                    continue;
                }

                if (sawRequest) {
                    if (requestBuilder.Length > 0) {
                        requestBuilder.Append(' ');
                    }
                    requestBuilder.Append(trimmedCurrent);
                }

                endExclusive = j + 1;
            }

            var start = isHeader ? i : markerIndex;
            for (var j = start; j < Math.Min(lines.Length, endExclusive); j++) {
                removed[j] = true;
            }

            id = id.Trim();
            title = title.Trim();
            reply = reply.Trim();
            var request = requestBuilder.ToString().Trim();
            if (id.Length > 0 && LooksLikeValidActionReply(reply, id)) {
                extracted.Add(new AssistantPendingAction(
                    Id: id,
                    Title: title,
                    Request: request,
                    Reply: reply));
            }

            i = Math.Max(i, endExclusive - 1);
        }

        if (!markerSeen) {
            return false;
        }

        var kept = new List<string>(lines.Length);
        for (var i = 0; i < lines.Length; i++) {
            if (removed[i]) {
                continue;
            }

            var trimmed = (lines[i] ?? string.Empty).Trim();
            if (string.Equals(trimmed, ActionHeader, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, ActionMarker, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            kept.Add(lines[i]);
        }

        cleanedText = Regex.Replace(string.Join('\n', kept).Trim(), @"\n{3,}", "\n\n");

        actions = extracted
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
        return true;
    }

    public static string MergeVisibleTextWithPendingActions(string cleanedText, IReadOnlyList<AssistantPendingAction> actions) {
        var text = (cleanedText ?? string.Empty).Trim();
        if (actions is null || actions.Count == 0) {
            return text;
        }

        var summary = BuildPendingActionSummary(actions);
        if (summary.Length == 0) {
            return text;
        }

        return text.Length == 0 ? summary : text + "\n\n" + summary;
    }

    private static string BuildPendingActionSummary(IReadOnlyList<AssistantPendingAction> actions) {
        if (actions.Count == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("You can run one of these follow-up actions:");
        for (var i = 0; i < actions.Count; i++) {
            var action = actions[i];
            var label = (action.Title ?? string.Empty).Trim();
            if (label.Length == 0) {
                label = (action.Request ?? string.Empty).Trim();
            }
            if (label.Length == 0) {
                label = action.Id.Trim();
            }
            if (label.Length > 180) {
                label = label[..180].TrimEnd();
            }

            sb.Append(i + 1)
                .Append(". ")
                .Append(label)
                .Append(" (`/act ")
                .Append(action.Id.Trim())
                .AppendLine("`)");
        }
        return sb.ToString().TrimEnd();
    }

    private static bool TryReadField(string line, string name, out string value) {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(line)) {
            return false;
        }

        var idx = line.IndexOf(':', StringComparison.Ordinal);
        if (idx < 0) {
            return false;
        }

        var key = line[..idx].Trim();
        if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        value = line[(idx + 1)..].Trim();
        return true;
    }

    private static bool LooksLikeValidActionReply(string reply, string id) {
        var normalizedReply = (reply ?? string.Empty).Trim();
        var normalizedId = (id ?? string.Empty).Trim();
        if (normalizedReply.Length == 0 || normalizedId.Length == 0) {
            return false;
        }

        if (!normalizedReply.StartsWith("/act", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var rest = normalizedReply[4..].Trim();
        var token = ReadFirstToken(rest);
        if (token.Length == 0 || !string.Equals(token, normalizedId, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var trailing = rest[token.Length..].Trim();
        return trailing.Length == 0;
    }

    private static string ReadFirstToken(string text) {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0) {
            return string.Empty;
        }

        var i = 0;
        while (i < value.Length && !char.IsWhiteSpace(value[i])) {
            i++;
        }

        return value[..i];
    }
}
