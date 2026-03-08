using System;
using System.Collections.Generic;
using System.Text;

namespace IntelligenceX.Chat.App.Rendering;

internal static partial class TranscriptMarkdownNormalizer {
    private static string ExpandCollapsedMetricLines(string text) {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var current = text;

        while (true) {
            var afterStatus = StatusCollapsedLineRegex.Replace(
                current,
                match => match.Groups["lead"].Value + newline + "- " + match.Groups["rest"].Value);

            var afterBullets = BulletCollapsedLineRegex.Replace(
                afterStatus,
                match => match.Groups["lead"].Value + newline + "- " + match.Groups["rest"].Value);

            if (afterBullets == current) {
                return afterBullets;
            }

            current = afterBullets;
        }
    }

    private static string ConvertLegacyMetricMarkdown(string text) {
        var statusNormalized = LegacyStatusSummaryRegex.Replace(
            text,
            match => {
                var indent = match.Groups["indent"].Value;
                var value = match.Groups["value"].Value.Trim();
                return value.Length == 0 ? indent + "Status" : indent + "Status **" + value + "**";
            });

        return LegacyBoldMetricBulletRegex.Replace(
            statusNormalized,
            match => {
                var indent = match.Groups["indent"].Value;
                var label = match.Groups["label"].Value.Trim();
                var value = match.Groups["value"].Value.Trim();
                if (value.Length == 0) {
                    return indent + label;
                }

                if (value.Contains("**", StringComparison.Ordinal)
                    || value.Contains("`", StringComparison.Ordinal)
                    || value.Contains("~~", StringComparison.Ordinal)
                    || value.Contains("==", StringComparison.Ordinal)) {
                    return indent + label + " " + value;
                }

                return indent + label + " **" + value + "**";
            });
    }

    private static string RepairDanglingTrailingStrongMetricClosers(string text) {
        if (string.IsNullOrEmpty(text) || text.IndexOf("****", StringComparison.Ordinal) < 0) {
            return text;
        }

        var hasCrLf = text.Contains("\r\n", StringComparison.Ordinal);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var changed = false;

        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i] ?? string.Empty;
            var trimmedStart = line.TrimStart();
            if (!trimmedStart.StartsWith("- ", StringComparison.Ordinal)
                && !OrderedListLeadRegex.IsMatch(trimmedStart)) {
                continue;
            }

            var repaired = TrailingDanglingStrongMetricTokenRegex.Replace(line, static match => {
                var token = match.Groups["token"].Value.Trim();
                if (token.Length == 0 || token.Contains("**", StringComparison.Ordinal)) {
                    return match.Value;
                }

                return "**" + token + "**" + match.Groups["tail"].Value;
            });
            if (repaired.Equals(line, StringComparison.Ordinal)) {
                continue;
            }

