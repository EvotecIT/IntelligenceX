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

    private static bool MentionsWorkflowPath(string line) =>
        line.Contains(".github/workflows/", StringComparison.OrdinalIgnoreCase);
}
