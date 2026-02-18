using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IntelligenceX.Chat.App.Rendering;

/// <summary>
/// Normalizes common LLM markdown artifacts before UI rendering.
/// </summary>
internal static class TranscriptMarkdownNormalizer {
    private const int StrongFlattenMaxIterations = 32;

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

    private static readonly Regex LeadingWhitespaceInsideStrongOpenRegex = new(
        @"(?:(?<=^)|(?<=[\s(\[{>]))\*\*[ \t]+(?=[^\s*\r\n])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MissingSpaceBeforeBoldMetricRegex = new(
        @"(?<=\s)-(?=\*\*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LineStartMissingSpaceBeforeBoldBulletRegex = new(
        @"(?m)^(?<indent>\s*)-(?=\*\*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LineStartHostLabelBulletRegex = new(
        @"(?m)^(?<indent>\s*)-(?=[A-Z]{2,}\d+\b)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SingleStarMetricRegex = new(
        @"(?<=\s)-\*(?=[A-Za-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SignalOuterStrongLineRegex = new(
        @"(?m)^(?<prefix>\s*-\s+Signal\s+\*\*)(?<body>[^\r\n]*)(?<suffix>\*\*)(?<tail>\s*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex InlineCodeSpanRegex = new(
        @"`[^`\r\n]*`",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UnmatchedInlineCodeTailRegex = new(
        @"`[^\r\n]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex NestedStrongSpanRegex = new(
        @"\*\*(?<inner>[^*\r\n]+)\*\*",
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
        @"(?<=\))\s*(?=\d+\.(?:\^\s*|\s*[*_]{2}|\s+)\S)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CollapsedOrderedListAfterDetailRegex = new(
        @"(?<=\))\s+(?=\d+[.)]\s*[*_]{0,2}\s*\S)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CollapsedOrderedListAfterStrongRegex = new(
        @"(?<=\*\*)\s+(?=\d+[.)]\s*[*_]{0,2}\s*\S)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OrderedListCaretRegex = new(
        @"(?m)^(?<lead>\s*\d+\.)\s*\^\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OrderedListParenMarkerRegex = new(
        @"(?m)^(?<indent>\s*)(?<num>\d+)\)(?=\s*\S)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OrderedListMarkerMissingSpaceRegex = new(
        @"(?m)^(?<lead>\s*\d+\.)(?=\S)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TightParenJoinRegex = new(
        @"(?<=[\p{L}\p{N}\)])\((?=[\p{L}][^\r\n)]*\))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AcronymParenJoinRegex = new(
        @"(?<=[\p{L}\p{N}\)])\((?=[A-Z]{2,})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StrongCloseAcronymParenJoinRegex = new(
        @"(?<=\*\*)\((?=[A-Z]{2,})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StrongCloseParenJoinRegex = new(
        @"(?<=\*\*)\((?=[\p{L}][^\r\n)]*\))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OrderedItemStrongMissingCloseBeforeParenRegex = new(
        @"(?m)^(?<lead>\s*\d+\.\s+)\*\*(?<title>[^*\r\n()]+)\((?<detail>[^)\r\n]+)\)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string NormalizeForRendering(string? text) {
        var normalized = text ?? string.Empty;
        if (normalized.Length == 0) {
            return string.Empty;
        }

        return ApplyTransformOutsideFencedCodeBlocks(normalized, static segment => {
            var protectedInlineCode = ProtectInlineCodeSpans(segment, out var codeSpans, out var tokenPrefix);
            var value = protectedInlineCode;
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
            value = LineStartMissingSpaceBeforeBoldBulletRegex.Replace(value, "${indent}- ");
            value = LineStartHostLabelBulletRegex.Replace(value, "${indent}- ");
            value = SingleStarMetricRegex.Replace(value, "- **");
            value = SignalOuterStrongLineRegex.Replace(value, static match => {
                var body = match.Groups["body"].Value;
                if (!body.Contains("**", StringComparison.Ordinal)) {
                    return match.Value;
                }

                var cleaned = FlattenNestedStrongOutsideInlineCode(body);
                if (cleaned.Equals(body, StringComparison.Ordinal)) {
                    return match.Value;
                }

                return match.Groups["prefix"].Value + cleaned + match.Groups["suffix"].Value + match.Groups["tail"].Value;
            });
            value = CollapsedBulletRegex.Replace(value, "\n- **");
            value = CollapsedOrderedListAfterDetailRegex.Replace(value, "\n");
            value = CollapsedOrderedListAfterStrongRegex.Replace(value, "\n");
            value = CollapsedOrderedListAfterParenRegex.Replace(value, "\n");
            value = OrderedListCaretRegex.Replace(value, "${lead} ");
            value = OrderedListParenMarkerRegex.Replace(value, "${indent}${num}.");
            value = OrderedListMarkerMissingSpaceRegex.Replace(value, "${lead} ");
            value = TightParenJoinRegex.Replace(value, " (");
            value = AcronymParenJoinRegex.Replace(value, " (");
            value = StrongCloseAcronymParenJoinRegex.Replace(value, " (");
            value = StrongCloseParenJoinRegex.Replace(value, " (");
            value = OrderedItemStrongMissingCloseBeforeParenRegex.Replace(value, static match => {
                var lead = match.Groups["lead"].Value;
                var title = match.Groups["title"].Value.Trim();
                var detail = match.Groups["detail"].Value.Trim();
                return lead + "**" + title + "** (" + detail + ")";
            });
            value = LeadingWhitespaceInsideStrongOpenRegex.Replace(value, "**");
            value = ExpandCollapsedMetricLines(value);
            value = ConvertLegacyMetricMarkdown(value);
            return RestoreInlineCodeSpans(value, codeSpans, tokenPrefix);
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

    private static string FlattenNestedStrongOutsideInlineCode(string body) {
        if (string.IsNullOrEmpty(body) || !body.Contains("**", StringComparison.Ordinal)) {
            return body;
        }

        var protectedBody = ProtectInlineCodeSpans(body, out var codeSpans, out var tokenPrefix);
        var flattened = FlattenNestedStrongSpans(protectedBody);
        return RestoreInlineCodeSpans(flattened, codeSpans, tokenPrefix);
    }

    private static string FlattenNestedStrongSpans(string input) {
        if (string.IsNullOrEmpty(input) || !input.Contains("**", StringComparison.Ordinal)) {
            return input;
        }

        string current = input;
        for (var i = 0; i < StrongFlattenMaxIterations; i++) {
            var next = NestedStrongSpanRegex.Replace(
                current,
                static match => match.Groups["inner"].Value);
            if (next.Equals(current, StringComparison.Ordinal)) {
                return next;
            }

            current = next;
        }

        return current;
    }

    private static string ProtectInlineCodeSpans(string input, out List<string> codeSpans, out string tokenPrefix) {
        var capturedCodeSpans = new List<string>();
        if (string.IsNullOrEmpty(input) || input.IndexOf('`', StringComparison.Ordinal) < 0) {
            codeSpans = capturedCodeSpans;
            tokenPrefix = string.Empty;
            return input;
        }
        var prefix = "\u001FIXCODE_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + "_";

        var protectedInput = InlineCodeSpanRegex.Replace(input, match => {
            var index = capturedCodeSpans.Count;
            capturedCodeSpans.Add(match.Value);
            return prefix + index.ToString(CultureInfo.InvariantCulture) + "\u001E";
        });
        protectedInput = UnmatchedInlineCodeTailRegex.Replace(protectedInput, match => {
            var index = capturedCodeSpans.Count;
            capturedCodeSpans.Add(match.Value);
            return prefix + index.ToString(CultureInfo.InvariantCulture) + "\u001E";
        });

        tokenPrefix = prefix;
        codeSpans = capturedCodeSpans;
        return protectedInput;
    }

    private static string RestoreInlineCodeSpans(string input, IReadOnlyList<string> codeSpans, string tokenPrefix) {
        if (codeSpans.Count == 0 || string.IsNullOrEmpty(input) || string.IsNullOrEmpty(tokenPrefix)) {
            return input;
        }

        var placeholderRegex = new Regex(
            Regex.Escape(tokenPrefix) + "(?<index>\\d+)\u001E",
            RegexOptions.CultureInvariant);

        return placeholderRegex.Replace(input, match => {
            if (!int.TryParse(match.Groups["index"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index)) {
                return match.Value;
            }

            return index >= 0 && index < codeSpans.Count
                ? codeSpans[index]
                : match.Value;
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
