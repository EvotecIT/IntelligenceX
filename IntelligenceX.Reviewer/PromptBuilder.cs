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
        var languageHintsBlock = BuildLanguageHintsBlock(files);
        var personaBlock = string.IsNullOrWhiteSpace(settings.Persona) ? string.Empty : $"Persona: {settings.Persona}\n";
        var notesBlock = string.IsNullOrWhiteSpace(settings.Notes) ? string.Empty : $"Additional guidance: {settings.Notes}\n";
        var languageHintsBlock = LanguageHints.Build(files, settings.IncludeLanguageHints);
        var severityBlock = string.IsNullOrWhiteSpace(settings.SeverityThreshold)
            ? string.Empty
            : $"Only include issues with severity >= {settings.SeverityThreshold}.\n";
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
            ["LanguageHintsBlock"] = languageHintsBlock,
            ["PersonaBlock"] = personaBlock,
            ["NotesBlock"] = notesBlock,
            ["LanguageHintsBlock"] = languageHintsBlock,
            ["SeverityBlock"] = severityBlock,
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

    private static string BuildLanguageHintsBlock(IReadOnlyList<PullRequestFile> files) {
        if (files.Count == 0) {
            return string.Empty;
        }
        var languages = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files) {
            var language = GetLanguageHint(file.Filename);
            if (!string.IsNullOrWhiteSpace(language)) {
                languages.Add(language);
            }
        }
        if (languages.Count == 0) {
            return string.Empty;
        }
        return $"Languages detected: {string.Join(", ", languages)}\n";
    }

    private static string? GetLanguageHint(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return null;
        }
        var fileName = Path.GetFileName(path);
        if (string.Equals(fileName, "Dockerfile", StringComparison.OrdinalIgnoreCase)) {
            return "Dockerfile";
        }
        if (string.Equals(fileName, "Makefile", StringComparison.OrdinalIgnoreCase)) {
            return "Makefile";
        }
        var ext = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(ext)) {
            return null;
        }
        ext = ext.ToLowerInvariant();
        return ext switch {
            ".cs" or ".csx" => "C#",
            ".fs" or ".fsx" => "F#",
            ".vb" => "VB.NET",
            ".ps1" or ".psm1" or ".psd1" => "PowerShell",
            ".js" or ".mjs" or ".cjs" => "JavaScript",
            ".ts" or ".tsx" => "TypeScript",
            ".jsx" => "JavaScript",
            ".json" => "JSON",
            ".yaml" or ".yml" => "YAML",
            ".md" => "Markdown",
            ".toml" => "TOML",
            ".ini" => "INI",
            ".xml" or ".config" => "XML",
            ".html" or ".htm" => "HTML",
            ".css" or ".scss" or ".sass" or ".less" => "CSS",
            ".sql" => "SQL",
            ".py" => "Python",
            ".rb" => "Ruby",
            ".php" => "PHP",
            ".java" => "Java",
            ".kt" or ".kts" => "Kotlin",
            ".swift" => "Swift",
            ".go" => "Go",
            ".rs" => "Rust",
            ".sh" => "Shell",
            ".bat" or ".cmd" => "Batch",
            _ => null
        };
    }
}
