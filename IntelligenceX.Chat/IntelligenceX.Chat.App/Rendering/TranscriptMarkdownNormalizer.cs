using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace IntelligenceX.Chat.App.Rendering;

/// <summary>
/// Normalizes common LLM markdown artifacts before UI rendering.
/// </summary>
internal static partial class TranscriptMarkdownNormalizer {
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

    private static readonly Regex DanglingTrailingStrongCloseRegex = new(
        @"(?m)^(?<prefix>\s*-\s+.*\s)(?<inner>[^\s*\r\n][^*\r\n]*?)\*{4}(?<tail>\s*)$",
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

    private static readonly Regex SignalFlowArrowTightStrongRegex = new(
        @"->\s*(?=\*\*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

    private static readonly Regex CachedToolEvidenceMarkerLineRegex = new(
        @"(?m)^[ \t]*ix:cached-tool-evidence:v1[ \t]*(?:\r?\n)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex LegacyToolHeadingBulletRegex = new(
        @"^(?<indent>\s*)-\s+(?<tool>[a-z0-9_.-]+):\s*(?<heading>#{2,6}\s+[^\r\n]+)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex LegacyToolSlugHeadingRegex = new(
        @"^(?<indent>\s*)####\s+(?<tool>[a-z0-9_.-]+)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex StandaloneSingleHashSeparatorRegex = new(
        @"^\s*#\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex StandaloneHashSeparatorBeforeHeadingSignalRegex = new(
        @"(?ms)^\s*#\s*$\s*^(?:\s*#{2,6}\s+\S.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex BrokenTwoLineStrongLeadInRegex = new(
        @"^(?<indent>\s*)\*\*(?<label>Result)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TrailingDanglingStrongMetricTokenRegex = new(
        @"(?<token>[\p{L}\p{N}_./:-]+)\*{4}(?<tail>\s*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OveropenedMetricValueStrongRegex = new(
        @"^(?<prefix>\s*(?:-\s+|\d+\.\s+)[^\r\n*]+?\s)\*{4,}(?<value>[^\s*\r\n][^*\r\n]*?)\*{2}(?<tail>\s*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AdjacentMetricStrongValueRegex = new(
        @"^(?<prefix>\s*(?:-\s+|\d+\.\s+)[^\r\n*]+?\s)\*\*(?<first>[^*\r\n]+)\*\*\*{2}(?<second>[^\s*\r\n][^*\r\n]*?)\*{2}(?<tail>\s*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MissingTrailingStrongMetricCloseRegex = new(
        @"^(?<prefix>\s*(?:-\s+|\d+\.\s+)[^\r\n*]+?\s)\*\*(?<value>[^\r\n*][^\r\n]*?)(?<!\*)\*(?<tail>\s*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OrderedListLeadRegex = new(
        @"^\d+[.)]\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

        normalized = StripInternalTransportMarkers(normalized);
        normalized = UpgradeLegacyVisualFences(normalized);
        normalized = UpgradeLegacyIndentedNetworkBlocks(normalized);
        normalized = NormalizeLegacyToolHeadingArtifacts(normalized);
        normalized = RemoveStandaloneHashSeparatorsBeforeHeadings(normalized);

        return ApplyTransformOutsideFencedCodeBlocks(normalized, static segment => {
            var protectedInlineCode = ProtectInlineCodeSpans(segment, out var codeSpans, out var tokenPrefix);
            var value = protectedInlineCode;
            value = ZeroWidthWhitespaceRegex.Replace(value, string.Empty);
            value = RepairBrokenTwoLineStrongLeadIns(value);
            value = NormalizeWithOfficeImoInputNormalizer(value);
            value = RemoveStandaloneHashSeparatorsBeforeHeadings(value);
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
            value = RepairMalformedMetricValueStrongRuns(value);
            value = RepairDanglingTrailingStrongMetricClosers(value);
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

        normalized = StripInternalTransportMarkers(normalized);
        normalized = UpgradeLegacyVisualFences(normalized);
        normalized = NormalizeLegacyToolHeadingArtifacts(normalized);

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
        if (normalized.Length == 0 || !RequiresLegacyTranscriptRepair(normalized)) {
            return false;
        }

        var repaired = NormalizeForRendering(normalized);
        if (repaired == normalized) {
            return false;
        }

        normalized = repaired;
        return true;
    }

}
