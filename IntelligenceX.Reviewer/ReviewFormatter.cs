using System.Text;

namespace IntelligenceX.Reviewer;

internal static class ReviewFormatter {
    public const string SummaryMarker = "<!-- intelligencex:summary -->";

    public static string BuildComment(PullRequestContext context, string reviewBody, ReviewSettings settings, bool inlineSupported) {
        var sb = new StringBuilder();
        sb.AppendLine(SummaryMarker);
        sb.AppendLine("## IntelligenceX Review");
        sb.AppendLine($"Reviewing PR #{context.Number}: **{EscapeMarkdown(context.Title)}**");
        sb.AppendLine();

        if (!inlineSupported && settings.Mode != "summary") {
            sb.AppendLine("> Inline comments are not enabled yet; posting summary only.");
            sb.AppendLine();
        }

        if (string.IsNullOrWhiteSpace(reviewBody)) {
            sb.AppendLine("_No review content was produced._");
        } else {
            sb.AppendLine(reviewBody.Trim());
        }

        sb.AppendLine();
        sb.AppendLine($"_Model: {settings.Model} · Length: {settings.Length}_");
        return sb.ToString();
    }

    private static string EscapeMarkdown(string value) {
        return value.Replace("\r", "").Replace("\n", " ");
    }
}
