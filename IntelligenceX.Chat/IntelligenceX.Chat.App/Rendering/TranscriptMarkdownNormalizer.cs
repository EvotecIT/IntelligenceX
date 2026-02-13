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

    public static string NormalizeForRendering(string? text) {
        var normalized = text ?? string.Empty;
        if (normalized.Length == 0) {
            return string.Empty;
        }

        normalized = EmojiWordJoinRegex.Replace(normalized, "$1 ");
        normalized = NumberedChoiceJoinRegex.Replace(normalized, "$1 ");
        normalized = LetterToNumberedChoiceJoinRegex.Replace(normalized, " ");
        normalized = TightBoldValueRegex.Replace(normalized, " ");
        normalized = MissingSpaceBeforeBoldMetricRegex.Replace(normalized, "- ");
        normalized = SingleStarMetricRegex.Replace(normalized, "- **");
        normalized = CollapsedBulletRegex.Replace(normalized, "\n- **");
        normalized = ExpandCollapsedMetricLines(normalized);
        normalized = ConvertLegacyMetricMarkdown(normalized);
        return normalized;
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
}
