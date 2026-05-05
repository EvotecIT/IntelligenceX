using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IntelligenceX.Reviewer;

internal static class WorkflowGuardSanitizer {
    public static string RemoveExcludedWorkflowReferences(string? reviewBody, bool workflowGuardActive) {
        if (string.IsNullOrWhiteSpace(reviewBody) || !workflowGuardActive) {
            return reviewBody ?? string.Empty;
        }

        using var reader = new StringReader(reviewBody);
        var output = new StringBuilder();
        string? line;
        while ((line = reader.ReadLine()) is not null) {
            if (MentionsWorkflowPath(line)) {
                continue;
            }
            output.AppendLine(line);
        }

        return output.ToString().TrimEnd();
    }

    public static string RemoveExcludedWorkflowBlockers(string? reviewBody, ReviewSettings settings, bool workflowGuardActive) {
        if (string.IsNullOrWhiteSpace(reviewBody) || !workflowGuardActive) {
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
        var removedWorkflowChecklistBlocker = false;
        var sectionHasOpenItem = false;
        var sectionHasCleanMarker = false;
        string? line;

        void FlushSection() {
            if (!inMergeBlockerSection || sectionLines.Count == 0) {
                return;
            }

            if (removedWorkflowChecklistBlocker && !sectionHasOpenItem && !sectionHasCleanMarker) {
                sectionLines.Add("None.");
            }

            foreach (var sectionLine in sectionLines) {
                output.AppendLine(sectionLine);
            }

            sectionLines.Clear();
            removedWorkflowChecklistBlocker = false;
            sectionHasOpenItem = false;
            sectionHasCleanMarker = false;
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

            if (inMergeBlockerSection && IsCleanMarker(trimmed)) {
                sectionHasCleanMarker = true;
            } else if (inMergeBlockerSection && IsOpenWorkflowChecklistItem(trimmed)) {
                removedWorkflowChecklistBlocker = true;
                continue;
            } else if (inMergeBlockerSection && IsOpenListItem(trimmed)) {
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

    private static bool IsOpenWorkflowChecklistItem(string line) =>
        line.StartsWith("- [ ]", StringComparison.Ordinal) &&
        MentionsWorkflowPath(line);

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

    private static bool IsCleanMarker(string line) {
        if (string.IsNullOrWhiteSpace(line)) {
            return false;
        }

        var normalized = ReviewBlockerSectionSanitizer.NormalizeBodyText(line);
        return string.Equals(normalized, "none", StringComparison.OrdinalIgnoreCase);
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

    private static string NormalizeHeader(string header) {
        var trimmed = header.Trim();
        while (trimmed.StartsWith("#", StringComparison.Ordinal)) {
            trimmed = trimmed.Substring(1).TrimStart();
        }

        return ReviewBlockerSectionSanitizer.NormalizeBodyText(trimmed);
    }

    private static bool MentionsWorkflowPath(string line) =>
        line.Contains(".github/workflows/", StringComparison.OrdinalIgnoreCase);
}
