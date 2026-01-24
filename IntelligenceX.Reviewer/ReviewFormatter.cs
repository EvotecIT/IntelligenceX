using System.Collections.Generic;
using System.IO;

namespace IntelligenceX.Reviewer;

internal static class ReviewFormatter {
    public const string SummaryMarker = "<!-- intelligencex:summary -->";

    public static string BuildComment(PullRequestContext context, string reviewBody, ReviewSettings settings, bool inlineSupported) {
        var inlineNote = (!inlineSupported && settings.Mode != "summary")
            ? "> Inline comments are not enabled yet; posting summary only.\n"
            : string.Empty;

        var body = string.IsNullOrWhiteSpace(reviewBody)
            ? "_No review content was produced._"
            : reviewBody.Trim();

        var template = ResolveSummaryTemplate(settings);
        var tokens = new Dictionary<string, string> {
            ["SummaryMarker"] = SummaryMarker,
            ["Number"] = context.Number.ToString(),
            ["Title"] = EscapeMarkdown(context.Title),
            ["InlineNote"] = inlineNote,
            ["ReviewBody"] = body,
            ["Model"] = settings.Model,
            ["Length"] = settings.Length.ToString().ToLowerInvariant()
        };

        return TemplateRenderer.Render(template, tokens).TrimEnd();
    }

    private static string ResolveSummaryTemplate(ReviewSettings settings) {
        if (!string.IsNullOrWhiteSpace(settings.SummaryTemplate)) {
            return settings.SummaryTemplate!;
        }
        if (!string.IsNullOrWhiteSpace(settings.SummaryTemplatePath)) {
            return File.ReadAllText(settings.SummaryTemplatePath!);
        }
        return TemplateLoader.Load("ReviewSummary.md");
    }

    private static string EscapeMarkdown(string value) {
        return value.Replace("\r", "").Replace("\n", " ");
    }
}
