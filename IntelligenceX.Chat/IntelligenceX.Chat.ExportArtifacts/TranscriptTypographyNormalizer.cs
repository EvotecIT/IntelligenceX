using System;
using System.Text.RegularExpressions;

namespace IntelligenceX.Chat.ExportArtifacts;

/// <summary>
/// Shared transcript typography repairs used by export and host preparation layers.
/// </summary>
public static class TranscriptTypographyNormalizer {
    private const string SignalFlowLabelAlternation = "Why it matters|Action|Next action|Fix action";
    private static readonly string[] SignalFlowLabels = ["Why it matters", "Action", "Next action", "Fix action"];
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex OverwrappedStrongSpanRegex = new(
        @"(?<!\*)\*{4}(?<inner>[^*\r\n]+)\*{4}(?!\*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexMatchTimeout);
    private static readonly Regex WrappedSignalFlowLineRegex = new(
        @"^(?<prefix>\s*-\s+[^\r\n]*?)\*\*(?<inner>[^\r\n]*->\s*\*\*[^\r\n]*?)\*\*(?<tail>\s*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexMatchTimeout);
    private static readonly Regex SignalFlowArrowLabelTightRegex = new(
        @"->\s*\*\*(?=(?:" + SignalFlowLabelAlternation + @"):)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexMatchTimeout);
    private static readonly Regex SignalFlowBoldLabelMissingSpaceRegex = new(
        @"(?<label>\*\*(?:" + SignalFlowLabelAlternation + @"):\*\*)(?=\S)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexMatchTimeout);
    private static readonly Regex SignalFlowPlainLabelMissingSpaceRegex = new(
        @"(?<label>(?<![\p{L}\p{N}_])(?:" + SignalFlowLabelAlternation + @"):)(?=\S)(?!\*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexMatchTimeout);
    private static readonly Regex TightSignalLabelRegex = new(
        @"(?:" + SignalFlowLabelAlternation + @"):\S",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexMatchTimeout);

    /// <summary>
    /// Normalizes shared transcript typography artifacts while leaving fenced code block content untouched.
    /// </summary>
    /// <param name="markdown">Markdown source to normalize.</param>
    /// <returns>Normalized markdown.</returns>
    public static string NormalizeMarkdownOutsideFencedCodeBlocks(string markdown) {
        if (string.IsNullOrEmpty(markdown) || !RequiresNormalization(markdown)) {
            return markdown ?? string.Empty;
        }

        var newline = DetectLineEnding(markdown);
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var insideFence = false;
        var fenceMarker = '\0';
        var fenceLength = 0;

        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i] ?? string.Empty;
            var trimmed = line.TrimStart();
            if (TryGetFenceToken(trimmed, out var marker, out var markerRunLength)) {
                if (!insideFence) {
                    insideFence = true;
                    fenceMarker = marker;
                    fenceLength = markerRunLength;
                    continue;
                }

                if (marker == fenceMarker
                    && markerRunLength >= fenceLength
                    && IsClosingFenceLine(trimmed, markerRunLength)) {
                    insideFence = false;
                    fenceMarker = '\0';
                    fenceLength = 0;
                }

                continue;
            }

            if (insideFence) {
                continue;
            }

            lines[i] = NormalizeLine(line);
        }

        return string.Join(newline, lines);
    }

    /// <summary>
    /// Normalizes a single non-fenced transcript line for shared signal-flow and emphasis artifacts.
    /// </summary>
    /// <param name="line">Input line.</param>
    /// <returns>Normalized line.</returns>
    public static string NormalizeLine(string line) {
        if (string.IsNullOrEmpty(line)) {
            return string.Empty;
        }

        var value = OverwrappedStrongSpanRegex.Replace(line, static match => {
            var inner = match.Groups["inner"].Value.Trim();
            return inner.Length == 0 ? match.Value : "**" + inner + "**";
        });
        value = RepairWrappedSignalFlowLine(value);
        value = NormalizeSignalFlowLabelSpacing(value);
        return value;
    }

    private static bool RequiresNormalization(string markdown) {
        if (string.IsNullOrEmpty(markdown)) {
            return false;
        }

        return markdown.Contains("****", StringComparison.Ordinal)
               || markdown.Contains("->**", StringComparison.Ordinal)
               || ContainsAnySignalFlowBoldLabel(markdown)
               || TightSignalLabelRegex.IsMatch(markdown);
    }

    private static string RepairWrappedSignalFlowLine(string line) {
        if (string.IsNullOrEmpty(line)) {
            return string.Empty;
        }

        return WrappedSignalFlowLineRegex.Replace(line, static match => {
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

    private static string NormalizeSignalFlowLabelSpacing(string line) {
        if (string.IsNullOrEmpty(line) || !ContainsAnySignalFlowLabelPrefix(line)) {
            return line;
        }

        var value = SignalFlowArrowLabelTightRegex.Replace(line, "-> **");
        value = SignalFlowBoldLabelMissingSpaceRegex.Replace(value, "${label} ");
        value = SignalFlowPlainLabelMissingSpaceRegex.Replace(value, "${label} ");
        return value;
    }

    private static bool ContainsAnySignalFlowBoldLabel(string text) {
        if (string.IsNullOrEmpty(text)) {
            return false;
        }

        for (var i = 0; i < SignalFlowLabels.Length; i++) {
            var marker = "-> **" + SignalFlowLabels[i] + ":**";
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAnySignalFlowLabelPrefix(string text) {
        if (string.IsNullOrEmpty(text)) {
            return false;
        }

        for (var i = 0; i < SignalFlowLabels.Length; i++) {
            var marker = SignalFlowLabels[i] + ":";
            if (text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return false;
    }

    private static string DetectLineEnding(string text) {
        if (text.Contains("\r\n", StringComparison.Ordinal)) {
            return "\r\n";
        }

        if (text.Contains('\r')) {
            return "\r";
        }

        return "\n";
    }

    private static bool TryGetFenceToken(string trimmedLine, out char marker, out int runLength) {
        marker = '\0';
        runLength = 0;
        if (string.IsNullOrEmpty(trimmedLine)) {
            return false;
        }

        var first = trimmedLine[0];
        if (first != '`' && first != '~') {
            return false;
        }

        var length = 0;
        while (length < trimmedLine.Length && trimmedLine[length] == first) {
            length++;
        }

        if (length < 3) {
            return false;
        }

        marker = first;
        runLength = length;
        return true;
    }

    private static bool IsClosingFenceLine(string trimmedLine, int markerRunLength) {
        if (string.IsNullOrEmpty(trimmedLine) || markerRunLength < 3 || trimmedLine.Length < markerRunLength) {
            return false;
        }

        for (var i = markerRunLength; i < trimmedLine.Length; i++) {
            if (!char.IsWhiteSpace(trimmedLine[i])) {
                return false;
            }
        }

        return true;
    }
}
