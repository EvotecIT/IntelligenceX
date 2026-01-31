using System.Collections.Generic;
using System.IO;

namespace IntelligenceX.Reviewer;

internal static class ReviewFormatter {
    public const string SummaryMarker = "<!-- intelligencex:summary -->";
    public const string InlineMarker = "<!-- intelligencex:inline -->";
    public const string ReviewedCommitMarker = "Reviewed commit:";
    private const string ProgressTemplateName = "ReviewProgress.md";

    public static string BuildComment(PullRequestContext context, string reviewBody, ReviewSettings settings, bool inlineSupported,
        string? autoResolveNote) {
        var inlineNote = (!inlineSupported && settings.Mode != "summary")
            ? "> Inline comments are not enabled yet; posting summary only.\n"
            : string.Empty;
        var autoResolveLine = string.IsNullOrWhiteSpace(autoResolveNote)
            ? string.Empty
            : $"> {autoResolveNote.Trim()}\n";

        var body = string.IsNullOrWhiteSpace(reviewBody)
            ? "_No review content was produced._"
            : reviewBody.Trim();

        var template = ResolveSummaryTemplate(settings);
        var tokens = new Dictionary<string, string> {
            ["SummaryMarker"] = SummaryMarker,
            ["Number"] = context.Number.ToString(),
            ["Title"] = EscapeMarkdown(context.Title),
            ["CommitLine"] = FormatCommitLine(context.HeadSha),
            ["InlineNote"] = inlineNote,
            ["AutoResolveNote"] = autoResolveLine,
            ["ReviewBody"] = body,
            ["Model"] = settings.Model,
            ["Length"] = settings.Length.ToString().ToLowerInvariant()
        };

        return TemplateRenderer.Render(template, tokens).TrimEnd();
    }

    public static string BuildProgressComment(PullRequestContext context, ReviewSettings settings, ReviewProgress progress,
        string? partialReview, bool inlineSupported) {
        var inlineNote = (!inlineSupported && settings.Mode != "summary")
            ? "> Inline comments are not enabled yet; posting summary only.\n"
            : string.Empty;

        var statusLine = string.IsNullOrWhiteSpace(progress.StatusLine)
            ? "Review in progress."
            : progress.StatusLine!;

        var preview = TrimPreview(partialReview, settings.ProgressPreviewChars);
        var preliminaryBlock = string.IsNullOrWhiteSpace(preview)
            ? "_No preliminary analysis yet._"
            : preview.Trim();

        var checklist = BuildChecklist(progress);
        var template = TemplateLoader.Load(ProgressTemplateName);
        var tokens = new Dictionary<string, string> {
            ["SummaryMarker"] = SummaryMarker,
            ["Number"] = context.Number.ToString(),
            ["Title"] = EscapeMarkdown(context.Title),
            ["InlineNote"] = inlineNote,
            ["ProgressLine"] = statusLine,
            ["Checklist"] = checklist,
            ["PreliminaryBlock"] = preliminaryBlock,
            ["Model"] = settings.Model,
            ["Length"] = settings.Length.ToString().ToLowerInvariant(),
            ["Mode"] = settings.Mode
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

    private static string FormatCommitLine(string? sha) {
        if (string.IsNullOrWhiteSpace(sha)) {
            return string.Empty;
        }
        var trimmed = sha.Trim();
        var shortSha = trimmed.Length > 7 ? trimmed.Substring(0, 7) : trimmed;
        return $"{ReviewedCommitMarker} `{shortSha}`\n";
    }

    private static string BuildChecklist(ReviewProgress progress) {
        return string.Join("\n", new[] {
            BuildChecklistLine(progress.Context, "Collect PR context"),
            BuildChecklistLine(progress.Files, "Analyze changed files"),
            BuildChecklistLine(progress.Review, "Generate review findings"),
            BuildChecklistLine(progress.Finalize, "Finalize summary")
        });
    }

    private static string BuildChecklistLine(ReviewProgressState state, string label) {
        return state switch {
            ReviewProgressState.Complete => $"- [x] {label}",
            ReviewProgressState.InProgress => $"- [ ] {label} (in progress)",
            _ => $"- [ ] {label}"
        };
    }

    private static string TrimPreview(string? value, int maxChars) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }
        var text = value.Trim();
        if (text.Length <= maxChars) {
            return text;
        }
        return text.Substring(0, maxChars) + "...";
    }
}
