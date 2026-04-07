using System.Collections.Generic;
using System.IO;
using System.Text;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Reviewer;

internal static class ReviewFormatter {
    public const string SummaryMarker = "<!-- intelligencex:summary -->";
    public const string InlineMarker = "<!-- intelligencex:inline -->";
    public const string StaticAnalysisInlineMarker = "<!-- intelligencex:analysis-inline -->";
    public const string ReviewedCommitMarker = "Reviewed commit:";
    private const string ProgressTemplateName = "ReviewProgress.md";
    private static readonly string[] SectionLabels = {
        "Summary 📝",
        "Review Summary 📝",
        "Todo List ✅",
        "Critical Issues ⚠️ (if any)",
        "Critical Issues ⚠️",
        "Other Issues 🧯",
        "Other Reviews 🧩",
        "Tests / Coverage 🧪",
        "Inline Comments 🔍",
        "Code Quality Assessment ⭐",
        "Excellent Aspects ✨",
        "Security & Performance 🔐⚡",
        "Test Quality 🧪",
        "Documentation 📚",
        "Backward Compatibility 🔄",
        "Recommendations 💡",
        "Next Steps 🚀"
    };

    public static string BuildComment(PullRequestContext context, string reviewBody, ReviewSettings settings, bool inlineSupported,
        bool inlineSuppressed, string? autoResolveNote, string? budgetNote, string? usageLine, string? findingsBlock) {
        var inlineNote = string.Empty;
        if (!inlineSupported && settings.Mode != "summary") {
            inlineNote = "> Inline comments are not enabled yet; posting summary only.\n";
        } else if (inlineSuppressed && settings.Mode != "summary") {
            inlineNote = "> Inline comments were skipped due to a failed review; posting summary only.\n";
        }
        var autoResolveLine = string.IsNullOrWhiteSpace(autoResolveNote)
            ? string.Empty
            : FormatBlockQuote(autoResolveNote);
        var budgetLine = string.IsNullOrWhiteSpace(budgetNote)
            ? string.Empty
            : FormatBlockQuote(budgetNote);

        var body = string.IsNullOrWhiteSpace(reviewBody)
            ? "_No review content was produced._"
            : NormalizeSectionLayout(reviewBody.Trim());

        var template = ResolveSummaryTemplate(settings);
        var reasoningParts = new List<string>();
        if (settings.ReasoningEffort.HasValue) {
            reasoningParts.Add($"effort: {settings.ReasoningEffort.Value.ToString().ToLowerInvariant()}");
        }
        if (settings.ReasoningSummary.HasValue) {
            reasoningParts.Add($"summary: {settings.ReasoningSummary.Value.ToString().ToLowerInvariant()}");
        }
        var reasoningLine = reasoningParts.Count == 0
            ? string.Empty
            : $" | Reasoning: {string.Join(", ", reasoningParts)}";
        var reasoningMeta = reasoningParts.Count == 0
            ? "- Reasoning: not configured"
            : $"- Reasoning: {string.Join(", ", reasoningParts)}";
        var reasoningLabel = BuildReasoningLabel(settings.ReasoningEffort);
        var usageMeta = BuildUsageMetaLine(usageLine);
        var tokens = new Dictionary<string, string> {
            ["SummaryMarker"] = SummaryMarker,
            ["Number"] = context.Number.ToString(),
            ["Title"] = EscapeMarkdown(context.Title),
            ["CommitLine"] = FormatCommitLine(context.HeadSha),
            ["ReasoningLabel"] = reasoningLabel,
            ["InlineNote"] = inlineNote,
            ["AutoResolveNote"] = autoResolveLine,
            ["BudgetNote"] = budgetLine,
            ["ReviewBody"] = body,
            ["Model"] = settings.Model,
            ["Length"] = settings.Length.ToString().ToLowerInvariant(),
            ["Mode"] = settings.Mode,
            ["ReasoningLine"] = reasoningLine,
            ["ReasoningMeta"] = reasoningMeta,
            ["UsageLine"] = string.IsNullOrWhiteSpace(usageLine) ? string.Empty : usageLine.Trim(),
            ["UsageMeta"] = usageMeta,
            ["FindingsBlock"] = string.IsNullOrWhiteSpace(findingsBlock) ? string.Empty : findingsBlock.Trim()
        };

        return TemplateRenderer.Render(template, tokens).TrimEnd();
    }

