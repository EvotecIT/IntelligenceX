using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace IntelligenceX.Chat.App.Rendering;

/// <summary>
/// Normalizes common LLM markdown artifacts before UI rendering.
/// </summary>
internal static class TranscriptMarkdownNormalizer {
    // Hard stop for nested-strong flattening; 32 iterations safely handles deeply nested artifacts
    // while guaranteeing convergence on malformed inputs.
    private const int StrongFlattenMaxIterations = 32;
    // Keeps labeled-bullet prefix capture bounded while still supporting long localized labels.
    private const int LabeledOuterStrongPrefixMaxChars = 120;

    private static readonly Regex EmojiWordJoinRegex = new(
        @"([✅☑✔❌⚠🔥])(?!\s)(?=[\p{L}\p{N}])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ZeroWidthWhitespaceRegex = new(
        @"[\u200B\u2060\uFEFF]",
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

    private static readonly Regex OverwrappedStrongSpanRegex = new(
        @"(?<!\*)\*{4}(?<inner>[^*\r\n]+)\*{4}(?!\*)",
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

    private static readonly Regex LineStartUnicodeDashBulletRegex = new(
        @"(?m)^(?<indent>\s*)[‐‑‒–—−](?=(?:\s*\*\*|[A-Z]{2,}\d+\b|[\p{Lu}][\p{L}\p{N}]{1,}\b))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LineStartHostLabelBulletRegex = new(
        @"(?m)^(?<indent>\s*)-(?=[A-Z]{2,}\d+\b)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SingleStarMetricRegex = new(
        @"(?<=\s)-\*(?=[A-Za-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LabeledOuterStrongLineRegex = new(
        @"(?m)^(?<prefix>\s*-\s+[^*\r\n]{2," + LabeledOuterStrongPrefixMaxChars.ToString(CultureInfo.InvariantCulture) + @"}\s+\*\*)(?<body>[^\r\n]*)(?<suffix>\*\*)(?<tail>\s*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WrappedSignalFlowLineRegex = new(
        @"(?m)^(?<prefix>\s*-\s+[^\r\n]*?)\*\*(?<inner>[^\r\n]*->\s*\*\*[^\r\n]*?)\*\*(?<tail>\s*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SignalFlowArrowLabelTightRegex = new(
        @"->\s*\*\*(?=(?:Why it matters|Action|Next action|Fix action):)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SignalFlowBoldLabelMissingSpaceRegex = new(
        @"(?<label>\*\*(?:Why it matters|Action|Next action|Fix action):\*\*)(?=\S)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SignalFlowPlainLabelMissingSpaceRegex = new(
        @"(?<label>(?<![\p{L}\p{N}_])(?:Why it matters|Action|Next action|Fix action):)(?=\S)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SentenceCollapsedBulletRegex = new(
        @"(?<=[\.\!\?\)\]])\s*(?=-\s*(?:\*\*[^\r\n]|[A-Z]{2,}\d+\b))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex InlineCodeSpanRegex = new(
        @"`[^`\r\n]*`",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UnmatchedInlineCodeTailRegex = new(
        @"`[^\r\n]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex InlineCodePlaceholderRegex = new(
        "\u001FIXCODE_(?<prefix>[0-9a-f]{8})_(?<index>\\d+)\u001E",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
        @"\*\*Status:\s|-\*\*|-\*[A-Za-z]|:\*\*\S|^\s*-[A-Z]{2,}\d+\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex StandaloneHostLabelBulletRegex = new(
        @"^\s*-(?:\s*\*\*)?\s*[A-Z]{2,}\d+(?:\s*\*\*)?\s*:?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StructuralMarkdownLineRegex = new(
        @"^(?:[-+*]\s+|\d+[.)]\s+|#{1,6}\s+|>\s?|```|~~~|\|)",
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

    private static int InlineCodePlaceholderCounter;

    public static string NormalizeForRendering(string? text) {
        var normalized = text ?? string.Empty;
        if (normalized.Length == 0) {
            return string.Empty;
        }

        return ApplyTransformOutsideFencedCodeBlocks(normalized, static segment => {
            var protectedInlineCode = ProtectInlineCodeSpans(segment, out var codeSpans, out var tokenPrefix);
            var value = protectedInlineCode;
            value = ZeroWidthWhitespaceRegex.Replace(value, string.Empty);
            value = EmojiWordJoinRegex.Replace(value, "$1 ");
            value = NumberedChoiceJoinRegex.Replace(value, "$1 ");
            value = LetterToNumberedChoiceJoinRegex.Replace(value, " ");
            value = SentenceCollapsedBulletRegex.Replace(value, "\n");
            value = RepairWrappedSignalFlowLines(value);
            value = NormalizeSignalFlowLabelSpacing(value);
            value = TightBoldValueRegex.Replace(value, " ");
            value = TightBoldSuffixRegex.Replace(value, "$1 ");
            value = NormalizeOverwrappedStrongSpans(value);
            value = SimpleStrongSpanRegex.Replace(value, static match => {
                var inner = match.Groups["inner"].Value;
                var trimmed = inner.Trim();
                return trimmed.Length == 0 ? match.Value : "**" + trimmed + "**";
            });
            value = MissingSpaceBeforeBoldMetricRegex.Replace(value, "- ");
            value = LineStartUnicodeDashBulletRegex.Replace(value, "${indent}-");
            value = LineStartMissingSpaceBeforeBoldBulletRegex.Replace(value, "${indent}- ");
            value = LineStartHostLabelBulletRegex.Replace(value, "${indent}- ");
            value = SingleStarMetricRegex.Replace(value, "- **");
            value = LabeledOuterStrongLineRegex.Replace(value, static match => {
                var body = match.Groups["body"].Value;
                if (!body.Contains("**", StringComparison.Ordinal)) {
                    return match.Value;
                }

                var trimmedBody = body.TrimEnd();
                if (trimmedBody.Length == 0) {
                    return match.Value;
                }

                var lastBodyChar = trimmedBody[^1];
                if (lastBodyChar != '.' && lastBodyChar != '!' && lastBodyChar != '?' && lastBodyChar != ')') {
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
            value = MergeSplitHostLabelBullets(value);
            value = ExpandCollapsedMetricLines(value);
            value = ConvertLegacyMetricMarkdown(value);
            return RestoreInlineCodeSpans(value, codeSpans, tokenPrefix);
        });
    }

    /// <summary>
    /// Lightweight sanitizer for partial streaming text before render.
    /// Keeps edits conservative to avoid reshaping incomplete markdown while still removing
    /// the most common visually-breaking artifacts seen in deltas.
    /// </summary>
    public static string NormalizeForStreamingPreview(string? text) {
        var normalized = text ?? string.Empty;
        if (normalized.Length == 0) {
            return string.Empty;
        }

        return ApplyTransformOutsideFencedCodeBlocks(normalized, static segment => {
            var value = ZeroWidthWhitespaceRegex.Replace(segment, string.Empty);
            if (RequiresStreamingFullTypographyNormalization(value)) {
                return NormalizeForRendering(value);
            }

            value = LineStartUnicodeDashBulletRegex.Replace(value, "${indent}-");
            value = LineStartMissingSpaceBeforeBoldBulletRegex.Replace(value, "${indent}- ");
            value = LineStartHostLabelBulletRegex.Replace(value, "${indent}- ");
            value = LeadingWhitespaceInsideStrongOpenRegex.Replace(value, "**");
            value = MergeSplitHostLabelBullets(value);
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

    private static string RepairWrappedSignalFlowLines(string text) {
        if (string.IsNullOrEmpty(text)) {
            return text;
        }

        return WrappedSignalFlowLineRegex.Replace(text, static match => {
            // Never rewrite lines that contain inline-code markers.
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
        return OverwrappedStrongSpanRegex.Replace(text, static match => {
            var inner = match.Groups["inner"].Value.Trim();
            return inner.Length == 0 ? match.Value : "**" + inner + "**";
        });
    }

    private static string NormalizeSignalFlowLabelSpacing(string text) {
        if (string.IsNullOrEmpty(text)) {
            return text;
        }

        if (text.IndexOf("why it matters:", StringComparison.OrdinalIgnoreCase) < 0
            && text.IndexOf("action:", StringComparison.OrdinalIgnoreCase) < 0
            && text.IndexOf("next action:", StringComparison.OrdinalIgnoreCase) < 0
            && text.IndexOf("fix action:", StringComparison.OrdinalIgnoreCase) < 0) {
            return text;
        }

        var value = SignalFlowArrowLabelTightRegex.Replace(text, "-> **");
        value = SignalFlowBoldLabelMissingSpaceRegex.Replace(value, static match => match.Groups["label"].Value + " ");
        value = SignalFlowPlainLabelMissingSpaceRegex.Replace(value, static match => match.Groups["label"].Value + " ");
        return value;
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
               || text.Contains("-> **Why it matters:**", StringComparison.OrdinalIgnoreCase)
               || text.Contains("-> **Action:**", StringComparison.OrdinalIgnoreCase)
               || text.Contains("-> **Next action:**", StringComparison.OrdinalIgnoreCase)
               || text.Contains("-> **Fix action:**", StringComparison.OrdinalIgnoreCase)
               || text.IndexOf("why it matters:", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("action:", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("next action:", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("fix action:", StringComparison.OrdinalIgnoreCase) >= 0;
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
                match => {
                    var inner = match.Groups["inner"].Value;
                    if (inner.Length == 0) {
                        return inner;
                    }

                    var prefix = string.Empty;
                    var suffix = string.Empty;
                    var start = match.Index;
                    var end = match.Index + match.Length;
                    if (start > 0) {
                        var before = current[start - 1];
                        if (!char.IsWhiteSpace(before) && IsWordLikeChar(before) && IsWordLikeChar(inner[0])) {
                            prefix = " ";
                        }
                    }

                    if (end < current.Length) {
                        var after = current[end];
                        if (!char.IsWhiteSpace(after) && IsWordLikeChar(inner[^1]) && IsWordLikeChar(after)) {
                            suffix = " ";
                        }
                    }

                    return prefix + inner + suffix;
                });
            if (next.Equals(current, StringComparison.Ordinal)) {
                return next;
            }

            current = next;
        }

        return current;
    }

    private static bool IsWordLikeChar(char value) {
        return char.IsLetterOrDigit(value);
    }

    private static string ProtectInlineCodeSpans(string input, out List<string> codeSpans, out string tokenPrefix) {
        var capturedCodeSpans = new List<string>();
        if (string.IsNullOrEmpty(input) || input.IndexOf('`', StringComparison.Ordinal) < 0) {
            codeSpans = capturedCodeSpans;
            tokenPrefix = string.Empty;
            return input;
        }
        var prefixId = unchecked((uint)Interlocked.Increment(ref InlineCodePlaceholderCounter))
            .ToString("x8", CultureInfo.InvariantCulture);
        var prefix = "\u001FIXCODE_" + prefixId + "_";

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

        // Keep placeholder replacement strictly opt-in for the current call's token prefix.
        // This avoids mutating user text that may coincidentally match the placeholder shape.
        return InlineCodePlaceholderRegex.Replace(input, match => {
            if (!match.Value.StartsWith(tokenPrefix, StringComparison.Ordinal)) {
                return match.Value;
            }

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
