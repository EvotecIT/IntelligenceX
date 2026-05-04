using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IntelligenceX.Reviewer;

internal static class ReviewThreadBlockerSanitizer {
    public static string RemoveResolvedThreadBlockers(string? reviewBody, ReviewSettings settings,
        ReviewHistorySnapshot? history, bool reviewThreadsUnavailable) {
        if (string.IsNullOrWhiteSpace(reviewBody) ||
            reviewThreadsUnavailable ||
            history?.ThreadSnapshot is null ||
            history.ThreadSnapshot.ActiveCount != 0) {
            return reviewBody ?? string.Empty;
        }

        var body = ReviewFormatter.NormalizeSectionLayout(reviewBody);
        var sections = settings.ResolveMergeBlockerSections();
        if (sections.Count == 0) {
            sections = new[] { "todo list", "critical issues" };
        }

        using var reader = new StringReader(body);
        var output = new StringBuilder();
        var sectionLines = new List<string>();
        var inMergeBlockerSection = false;
        var removedThreadBlocker = false;
        var sectionHasOpenItem = false;
        string? line;

        void FlushSection() {
            if (!inMergeBlockerSection || sectionLines.Count == 0) {
                return;
            }

            if (removedThreadBlocker && !sectionHasOpenItem) {
                sectionLines.Add("- none.");
            }

            foreach (var sectionLine in sectionLines) {
                output.AppendLine(sectionLine);
            }

            sectionLines.Clear();
            removedThreadBlocker = false;
            sectionHasOpenItem = false;
        }

        while ((line = reader.ReadLine()) is not null) {
            var trimmed = line.Trim();
            if (IsMarkdownHeading(trimmed)) {
                FlushSection();
                sectionLines.Clear();
                inMergeBlockerSection = IsConfiguredSection(trimmed, sections);
                if (inMergeBlockerSection) {
                    sectionLines.Add(line);
                } else {
                    output.AppendLine(line);
                }
                continue;
            }

            if (inMergeBlockerSection && IsStaleThreadResolutionItem(trimmed)) {
                removedThreadBlocker = true;
                continue;
            }

            if (inMergeBlockerSection && IsOpenListItem(trimmed)) {
                sectionHasOpenItem = true;
            }

            if (inMergeBlockerSection) {
                sectionLines.Add(line);
                continue;
            }

            output.AppendLine(line);
        }

        FlushSection();
        return output.ToString().TrimEnd();
    }

    private static bool IsConfiguredSection(string header, IReadOnlyList<string> sections) {
        var normalizedHeader = NormalizeHeader(header);
        foreach (var section in sections) {
            var normalizedSection = NormalizeHeader(section);
            if (normalizedHeader.Equals(normalizedSection, StringComparison.OrdinalIgnoreCase) ||
                normalizedHeader.StartsWith(normalizedSection + " ", StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static bool IsMarkdownHeading(string line) {
        if (string.IsNullOrWhiteSpace(line) || line[0] != '#') {
            return false;
        }

        var index = 0;
        while (index < line.Length && line[index] == '#') {
            index++;
        }

        return index > 0 && index < line.Length && char.IsWhiteSpace(line[index]);
    }

    private static bool IsOpenListItem(string line) {
        if (string.IsNullOrWhiteSpace(line)) {
            return false;
        }

        if (line.StartsWith("- [x]", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return line.StartsWith("- [ ]", StringComparison.Ordinal) ||
               line.StartsWith("-", StringComparison.Ordinal);
    }

    private static bool IsStaleThreadResolutionItem(string line) {
        if (!IsOpenListItem(line)) {
            return false;
        }

        var normalized = NormalizeBodyText(line);
        var mentionsThreadState =
            normalized.Contains("review thread", StringComparison.Ordinal) ||
            normalized.Contains("review threads", StringComparison.Ordinal) ||
            normalized.Contains("unresolved thread", StringComparison.Ordinal) ||
            normalized.Contains("unresolved threads", StringComparison.Ordinal) ||
            normalized.Contains("active thread", StringComparison.Ordinal) ||
            normalized.Contains("active threads", StringComparison.Ordinal) ||
            normalized.Contains("review conversation", StringComparison.Ordinal) ||
            normalized.Contains("conversation resolution", StringComparison.Ordinal);
        if (!mentionsThreadState) {
            return false;
        }

        return normalized.Contains("resolve", StringComparison.Ordinal) ||
               normalized.Contains("clear", StringComparison.Ordinal) ||
               normalized.Contains("still active", StringComparison.Ordinal) ||
               normalized.Contains("remaining active", StringComparison.Ordinal);
    }

    private static string NormalizeHeader(string header) {
        var trimmed = header.Trim();
        while (trimmed.StartsWith("#", StringComparison.Ordinal)) {
            trimmed = trimmed.Substring(1).TrimStart();
        }

        return NormalizeBodyText(trimmed);
    }

    private static string NormalizeBodyText(string value) {
        var sb = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var ch in value.ToLowerInvariant()) {
            if (char.IsLetterOrDigit(ch)) {
                if (pendingSpace && sb.Length > 0) {
                    sb.Append(' ');
                }
                sb.Append(ch);
                pendingSpace = false;
            } else {
                pendingSpace = true;
            }
        }

        return sb.ToString().Trim();
    }
}
