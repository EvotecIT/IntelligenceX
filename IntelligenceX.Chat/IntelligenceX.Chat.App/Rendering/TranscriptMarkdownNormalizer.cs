using System;
using System.Text;
using System.Text.RegularExpressions;

namespace IntelligenceX.Chat.App.Rendering;

/// <summary>
/// Normalizes common LLM markdown artifacts before UI rendering.
/// </summary>
internal static class TranscriptMarkdownNormalizer {
    private static readonly Regex EmojiWordJoinRegex = new(
        @"([✅☑✔❌⚠🔥])(?!\s)(?=[\p{L}\p{N}])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NumberedChoiceJoinRegex = new(
        @"(\bor|\band|[,;:])(?!\s)(?=\d+\))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex LetterToNumberedChoiceJoinRegex = new(
        @"(?<=[A-Za-z])(?=\d+\))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CollapsedBulletRegex = new(
        @"(?<=\*\*)[ \t]-[ \t]\*\*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TightBoldValueRegex = new(
        @"(?<=:\*\*)(?=\S)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TightBoldSuffixRegex = new(
        @"(\*\*[^*\r\n]+\*\*)(?=[\p{L}\p{N}])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SimpleStrongSpanRegex = new(
        @"\*\*(?<inner>[^*\r\n]+)\*\*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MissingSpaceBeforeBoldMetricRegex = new(
        @"(?<=\s)-(?=\*\*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SingleStarMetricRegex = new(
        @"(?<=\s)-\*(?=[A-Za-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StatusCollapsedLineRegex = new(
        @"(?m)^(?<lead>\s*\*\*Status:[^\r\n]*?)[ \t]-[ \t](?<rest>\*\*.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BulletCollapsedLineRegex = new(
        @"(?m)^(?<lead>\s*-\s[^\r\n]*?)[ \t]-[ \t](?<rest>\*\*.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LegacyStatusSummaryRegex = new(
        @"(?m)^(?<indent>\s*)\*\*Status:\s*(?<value>[^*\r\n]+)\*\*\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LegacyBoldMetricBulletRegex = new(
        @"(?m)^(?<indent>\s*-\s)\*\*(?<label>[^*\r\n:]+):\*\*\s*(?<value>[^\r\n]*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LegacyRepairSignalRegex = new(
        @"\*\*Status:\s|-\*\*|-\*[A-Za-z]|:\*\*\S",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CollapsedOrderedListAfterParenRegex = new(
        @"(?<=\))[ \t]+(?=\d+\.(?:\^\s*|\s+)\S)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OrderedListCaretRegex = new(
        @"(?m)^(?<lead>\s*\d+\.)\s*\^\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AcronymParenJoinRegex = new(
        @"(?<=[\p{L}\p{N}\)])\((?=[A-Z]{2,})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StrongCloseAcronymParenJoinRegex = new(
        @"(?<=\*\*)\((?=[A-Z]{2,})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string NormalizeForRendering(string? text) {
        var normalized = text ?? string.Empty;
        if (normalized.Length == 0) {
            return string.Empty;
        }

        return ApplyTransformOutsideFencedCodeBlocks(normalized, static segment => {
            var value = segment;
            value = EmojiWordJoinRegex.Replace(value, "$1 ");
            value = NumberedChoiceJoinRegex.Replace(value, "$1 ");
            value = LetterToNumberedChoiceJoinRegex.Replace(value, " ");
            value = TightBoldValueRegex.Replace(value, " ");
            value = TightBoldSuffixRegex.Replace(value, "$1 ");
            value = SimpleStrongSpanRegex.Replace(value, static match => {
                var inner = match.Groups["inner"].Value;
                var trimmed = inner.Trim();
                return trimmed.Length == 0 ? match.Value : "**" + trimmed + "**";
            });
            value = MissingSpaceBeforeBoldMetricRegex.Replace(value, "- ");
            value = SingleStarMetricRegex.Replace(value, "- **");
            value = CollapsedBulletRegex.Replace(value, "\n- **");
            value = CollapsedOrderedListAfterParenRegex.Replace(value, "\n");
            value = OrderedListCaretRegex.Replace(value, "${lead} ");
            value = AcronymParenJoinRegex.Replace(value, " (");
            value = StrongCloseAcronymParenJoinRegex.Replace(value, " (");
            value = ExpandCollapsedMetricLines(value);
            value = ConvertLegacyMetricMarkdown(value);
            return value;
        });
    }

    public static bool TryRepairLegacyTranscript(string? text, out string normalized) {
        normalized = text ?? string.Empty;
        if (normalized.Length == 0 || !LegacyRepairSignalRegex.IsMatch(normalized)) {
            return false;
        }

        var repaired = NormalizeForRendering(normalized);
        if (repaired == normalized) {
            return false;
        }

        normalized = repaired;
        return true;
    }

    private static string ExpandCollapsedMetricLines(string text) {
        var newline = text.Contains("\r\n", System.StringComparison.Ordinal) ? "\r\n" : "\n";
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
                return value.Length == 0 ? indent + label : indent + label + " **" + value + "**";
            });
    }

    private static string ApplyTransformOutsideFencedCodeBlocks(string input, Func<string, string> transform) {
        if (string.IsNullOrEmpty(input)) {
            return input ?? string.Empty;
        }

        var output = new StringBuilder(input.Length);
        var outsideSegment = new StringBuilder();
        var inFence = false;
        var fenceMarker = '\0';
        var fenceRunLength = 0;

        var index = 0;
        while (index < input.Length) {
            var lineStart = index;
            while (index < input.Length && input[index] != '\r' && input[index] != '\n') {
                index++;
            }

            var lineEnd = index;
            if (index < input.Length && input[index] == '\r') {
                index++;
                if (index < input.Length && input[index] == '\n') {
                    index++;
                }
            } else if (index < input.Length && input[index] == '\n') {
                index++;
            }

            var line = input.Substring(lineStart, lineEnd - lineStart);
            var lineWithNewline = input.Substring(lineStart, index - lineStart);

            if (TryReadFenceRun(line, out var runMarker, out var runLength, out var runSuffix)) {
                if (!inFence) {
                    FlushOutsideSegment(output, outsideSegment, transform);
                    inFence = true;
                    fenceMarker = runMarker;
                    fenceRunLength = runLength;
                    output.Append(lineWithNewline);
                    continue;
                }

                if (runMarker == fenceMarker && runLength >= fenceRunLength && string.IsNullOrWhiteSpace(runSuffix)) {
                    inFence = false;
                    fenceMarker = '\0';
                    fenceRunLength = 0;
                    output.Append(lineWithNewline);
                    continue;
                }
            }

            if (inFence) {
                output.Append(lineWithNewline);
            } else {
                outsideSegment.Append(lineWithNewline);
            }
        }

        FlushOutsideSegment(output, outsideSegment, transform);
        return output.ToString();
    }

    private static void FlushOutsideSegment(StringBuilder output, StringBuilder outsideSegment, Func<string, string> transform) {
        if (outsideSegment.Length == 0) {
            return;
        }

        var transformed = transform(outsideSegment.ToString());
        output.Append(transformed);
        outsideSegment.Clear();
    }

    private static bool TryReadFenceRun(string line, out char marker, out int runLength, out string suffix) {
        marker = '\0';
        runLength = 0;
        suffix = string.Empty;
        if (line is null) {
            return false;
        }

        var trimmed = line.TrimStart();
        if (trimmed.Length < 3) {
            return false;
        }

        var first = trimmed[0];
        if (first != '`' && first != '~') {
            return false;
        }

        var i = 0;
        while (i < trimmed.Length && trimmed[i] == first) {
            i++;
        }

        if (i < 3) {
            return false;
        }

        marker = first;
        runLength = i;
        suffix = trimmed.Substring(i);
        return true;
    }
}
