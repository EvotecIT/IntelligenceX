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

        return ReviewBlockerSectionSanitizer.RemoveMatchingOpenChecklistItems(reviewBody, settings, MentionsWorkflowPath);
    }

    private static bool MentionsWorkflowPath(string line) =>
        line.Contains(".github/workflows/", StringComparison.OrdinalIgnoreCase);
}
