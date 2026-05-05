using System;
using System.Text;

namespace IntelligenceX.Reviewer;

internal static class ReviewStateBuilder {
    public static string BuildCommentBlock(string? reviewBody, ReviewSettings settings, bool reviewFailed) {
        var state = Build(reviewBody, settings, reviewFailed);
        var sb = new StringBuilder();
        sb.AppendLine("## Review State 🧭");
        sb.AppendLine();
        sb.AppendLine("| Recommendation | Merge blockers | Evidence |");
        sb.AppendLine("| --- | --- | --- |");
        sb.AppendLine($"| {EscapeTableCell(state.RecommendationLabel)} | {EscapeTableCell(state.MergeBlockerLabel)} | {EscapeTableCell(state.Evidence)} |");
        return sb.ToString().TrimEnd();
    }

    internal static ReviewState Build(string? reviewBody, ReviewSettings settings, bool reviewFailed) {
        if (reviewFailed) {
            return new ReviewState("manual-review", "Manual review", "unknown", "review provider failed or returned a failure body");
        }

        var posture = ReviewMergeBlockerPosture.Build(reviewBody, settings, settings.History.MaxItems);

        if (posture.FindingsParseIncomplete) {
            return new ReviewState("manual-review", "Manual review",
                FormatBlockerCount(posture.OpenFindingCount, posture.HasMergeBlockers),
                "merge-blocker section contained lines that could not be normalized");
        }

        if (posture.MissingRequiredSections) {
            return new ReviewState("manual-review", "Manual review", "unknown",
                "configured merge-blocker sections were missing");
        }

        if (posture.HasMergeBlockers && posture.OpenFindingCount == 0) {
            return new ReviewState("manual-review", "Manual review", "unknown",
                "configured merge-blocker sections were missing or could not be normalized");
        }

        if (posture.HasMergeBlockers) {
            return new ReviewState("needs-work", "Needs work",
                FormatBlockerCount(posture.OpenFindingCount, true),
                "open Todo/Critical item(s) detected");
        }

        return new ReviewState("approve", "Approve", "none detected",
            "configured merge-blocker sections parsed with no open items");
    }

    private static string FormatBlockerCount(int openCount, bool hasMergeBlockers) {
        if (openCount > 0) {
            return openCount == 1 ? "1 open item" : $"{openCount} open items";
        }

        return hasMergeBlockers ? "present" : "none detected";
    }

    private static string EscapeTableCell(string value) {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }
}

internal readonly record struct ReviewState(string Recommendation, string RecommendationLabel, string MergeBlockerLabel,
    string Evidence);
