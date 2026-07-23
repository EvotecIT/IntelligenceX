using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using IntelligenceX.Chat.App.Markdown;

namespace IntelligenceX.Chat.App.Rendering;

/// <summary>
/// Shared semantic presentation contract for transcript outcome markers such as <c>[warning]</c> and <c>[error]</c>.
/// </summary>
internal sealed record TranscriptOutcomePresentation(
    string Kind,
    string Headline,
    string DetailMarkdown,
    string Badge,
    TranscriptOutcomeTone Tone);

/// <summary>
/// Product-neutral tone used by both HTML and native transcript renderers.
/// </summary>
internal enum TranscriptOutcomeTone {
    Neutral,
    Information,
    Warning,
    Error
}

/// <summary>
/// Owns the legacy outcome-marker interpretation once for every transcript surface.
/// </summary>
internal static class TranscriptOutcomePresentationParser {
    private const string ExecutionContractMarker = "ix:execution-contract:v1";
    private static readonly Regex OutcomePrefixRegex = new(
        @"^\[(?<kind>[a-zA-Z0-9 _-]+)\](?:[ \t]+(?<headline>[^\r\n]*))?",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryParse(string role, string? text, out TranscriptOutcomePresentation presentation) {
        presentation = null!;
        if (!IsOutcomeRole(role)) {
            return false;
        }

        var raw = (text ?? string.Empty).Trim();
        if (raw.Length == 0) {
            return false;
        }

        var match = OutcomePrefixRegex.Match(raw);
        if (!match.Success) {
            return false;
        }

        var kind = NormalizeKind(match.Groups["kind"].Value);
        if (!IsSupportedKind(kind)) {
            return false;
        }

        var headline = match.Groups["headline"].Value.Trim();
        if (headline.Length == 0) {
            headline = GetDefaultTitle(kind, role);
        }

        var rawDetail = raw[match.Length..];
        var detail = kind.Equals("execution_blocked", StringComparison.OrdinalIgnoreCase)
            ? PrepareExecutionBlockedDetail(rawDetail)
            : TranscriptMarkdownPreparation.PrepareOutcomeDetailBody(rawDetail);
        presentation = new TranscriptOutcomePresentation(
            kind,
            headline,
            detail,
            GetBadge(kind),
            GetTone(kind));
        return true;
    }

    private static string NormalizeKind(string kind) =>
        (kind ?? string.Empty)
        .Trim()
        .Replace("-", "_", StringComparison.Ordinal)
        .Replace(" ", "_", StringComparison.Ordinal)
        .ToLowerInvariant();

    private static bool IsSupportedKind(string kind) =>
        kind.Equals("error", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("canceled", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("limit", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("warning", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("startup", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("execution_blocked", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("cached_evidence_fallback", StringComparison.OrdinalIgnoreCase);

    private static bool IsOutcomeRole(string role) =>
        string.Equals(role, "Assistant", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "System", StringComparison.OrdinalIgnoreCase);

    private static string GetDefaultTitle(string kind, string role) {
        var isSystemRole = string.Equals(role, "System", StringComparison.OrdinalIgnoreCase);
        if (kind.Equals("canceled", StringComparison.OrdinalIgnoreCase)) {
            return isSystemRole ? "System canceled" : "Turn canceled";
        }
        if (kind.Equals("limit", StringComparison.OrdinalIgnoreCase)) {
            return isSystemRole ? "System limit reached" : "Limit reached";
        }
        if (kind.Equals("warning", StringComparison.OrdinalIgnoreCase)) {
            return isSystemRole ? "System warning" : "Warning";
        }
        if (kind.Equals("startup", StringComparison.OrdinalIgnoreCase)) {
            return isSystemRole ? "Startup diagnostics" : "Startup";
        }
        if (kind.Equals("execution_blocked", StringComparison.OrdinalIgnoreCase)) {
            return isSystemRole ? "System action blocked" : "Execution blocked";
        }
        if (kind.Equals("cached_evidence_fallback", StringComparison.OrdinalIgnoreCase)) {
            return "Cached evidence fallback";
        }

        return isSystemRole ? "System error" : "Request failed";
    }

    private static string GetBadge(string kind) {
        if (kind.Equals("canceled", StringComparison.OrdinalIgnoreCase)) return "Canceled";
        if (kind.Equals("limit", StringComparison.OrdinalIgnoreCase)) return "Limit";
        if (kind.Equals("warning", StringComparison.OrdinalIgnoreCase)) return "Warning";
        if (kind.Equals("startup", StringComparison.OrdinalIgnoreCase)) return "Startup";
        if (kind.Equals("execution_blocked", StringComparison.OrdinalIgnoreCase)) return "Blocked";
        if (kind.Equals("cached_evidence_fallback", StringComparison.OrdinalIgnoreCase)) return "Cached";
        return "Error";
    }

    private static TranscriptOutcomeTone GetTone(string kind) {
        if (kind.Equals("limit", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("warning", StringComparison.OrdinalIgnoreCase)) {
            return TranscriptOutcomeTone.Warning;
        }

        if (kind.Equals("error", StringComparison.OrdinalIgnoreCase)) {
            return TranscriptOutcomeTone.Error;
        }

        if (kind.Equals("startup", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("cached_evidence_fallback", StringComparison.OrdinalIgnoreCase)) {
            return TranscriptOutcomeTone.Information;
        }

        return TranscriptOutcomeTone.Neutral;
    }

    private static string PrepareExecutionBlockedDetail(string rawDetail) {
        var lines = (rawDetail ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        string summary = string.Empty;
        string selectedAction = string.Empty;
        string actionCommand = string.Empty;
        string reasonCode = string.Empty;
        var consumedLines = new bool[lines.Length];

        for (var i = 0; i < lines.Length; i++) {
            var line = SanitizeDetailLine(lines[i]).Trim();
            if (line.Length == 0) continue;
            if (summary.Length == 0) {
                summary = line;
                consumedLines[i] = true;
                continue;
            }
            if (line.StartsWith("Selected action request:", StringComparison.OrdinalIgnoreCase)) {
                selectedAction = line["Selected action request:".Length..].Trim();
                consumedLines[i] = true;
                continue;
            }
            if (line.StartsWith("Action:", StringComparison.OrdinalIgnoreCase)) {
                actionCommand = line["Action:".Length..].Trim();
                consumedLines[i] = true;
                continue;
            }
            if (!line.StartsWith("Reason code:", StringComparison.OrdinalIgnoreCase)) continue;
            consumedLines[i] = true;
            reasonCode = line["Reason code:".Length..].Trim();
            if (reasonCode.Length > 0) continue;
            for (var j = i + 1; j < lines.Length; j++) {
                var next = SanitizeDetailLine(lines[j]).Trim();
                if (next.Length == 0) {
                    consumedLines[j] = true;
                    continue;
                }
                reasonCode = next;
                consumedLines[j] = true;
                i = j;
                break;
            }
        }

        var detail = new StringBuilder();
        if (summary.Length > 0) detail.Append(summary);
        if (selectedAction.Length > 0 || actionCommand.Length > 0 || reasonCode.Length > 0) {
            if (detail.Length > 0) detail.AppendLine().AppendLine();
            AppendMetadataLine(detail, "Selected action", selectedAction);
            AppendMetadataLine(detail, "Action command", actionCommand);
            AppendMetadataLine(detail, "Reason code", reasonCode);
        }

        var trailingNotes = BuildTrailingNotes(lines, consumedLines);
        if (trailingNotes.Length > 0) {
            if (detail.Length > 0) detail.AppendLine().AppendLine();
            detail.Append(trailingNotes);
        }

        return TranscriptMarkdownPreparation.PrepareOutcomeDetailBody(detail.ToString());
    }

    private static string BuildTrailingNotes(IReadOnlyList<string> lines, IReadOnlyList<bool> consumedLines) {
        var trailingLines = new List<string>(lines.Count);
        for (var i = 0; i < lines.Count; i++) {
            if (!consumedLines[i]) trailingLines.Add(SanitizeDetailLine(lines[i]));
        }
        var start = 0;
        while (start < trailingLines.Count && string.IsNullOrWhiteSpace(trailingLines[start])) start++;
        var end = trailingLines.Count - 1;
        while (end >= start && string.IsNullOrWhiteSpace(trailingLines[end])) end--;
        return end < start ? string.Empty : string.Join('\n', trailingLines.GetRange(start, end - start + 1));
    }

    private static string SanitizeDetailLine(string? line) =>
        (line ?? string.Empty).Replace(ExecutionContractMarker, string.Empty, StringComparison.OrdinalIgnoreCase);

    private static void AppendMetadataLine(StringBuilder detail, string label, string value) {
        var normalized = NormalizeMetadataValue(value);
        if (normalized.Length == 0) return;
        detail.Append("- ").Append(label).Append(": ").Append(BuildCodeSpan(normalized)).AppendLine();
    }

    private static string NormalizeMetadataValue(string? value) {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var result = new StringBuilder(value.Length);
        var pendingWhitespace = false;
        foreach (var character in value) {
            if (char.IsControl(character) || char.IsWhiteSpace(character)) {
                pendingWhitespace = result.Length > 0;
                continue;
            }
            if (pendingWhitespace) {
                result.Append(' ');
                pendingWhitespace = false;
            }
            result.Append(character);
        }
        return result.ToString().Trim();
    }

    private static string BuildCodeSpan(string value) {
        var longestRun = 0;
        var currentRun = 0;
        foreach (var character in value) {
            if (character == '`') {
                longestRun = Math.Max(longestRun, ++currentRun);
            } else {
                currentRun = 0;
            }
        }
        var fence = new string('`', Math.Max(1, longestRun + 1));
        var padded = value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]) || value[0] == '`' || value[^1] == '`');
        return padded ? fence + " " + value + " " + fence : fence + value + fence;
    }
}