            lines[i] = repaired;
            changed = true;
        }

        if (!changed) {
            return text;
        }

        var rebuilt = string.Join("\n", lines);
        return hasCrLf ? rebuilt.Replace("\n", "\r\n", StringComparison.Ordinal) : rebuilt;
    }

    private static string RepairMalformedMetricValueStrongRuns(string text) {
        if (string.IsNullOrEmpty(text) || text.IndexOf("**", StringComparison.Ordinal) < 0) {
            return text;
        }

        var hasCrLf = text.Contains("\r\n", StringComparison.Ordinal);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var changed = false;

        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i] ?? string.Empty;
            if (!TryRepairMalformedMetricValueStrongRunLine(line, out var repaired)
                || repaired.Equals(line, StringComparison.Ordinal)) {
                continue;
            }

            lines[i] = repaired;
            changed = true;
        }

        if (!changed) {
            return text;
        }

        var rebuilt = string.Join("\n", lines);
        return hasCrLf ? rebuilt.Replace("\n", "\r\n", StringComparison.Ordinal) : rebuilt;
    }

    private static bool TryRepairMalformedMetricValueStrongRunLine(string line, out string repaired) {
        repaired = line;
        var trimmedStart = line.TrimStart();
        if (!trimmedStart.StartsWith("- ", StringComparison.Ordinal)
            && !OrderedListLeadRegex.IsMatch(trimmedStart)) {
            return false;
        }

        repaired = OveropenedMetricValueStrongRegex.Replace(line, static match => {
            var value = match.Groups["value"].Value.Trim();
            return value.Length == 0
                ? match.Value
                : match.Groups["prefix"].Value + "**" + value + "**" + match.Groups["tail"].Value;
        });

        repaired = AdjacentMetricStrongValueRegex.Replace(repaired, static match => {
            var first = match.Groups["first"].Value.Trim();
            var second = match.Groups["second"].Value.Trim();
            if (first.Length == 0 || second.Length == 0) {
                return match.Value;
            }

            if (IsSymbolOnlyMetricValue(first)) {
                return match.Groups["prefix"].Value + first + " **" + second + "**" + match.Groups["tail"].Value;
            }

            return match.Groups["prefix"].Value
                   + "**"
                   + first
                   + "** **"
                   + second
                   + "**"
                   + match.Groups["tail"].Value;
        });

        repaired = MissingTrailingStrongMetricCloseRegex.Replace(repaired, static match => {
            var value = match.Groups["value"].Value.Trim();
            return value.Length == 0
                ? match.Value
                : match.Groups["prefix"].Value + "**" + value + "**" + match.Groups["tail"].Value;
        });

        return true;
    }

    private static bool IsSymbolOnlyMetricValue(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        foreach (var ch in value) {
            if (char.IsWhiteSpace(ch)) {
                continue;
            }

            if (char.IsLetterOrDigit(ch)) {
                return false;
            }
        }

        return true;
    }

    private static string RepairWrappedSignalFlowLines(string text) {
        if (string.IsNullOrEmpty(text)) {
            return text;
        }

        return WrappedSignalFlowLineRegex.Replace(text, static match => {
            if (match.Value.IndexOf('`', StringComparison.Ordinal) >= 0) {
                return match.Value;
            }

            var inner = match.Groups["inner"].Value;
            var markerIndex = inner.IndexOf("-> **", StringComparison.Ordinal);
            if (markerIndex < 0) {
                markerIndex = inner.IndexOf("->**", StringComparison.Ordinal);
            }
            if (markerIndex <= 0) {
                return match.Value;
            }

            var headline = inner[..markerIndex].TrimEnd();
            if (headline.Length == 0) {
                return match.Value;
            }

            var flow = inner[markerIndex..].TrimStart();
            if (flow.StartsWith("->**", StringComparison.Ordinal)) {
                flow = "-> **" + flow[4..];
            }
            if (!flow.StartsWith("-> **", StringComparison.Ordinal)) {
                return match.Value;
            }

            return match.Groups["prefix"].Value + "**" + headline + "** " + flow + match.Groups["tail"].Value;
        });
    }

    private static string NormalizeOverwrappedStrongSpans(string text) {
        var normalized = OverwrappedStrongSpanRegex.Replace(text, static match => {
            var inner = match.Groups["inner"].Value.Trim();
            return inner.Length == 0 ? match.Value : "**" + inner + "**";
        });

        return DanglingTrailingStrongCloseRegex.Replace(normalized, static match => {
            var value = match.Groups["inner"].Value.Trim();
            if (value.Length == 0 || value.Contains("**", StringComparison.Ordinal)) {
                return match.Value;
            }

            return match.Groups["prefix"].Value + "**" + value + "**" + match.Groups["tail"].Value;
        });
    }

    private static string NormalizeSignalFlowLabelSpacing(string text) {
        if (string.IsNullOrEmpty(text) || text.IndexOf("->", StringComparison.Ordinal) < 0) {
            return text;
        }

        var spacedArrows = SignalFlowArrowTightStrongRegex.Replace(text, "-> ");
        var hasCrLf = spacedArrows.Contains("\r\n", StringComparison.Ordinal);
        var normalized = spacedArrows.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var changed = !spacedArrows.Equals(text, StringComparison.Ordinal);

        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i] ?? string.Empty;
            if (line.IndexOf("->", StringComparison.Ordinal) < 0) {
                continue;
            }

            var fixedLine = NormalizeSignalFlowArrowSegments(line);
            if (fixedLine.Equals(line, StringComparison.Ordinal)) {
                continue;
            }

            lines[i] = fixedLine;
            changed = true;
        }

        if (!changed) {
            return text;
        }

        var rebuilt = string.Join("\n", lines);
        return hasCrLf ? rebuilt.Replace("\n", "\r\n", StringComparison.Ordinal) : rebuilt;
    }

    private static string NormalizeSignalFlowArrowSegments(string line) {
        var segments = line.Split("->", StringSplitOptions.None);
        if (segments.Length < 2) {
            return line;
        }

        var builder = new StringBuilder(line.Length + 8);
        builder.Append(segments[0]);
        for (var i = 1; i < segments.Length; i++) {
            builder.Append("->");
            builder.Append(NormalizeSignalFlowSegmentLabelSpacing(segments[i]));
        }

        return builder.ToString();
    }

    private static string NormalizeSignalFlowSegmentLabelSpacing(string segment) {
        if (string.IsNullOrEmpty(segment)) {
            return segment;
        }

        var start = 0;
        while (start < segment.Length && char.IsWhiteSpace(segment[start])) {
            start++;
        }
        if (start >= segment.Length) {
            return segment;
        }

        var strongNormalized = TryNormalizeLeadingStrongSignalLabel(segment, start);
        if (!strongNormalized.Equals(segment, StringComparison.Ordinal)) {
            return strongNormalized;
        }

        return TryNormalizeLeadingPlainSignalLabel(segment, start);
    }

    private static string TryNormalizeLeadingStrongSignalLabel(string segment, int start) {
        if (start + 1 >= segment.Length || segment[start] != '*' || segment[start + 1] != '*') {
            return segment;
        }

        var close = segment.IndexOf("**", start + 2, StringComparison.Ordinal);
        if (close < 0 || close + 2 >= segment.Length) {
            return segment;
        }

        if (segment[close - 1] != ':') {
            return segment;
        }

        var next = segment[close + 2];
        if (char.IsWhiteSpace(next)) {
            return segment;
        }

        return segment.Insert(close + 2, " ");
    }

    private static string TryNormalizeLeadingPlainSignalLabel(string segment, int start) {
        var colon = segment.IndexOf(':', start);
        if (colon <= start || colon >= segment.Length - 1) {
            return segment;
        }

        var previous = segment[colon - 1];
        var next = segment[colon + 1];
        if (char.IsWhiteSpace(next) || next == '/' || next == '\\') {
            return segment;
        }

        if (!char.IsLetter(previous) || !char.IsLetter(next)) {
            return segment;
        }

        return segment.Insert(colon + 1, " ");
    }

    private static string MergeSplitHostLabelBullets(string text) {
        if (string.IsNullOrEmpty(text) || text.IndexOf('\n') < 0) {
            return text;
        }

        var hasCrLf = text.Contains("\r\n", StringComparison.Ordinal);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        if (lines.Length < 2) {
            return text;
        }

        var merged = new List<string>(lines.Length);
        for (var i = 0; i < lines.Length; i++) {
            var current = lines[i] ?? string.Empty;
            if (i + 1 < lines.Length
                && StandaloneHostLabelBulletRegex.IsMatch(current)
                && ShouldAttachHostLabelContinuation(lines[i + 1])) {
                var next = (lines[i + 1] ?? string.Empty).TrimStart();
                merged.Add(current.TrimEnd() + " " + next);
                i++;
                continue;
            }

            merged.Add(current);
        }

        var rebuilt = string.Join("\n", merged);
        return hasCrLf ? rebuilt.Replace("\n", "\r\n", StringComparison.Ordinal) : rebuilt;
    }

    private static bool ShouldAttachHostLabelContinuation(string line) {
        if (string.IsNullOrWhiteSpace(line)) {
            return false;
        }

        var trimmed = line.TrimStart();
        return !StructuralMarkdownLineRegex.IsMatch(trimmed);
    }

    private static bool RequiresStreamingFullTypographyNormalization(string text) {
        if (string.IsNullOrEmpty(text)) {
            return false;
        }

        return text.Contains("****", StringComparison.Ordinal)
               || text.Contains("->**", StringComparison.Ordinal)
               || HasCompactSignalFlowLabelSpacing(text);
    }

    private static bool HasCompactSignalFlowLabelSpacing(string text) {
        if (string.IsNullOrEmpty(text) || text.IndexOf("->", StringComparison.Ordinal) < 0) {
            return false;
        }

        var normalized = NormalizeSignalFlowLabelSpacing(text);
        return !normalized.Equals(text, StringComparison.Ordinal);
    }
}