    internal static string NormalizeSectionLayout(string reviewBody) {
        if (string.IsNullOrWhiteSpace(reviewBody)) {
            return string.Empty;
        }

        var lines = reviewBody.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder();
        var previousLineBlank = true;
        var fencedCodeMarker = string.Empty;

        foreach (var line in lines) {
            var rawLine = line.TrimEnd();
            var trimmedLine = rawLine.TrimStart();
            if (TryMatchFenceDelimiter(trimmedLine, out var currentFenceMarker)) {
                if (string.IsNullOrEmpty(fencedCodeMarker)) {
                    fencedCodeMarker = currentFenceMarker;
                } else if (string.Equals(fencedCodeMarker, currentFenceMarker, System.StringComparison.Ordinal)) {
                    fencedCodeMarker = string.Empty;
                }

                sb.AppendLine(rawLine);
                previousLineBlank = string.IsNullOrWhiteSpace(rawLine);
                continue;
            }

            if (!string.IsNullOrEmpty(fencedCodeMarker) || IsIndentedCodeLine(rawLine)) {
                sb.AppendLine(rawLine);
                previousLineBlank = string.IsNullOrWhiteSpace(rawLine);
                continue;
            }

            if (TryNormalizeSectionLine(trimmedLine, out var heading, out var remainder)) {
                if (sb.Length > 0 && !previousLineBlank) {
                    sb.AppendLine();
                }

                sb.AppendLine(heading);
                if (!string.IsNullOrWhiteSpace(remainder)) {
                    sb.AppendLine();
                    sb.AppendLine(remainder);
                    previousLineBlank = false;
                } else {
                    previousLineBlank = false;
                }
                continue;
            }

            sb.AppendLine(rawLine);
            previousLineBlank = string.IsNullOrWhiteSpace(rawLine);
        }

        return sb.ToString().TrimEnd();
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

    private static string FormatBlockQuote(string value) {
        var lines = value.Replace("\r", "").Split('\n');
        var sb = new StringBuilder();
        foreach (var line in lines) {
            var trimmed = line.TrimEnd();
            if (trimmed.Length == 0) {
                sb.AppendLine(">");
                continue;
            }
            sb.AppendLine($"> {trimmed}");
        }
        return sb.ToString();
    }

    private static string FormatCommitLine(string? sha) {
        if (string.IsNullOrWhiteSpace(sha)) {
            return string.Empty;
        }
        var trimmed = sha.Trim();
        var shortSha = trimmed.Length > 7 ? trimmed.Substring(0, 7) : trimmed;
        return $"{ReviewedCommitMarker} `{shortSha}`\n";
    }

    private static string BuildReasoningLabel(ReasoningEffort? effort) {
        if (!effort.HasValue) {
            return string.Empty;
        }
        var label = effort.Value switch {
            ReasoningEffort.Low => "low",
            ReasoningEffort.Medium => "medium",
            ReasoningEffort.High => "high",
            _ => null
        };
        return string.IsNullOrWhiteSpace(label) ? string.Empty : $"Reasoning level: {label}\n";
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

    private static string BuildUsageMetaLine(string? usageLine) {
        if (string.IsNullOrWhiteSpace(usageLine)) {
            return "- Usage: unavailable";
        }
        const string prefix = "Usage: ";
        var trimmed = usageLine.Trim();
        if (trimmed.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) {
            trimmed = trimmed.Substring(prefix.Length).Trim();
        }
        return string.IsNullOrWhiteSpace(trimmed)
            ? "- Usage: unavailable"
            : $"- Usage: {trimmed}";
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

    private static bool TryNormalizeSectionLine(string trimmedLine, out string heading, out string remainder) {
        heading = string.Empty;
        remainder = string.Empty;

        if (string.IsNullOrWhiteSpace(trimmedLine)) {
            return false;
        }

        var headingPrefix = "##";
        var content = trimmedLine;
        var hadHeadingPrefix = false;
        if (trimmedLine.StartsWith("### ", System.StringComparison.Ordinal)) {
            headingPrefix = "###";
            content = trimmedLine.Substring(4).TrimStart();
            hadHeadingPrefix = true;
        } else if (trimmedLine.StartsWith("## ", System.StringComparison.Ordinal)) {
            content = trimmedLine.Substring(3).TrimStart();
            hadHeadingPrefix = true;
        }

        foreach (var label in SectionLabels) {
            if (!content.StartsWith(label, System.StringComparison.Ordinal)) {
                continue;
            }

            if (content.Length > label.Length) {
                var next = content[label.Length];
                if (!hadHeadingPrefix && !char.IsWhiteSpace(next) && next != ':' && next != '-') {
                    continue;
                }
            }

            heading = $"{headingPrefix} {label}";
            remainder = content.Substring(label.Length).TrimStart();
            if (remainder.StartsWith(":", System.StringComparison.Ordinal)) {
                remainder = remainder.Substring(1).TrimStart();
            }
            if (hadHeadingPrefix && string.IsNullOrWhiteSpace(remainder)) {
                heading = string.Empty;
                return false;
            }
            return true;
        }

        return false;
    }

    private static bool TryMatchFenceDelimiter(string trimmedLine, out string marker) {
        marker = string.Empty;
        if (trimmedLine.StartsWith("```", System.StringComparison.Ordinal)) {
            marker = "```";
            return true;
        }
        if (trimmedLine.StartsWith("~~~", System.StringComparison.Ordinal)) {
            marker = "~~~";
            return true;
        }
        return false;
    }

    private static bool IsIndentedCodeLine(string rawLine) {
        return rawLine.StartsWith("    ", System.StringComparison.Ordinal)
               || rawLine.StartsWith("\t", System.StringComparison.Ordinal);
    }
}
