using System;
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
            if (MentionsWorkflowPath(line) && IsWorkflowFindingLine(line)) {
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

        return ReviewBlockerSectionSanitizer.RemoveMatchingItemsFromMergeBlockerSections(reviewBody, settings,
            IsOpenWorkflowChecklistItem);
    }

    private static bool IsOpenWorkflowChecklistItem(string line) =>
        line.StartsWith("- [ ]", StringComparison.Ordinal) &&
        MentionsWorkflowPath(line);

    private static bool IsWorkflowFindingLine(string line) {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("- [ ]", StringComparison.Ordinal) ||
               trimmed.StartsWith("- [x]", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("- [warning]", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("- [error]", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("- [critical]", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MentionsWorkflowPath(string line) {
        const string marker = ".github/workflows/";
        var index = 0;
        while ((index = line.IndexOf(marker, index, StringComparison.OrdinalIgnoreCase)) >= 0) {
            var end = index;
            while (end < line.Length && !IsWorkflowPathTerminator(line[end])) {
                end++;
            }

            var token = line.Substring(index, end - index).TrimEnd('.', ',', ';', ':', '!', '?');
            if (token.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
                token.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            index = end + 1;
        }

        return false;
    }

    private static bool IsWorkflowPathTerminator(char value) =>
        char.IsWhiteSpace(value) ||
        value == '`' ||
        value == '\'' ||
        value == '"' ||
        value == ')' ||
        value == ']' ||
        value == '}';
}
