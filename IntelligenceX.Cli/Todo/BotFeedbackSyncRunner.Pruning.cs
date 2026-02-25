using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace IntelligenceX.Cli.Todo;

internal static partial class BotFeedbackSyncRunner {
    private const string DetailsOpenTag = "<details>";
    private const string DetailsCloseTag = "</details>";
    private const string SummaryOpenTag = "<summary>";
    private const string SummaryCloseTag = "</summary>";

    private static readonly Regex PrSummaryNumberPattern = new(
        @"PR\s*#\s*(?<number>\d+)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string RemovePrBlocksNotInSet(string section, IReadOnlySet<int> keepPrNumbers, out bool changed) {
        if (string.IsNullOrEmpty(section)) {
            changed = false;
            return section;
        }

        var changedLocal = false;
        var sb = new StringBuilder(section.Length);
        var cursor = 0;
        while (cursor < section.Length) {
            var detailsStart = section.IndexOf(DetailsOpenTag, cursor, StringComparison.OrdinalIgnoreCase);
            if (detailsStart < 0) {
                sb.Append(section, cursor, section.Length - cursor);
                break;
            }

            sb.Append(section, cursor, detailsStart - cursor);

            if (!TryFindDetailsBlockEnd(section, detailsStart, out var detailsEndExclusive)) {
                // Malformed block: keep the remaining text as-is.
                sb.Append(section, detailsStart, section.Length - detailsStart);
                break;
            }

            var blockEndExclusive = ConsumeFollowingLineBreak(section, detailsEndExclusive);
            if (TryReadPrNumberFromDetailsBlock(section, detailsStart, detailsEndExclusive, out var prNumber) &&
                !keepPrNumbers.Contains(prNumber)) {
                changedLocal = true;
            } else {
                sb.Append(section, detailsStart, blockEndExclusive - detailsStart);
            }

            cursor = blockEndExclusive;
        }

        changed = changedLocal;
        return changedLocal ? sb.ToString() : section;
    }

    private static int ConsumeFollowingLineBreak(string text, int index) {
        var cursor = index;
        while (cursor < text.Length && (text[cursor] == ' ' || text[cursor] == '\t')) {
            cursor++;
        }

        if (cursor < text.Length && text[cursor] == '\r') {
            cursor++;
            if (cursor < text.Length && text[cursor] == '\n') {
                cursor++;
            }
            return cursor;
        }

        if (cursor < text.Length && text[cursor] == '\n') {
            cursor++;
        }

        return cursor;
    }

    private static bool TryFindDetailsBlockEnd(string text, int detailsStart, out int detailsEndExclusive) {
        detailsEndExclusive = -1;
        if (detailsStart < 0 || detailsStart >= text.Length) {
            return false;
        }

        var cursor = detailsStart + DetailsOpenTag.Length;
        var depth = 1;
        while (cursor < text.Length) {
            var nextOpen = text.IndexOf(DetailsOpenTag, cursor, StringComparison.OrdinalIgnoreCase);
            var nextClose = text.IndexOf(DetailsCloseTag, cursor, StringComparison.OrdinalIgnoreCase);
            if (nextClose < 0) {
                return false;
            }

            if (nextOpen >= 0 && nextOpen < nextClose) {
                depth++;
                cursor = nextOpen + DetailsOpenTag.Length;
                continue;
            }

            depth--;
            cursor = nextClose + DetailsCloseTag.Length;
            if (depth == 0) {
                detailsEndExclusive = cursor;
                return true;
            }
        }

        return false;
    }

    private static bool TryReadPrNumberFromDetailsBlock(string section, int detailsStart, int detailsEndExclusive, out int prNumber) {
        prNumber = 0;
        var summaryStart = section.IndexOf(SummaryOpenTag, detailsStart, StringComparison.OrdinalIgnoreCase);
        if (summaryStart < 0 || summaryStart >= detailsEndExclusive) {
            return false;
        }

        summaryStart += SummaryOpenTag.Length;
        var summaryEnd = section.IndexOf(SummaryCloseTag, summaryStart, StringComparison.OrdinalIgnoreCase);
        if (summaryEnd < 0 || summaryEnd > detailsEndExclusive) {
            return false;
        }

        var summaryText = section.Substring(summaryStart, summaryEnd - summaryStart);
        var match = PrSummaryNumberPattern.Match(summaryText);
        return match.Success && int.TryParse(match.Groups["number"].Value, out prNumber);
    }
}
