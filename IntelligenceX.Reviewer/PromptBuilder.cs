using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IntelligenceX.Reviewer;

internal static class PromptBuilder {
    public static string Build(PullRequestContext context, IReadOnlyList<PullRequestFile> files, ReviewSettings settings,
        string? diffNote, ReviewContextExtras? extras = null, bool inlineSupported = false, string? previousSummary = null) {
        var template = ResolveTemplate(settings);
        var profileBlock = string.IsNullOrWhiteSpace(settings.Profile) ? string.Empty : $"Profile: {settings.Profile}\n";
        var strictnessBlock = string.IsNullOrWhiteSpace(settings.Strictness) ? string.Empty : $"Strictness: {settings.Strictness}\n";
        var styleBlock = string.IsNullOrWhiteSpace(settings.Style) ? string.Empty : $"Style: {settings.Style}\n";
        var toneBlock = string.IsNullOrWhiteSpace(settings.Tone) ? string.Empty : $"Tone: {settings.Tone}\n";
        var outputStyleBlock = string.IsNullOrWhiteSpace(settings.OutputStyle) ? string.Empty : $"Output style: {settings.OutputStyle}\n";
        var focusBlock = settings.Focus.Count == 0 ? string.Empty : $"Focus areas: {string.Join(", ", settings.Focus)}\n";
        var personaBlock = string.IsNullOrWhiteSpace(settings.Persona) ? string.Empty : $"Persona: {settings.Persona}\n";
        var notesBlock = string.IsNullOrWhiteSpace(settings.Notes) ? string.Empty : $"Additional guidance: {settings.Notes}\n";
        var mergeBlockerSectionsBlock = BuildMergeBlockerSectionsBlock(settings);
        var languageHintsBlock = LanguageHints.Build(files, settings.IncludeLanguageHints);
        var severityBlock = string.IsNullOrWhiteSpace(settings.SeverityThreshold)
            ? string.Empty
            : $"Only include issues with severity >= {settings.SeverityThreshold}.\n";
        var narrativeContractBlock = BuildNarrativeContractBlock(settings.NarrativeMode);
        var nextStepsSection = settings.IncludeNextSteps ? "- Next Steps 🚀\n" : string.Empty;
        var diffRangeBlock = string.IsNullOrWhiteSpace(diffNote) ? string.Empty : $"Diff range: {diffNote}\n";
        var summaryStabilityBlock = string.IsNullOrWhiteSpace(previousSummary)
            ? string.Empty
            : $"Previous review summary (keep wording stable unless new evidence changes the outcome):\n{previousSummary.Trim()}\n\n";

        var tokens = new Dictionary<string, string> {
            ["ProfileBlock"] = profileBlock,
            ["StrictnessBlock"] = strictnessBlock,
            ["StyleBlock"] = styleBlock,
            ["ToneBlock"] = toneBlock,
            ["OutputStyleBlock"] = outputStyleBlock,
            ["FocusBlock"] = focusBlock,
            ["PersonaBlock"] = personaBlock,
            ["NotesBlock"] = notesBlock,
            ["MergeBlockerSectionsBlock"] = mergeBlockerSectionsBlock,
            ["LanguageHintsBlock"] = languageHintsBlock,
            ["SeverityBlock"] = severityBlock,
            ["NarrativeContractBlock"] = narrativeContractBlock,
            ["Length"] = settings.Length.ToString().ToLowerInvariant(),
            ["Mode"] = settings.Mode,
            ["MaxInlineComments"] = settings.MaxInlineComments.ToString(),
            ["InlineSupported"] = inlineSupported ? "true" : "false",
            ["DiffRangeBlock"] = diffRangeBlock,
            ["SummaryStabilityBlock"] = summaryStabilityBlock,
            ["NextStepsSection"] = nextStepsSection,
            ["Title"] = context.Title ?? string.Empty,
            ["Body"] = string.IsNullOrWhiteSpace(context.Body) ? "<no description>" : context.Body,
            ["Files"] = BuildFilesBlock(files),
            ["ReviewHistorySection"] = extras?.ReviewHistorySection ?? string.Empty,
            ["CiContextSection"] = extras?.CiContextSection ?? string.Empty,
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
        if (ReviewSettings.IsCompactOutputStyle(settings.OutputStyle)) {
            return TemplateLoader.Load("ReviewPrompt.Compact.md");
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

    private static string BuildNarrativeContractBlock(ReviewNarrativeMode narrativeMode) {
        return narrativeMode switch {
            ReviewNarrativeMode.Freedom => """
Use a natural reviewer voice. You may use concise bullets, short paragraphs, or tables where helpful.
You do not need to force a fixed phrasing pattern for every item.
Still keep outcomes explicit: what is wrong, impact (when not obvious), and what change is required to unblock.
Avoid chain-of-thought.
""",
            _ => """
For each issue or todo item, include a one-sentence rationale (why it matters).
Keep wording crisp and deterministic to make merge-blockers easy to action.
Avoid chain-of-thought.
"""
        };
    }

    private static string BuildMergeBlockerSectionsBlock(ReviewSettings settings) {
        var sections = settings.ResolveMergeBlockerSections();
        if (sections.Count == 0) {
            return string.Empty;
        }
        return
            $"Merge-blocker sections: {string.Join(", ", sections)}.\n" +
            "Put merge-blocking findings only under those sections.\n";
    }
}
