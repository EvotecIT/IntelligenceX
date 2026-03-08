using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

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

    private static readonly Lazy<OfficeImoInputNormalizationBridge?> OfficeImoInputNormalizationBridgeLazy =
        new(CreateOfficeImoInputNormalizationBridge);
    private static readonly string[] OfficeImoInputNormalizationPropertyNames = [
        "NormalizeLooseStrongDelimiters",
        "NormalizeTightStrongBoundaries",
        "NormalizeOrderedListMarkerSpacing",
        "NormalizeOrderedListParenMarkers",
        "NormalizeOrderedListCaretArtifacts",
        "NormalizeTightParentheticalSpacing",
        "NormalizeNestedStrongDelimiters",
        "NormalizeTightArrowStrongBoundaries",
        "NormalizeTightColonSpacing"
    ];

    public static string NormalizeForRendering(string? text) {
        var normalized = text ?? string.Empty;
        if (normalized.Length == 0) {
            return string.Empty;
        }

        normalized = StripInternalTransportMarkers(normalized);
        normalized = UpgradeLegacyVisualFences(normalized);
        normalized = NormalizeLegacyToolHeadingArtifacts(normalized);

        return ApplyTransformOutsideFencedCodeBlocks(normalized, static segment => {
            var protectedInlineCode = ProtectInlineCodeSpans(segment, out var codeSpans, out var tokenPrefix);
            var value = protectedInlineCode;
            value = ZeroWidthWhitespaceRegex.Replace(value, string.Empty);
            value = NormalizeWithOfficeImoInputNormalizer(value);
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

    private static bool RequiresLegacyTranscriptRepair(string text) {
        if (string.IsNullOrEmpty(text)) {
            return false;
        }

        return LegacyRepairSignalRegex.IsMatch(text)
               || text.Contains("****", StringComparison.Ordinal)
               || text.IndexOf("ix:cached-tool-evidence:v1", StringComparison.OrdinalIgnoreCase) >= 0
               || LegacyToolHeadingBulletRegex.IsMatch(text)
               || LegacyToolSlugHeadingRegex.IsMatch(text)
               || ContainsLegacyJsonVisualFenceCandidate(text);
    }

    private static string StripInternalTransportMarkers(string text) {
        if (string.IsNullOrEmpty(text)
            || text.IndexOf("ix:cached-tool-evidence:v1", StringComparison.OrdinalIgnoreCase) < 0) {
            return text;
        }

        return CachedToolEvidenceMarkerLineRegex.Replace(text, string.Empty);
    }

    private static string NormalizeLegacyToolHeadingArtifacts(string text) {
        if (string.IsNullOrEmpty(text)) {
            return text;
        }

        var hasCrLf = text.Contains("\r\n", StringComparison.Ordinal);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var rewritten = new List<string>(lines.Length);
        var changed = false;

        for (var i = 0; i < lines.Length; i++) {
            var current = lines[i] ?? string.Empty;
            var bulletMatch = LegacyToolHeadingBulletRegex.Match(current);
            if (bulletMatch.Success) {
                rewritten.Add(bulletMatch.Groups["heading"].Value.Trim());
                changed = true;
                continue;
            }

            var slugMatch = LegacyToolSlugHeadingRegex.Match(current);
            if (slugMatch.Success && TryFindNextNonEmptyLine(lines, i + 1, out var nextIndex)) {
                var next = lines[nextIndex] ?? string.Empty;
                if (IsMarkdownHeadingLine(next)) {
                    changed = true;
                    continue;
                }
            }

            rewritten.Add(current);
        }

        if (!changed) {
            return text;
        }

        var rebuilt = string.Join("\n", rewritten);
        return hasCrLf ? rebuilt.Replace("\n", "\r\n", StringComparison.Ordinal) : rebuilt;
    }

    private static bool IsMarkdownHeadingLine(string line) {
        var trimmed = line.TrimStart();
        if (trimmed.Length < 4 || trimmed[0] != '#') {
            return false;
        }

        var depth = 0;
        while (depth < trimmed.Length && trimmed[depth] == '#') {
            depth++;
        }

        return depth is >= 2 and <= 6
               && depth < trimmed.Length
               && char.IsWhiteSpace(trimmed[depth]);
    }

    private static bool TryFindNextNonEmptyLine(string[] lines, int startIndex, out int index) {
        for (var i = startIndex; i < lines.Length; i++) {
            if (!string.IsNullOrWhiteSpace(lines[i])) {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
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
