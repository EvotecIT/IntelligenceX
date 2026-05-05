using System;

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

        return ReviewBlockerSectionSanitizer.RemoveMatchingOpenItems(reviewBody, settings,
            IsStaleThreadResolutionItem);
    }

    private static bool IsStaleThreadResolutionItem(string line) {
        var normalized = ReviewBlockerSectionSanitizer.NormalizeBodyText(line);
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
}
