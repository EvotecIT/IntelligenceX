using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IntelligenceX.Reviewer;

internal static class PromptBuilder {
    public static string Build(PullRequestContext context, IReadOnlyList<PullRequestFile> files, ReviewSettings settings,
        ReviewContextExtras? extras = null, bool inlineSupported = false) {
        var template = ResolveTemplate(settings);
        var profileBlock = string.IsNullOrWhiteSpace(settings.Profile) ? string.Empty : $"Profile: {settings.Profile}\n";
        var strictnessBlock = string.IsNullOrWhiteSpace(settings.Strictness) ? string.Empty : $"Strictness: {settings.Strictness}\n";
        var styleBlock = string.IsNullOrWhiteSpace(settings.Style) ? string.Empty : $"Style: {settings.Style}\n";
        var toneBlock = string.IsNullOrWhiteSpace(settings.Tone) ? string.Empty : $"Tone: {settings.Tone}\n";
        var outputStyleBlock = string.IsNullOrWhiteSpace(settings.OutputStyle) ? string.Empty : $"Output style: {settings.OutputStyle}\n";
        var focusBlock = settings.Focus.Count == 0 ? string.Empty : $"Focus areas: {string.Join(", ", settings.Focus)}\n";
        var personaBlock = string.IsNullOrWhiteSpace(settings.Persona) ? string.Empty : $"Persona: {settings.Persona}\n";
        var notesBlock = string.IsNullOrWhiteSpace(settings.Notes) ? string.Empty : $"Additional guidance: {settings.Notes}\n";
        var severityBlock = string.IsNullOrWhiteSpace(settings.SeverityThreshold)
            ? string.Empty
            : $"Only include issues with severity >= {settings.SeverityThreshold}.\n";
        var nextStepsSection = settings.IncludeNextSteps ? "- Next Steps 🚀\n" : string.Empty;

        var tokens = new Dictionary<string, string> {
            ["ProfileBlock"] = profileBlock,
            ["StrictnessBlock"] = strictnessBlock,
            ["StyleBlock"] = styleBlock,
            ["ToneBlock"] = toneBlock,
            ["OutputStyleBlock"] = outputStyleBlock,
            ["FocusBlock"] = focusBlock,
            ["PersonaBlock"] = personaBlock,
            ["NotesBlock"] = notesBlock,
            ["SeverityBlock"] = severityBlock,
            ["Length"] = settings.Length.ToString().ToLowerInvariant(),
            ["Mode"] = settings.Mode,
            ["MaxInlineComments"] = settings.MaxInlineComments.ToString(),
            ["InlineSupported"] = inlineSupported ? "true" : "false",
            ["NextStepsSection"] = nextStepsSection,
            ["Title"] = context.Title ?? string.Empty,
            ["Body"] = string.IsNullOrWhiteSpace(context.Body) ? "<no description>" : context.Body,
            ["Files"] = BuildFilesBlock(files),
            ["IssueCommentsSection"] = extras?.IssueCommentsSection ?? string.Empty,
            ["ReviewCommentsSection"] = extras?.ReviewCommentsSection ?? string.Empty,
            ["ReviewThreadsSection"] = extras?.ReviewThreadsSection ?? string.Empty,
            ["RelatedPrsSection"] = extras?.RelatedPrsSection ?? string.Empty
        };

        return TemplateRenderer.Render(template, tokens);
    }

    private static string ResolveTemplate(ReviewSettings settings) {
        if (!string.IsNullOrWhiteSpace(settings.PromptTemplate)) {
            return settings.PromptTemplate!;
        }
        if (!string.IsNullOrWhiteSpace(settings.PromptTemplatePath)) {
            return File.ReadAllText(settings.PromptTemplatePath!);
        }
        if (!string.IsNullOrWhiteSpace(settings.OutputStyle)) {
            var key = settings.OutputStyle.Trim().ToLowerInvariant();
            if (key is "claude" or "claude-like" or "claude_style" or "claude-style") {
                return TemplateLoader.Load("ReviewPrompt.Claude.md");
            }
        }
        var name = settings.Length switch {
            ReviewLength.Short => "ReviewPrompt.Short.md",
            ReviewLength.Medium => "ReviewPrompt.Medium.md",
            _ => "ReviewPrompt.Long.md"
        };
        return TemplateLoader.Load(name);
    }

    private static string BuildFilesBlock(IReadOnlyList<PullRequestFile> files) {
        var sb = new StringBuilder();
        foreach (var file in files) {
            sb.AppendLine($"File: {file.Filename} ({file.Status})");
            if (!string.IsNullOrWhiteSpace(file.Patch)) {
                sb.AppendLine("Patch:");
                sb.AppendLine(file.Patch);
            } else {
                sb.AppendLine("Patch: <no diff available>");
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}
