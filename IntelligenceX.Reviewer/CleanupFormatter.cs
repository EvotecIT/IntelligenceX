using System;
using System.Text;

namespace IntelligenceX.Reviewer;

internal static class CleanupFormatter {
    public const string SummaryMarker = "<!-- intelligencex-cleanup -->";

    public static string BuildSuggestionComment(PullRequestContext context, CleanupResult result) {
        var builder = new StringBuilder();
        builder.AppendLine(SummaryMarker);
        builder.AppendLine("## IntelligenceX Cleanup");
        builder.AppendLine();
        builder.AppendLine("Suggested PR metadata cleanup based on repository policy.");
        builder.AppendLine();
        if (!string.IsNullOrWhiteSpace(result.Title)) {
            builder.AppendLine("**Proposed title**");
            builder.AppendLine();
            builder.AppendLine(result.Title.Trim());
            builder.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(result.Body)) {
            builder.AppendLine("**Proposed body**");
            builder.AppendLine();
            AppendBodyBlock(builder, result.Body.Trim());
            builder.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(result.Notes)) {
            builder.AppendLine("**Notes**");
            builder.AppendLine();
            builder.AppendLine(result.Notes.Trim());
            builder.AppendLine();
        }
        builder.AppendLine($"Confidence: {result.Confidence:0.00}");
        return builder.ToString().TrimEnd();
    }

    public static string BuildEditComment(PullRequestContext context, CleanupResult result) {
        var builder = new StringBuilder();
        builder.AppendLine(SummaryMarker);
        builder.AppendLine("## IntelligenceX Cleanup");
        builder.AppendLine();
        builder.AppendLine("Applied PR metadata cleanup based on repository policy.");
        if (!string.IsNullOrWhiteSpace(result.Notes)) {
            builder.AppendLine();
            builder.AppendLine("**Notes**");
            builder.AppendLine();
            builder.AppendLine(result.Notes.Trim());
        }
        builder.AppendLine();
        builder.AppendLine($"Confidence: {result.Confidence:0.00}");
        return builder.ToString().TrimEnd();
    }

    private static void AppendBodyBlock(StringBuilder builder, string body) {
        if (body.Contains("```", StringComparison.Ordinal)) {
            foreach (var line in body.Replace("\r", "").Split('\n')) {
                builder.AppendLine("    " + line);
            }
            return;
        }
        builder.AppendLine("```markdown");
        builder.AppendLine(body);
        builder.AppendLine("```");
    }
}
