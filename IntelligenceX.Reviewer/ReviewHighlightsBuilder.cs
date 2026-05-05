using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace IntelligenceX.Reviewer;

internal static class ReviewHighlightsBuilder {
    private static readonly string[] PositiveSections = {
        "Excellent Aspects",
        "Code Quality Assessment"
    };

    private static readonly string[] RiskSections = {
        "Other Issues",
        "Security & Performance",
        "Backward Compatibility"
    };

    private static readonly string[] TestSections = {
        "Tests / Coverage",
        "Test Quality"
    };

    private static readonly string[] NextStepSections = {
        "Next Steps",
        "Recommendations"
    };

    public static string BuildCommentBlock(string? reviewBody, ReviewSettings settings, bool reviewFailed) {
        if (string.IsNullOrWhiteSpace(reviewBody)) {
            return string.Empty;
        }

        var state = ReviewStateBuilder.Build(reviewBody, settings, reviewFailed);
        var positives = ExtractSectionItems(reviewBody, PositiveSections, 2);
        if (positives.Count == 0) {
            positives = ExtractSummarySentences(reviewBody, 1);
        }

        var risks = ExtractSectionItems(reviewBody, RiskSections, 3);
        var tests = ExtractSectionItems(reviewBody, TestSections, 2);
        if (tests.Count == 0) {
            tests = ExtractSectionParagraphs(reviewBody, TestSections, 1);
        }
        var next = ExtractSectionItems(reviewBody, NextStepSections, 2);
        if (next.Count == 0) {
            next = ExtractSectionParagraphs(reviewBody, NextStepSections, 1);
        }
        if (positives.Count == 0 && risks.Count == 0 && tests.Count == 0 && next.Count == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("## Review Highlights ✨");
        sb.AppendLine();
        sb.AppendLine($"**Verdict:** {NormalizeLine(state.RecommendationLabel)}. Merge blockers: {NormalizeLine(state.MergeBlockerLabel)}.");
        sb.AppendLine();
        AppendItemSection(sb, "Good", positives);
        AppendItemSection(sb, "Risks / Watch", risks);
        AppendItemSection(sb, "Tests", tests);
        AppendItemSection(sb, "Next", next);
        return sb.ToString().TrimEnd();
    }

    private static void AppendItemSection(StringBuilder sb, string title, IReadOnlyList<string> items) {
        sb.AppendLine($"**{title}**");
        if (items.Count == 0) {
            sb.AppendLine("None noted.");
            sb.AppendLine();
            return;
        }

        foreach (var item in items) {
            sb.AppendLine($"- {TrimHighlightItem(item)}");
        }

        sb.AppendLine();
    }

    private static IReadOnlyList<string> ExtractSummarySentences(string body, int maxItems) {
        var paragraphs = ExtractSectionParagraphs(body, new[] { "Summary", "Review Summary" }, maxItems);
        if (paragraphs.Count == 0) {
            return paragraphs;
        }

        var items = new List<string>(paragraphs.Count);
        foreach (var paragraph in paragraphs) {
            var sentence = FirstSentence(paragraph);
            if (!string.IsNullOrWhiteSpace(sentence)) {
                items.Add(sentence);
            }
        }
        return items;
    }

    private static IReadOnlyList<string> ExtractSectionItems(string body, IReadOnlyList<string> sectionNames, int maxItems) {
        if (string.IsNullOrWhiteSpace(body) || maxItems <= 0 || sectionNames.Count == 0) {
            return Array.Empty<string>();
        }

        var targetSections = new HashSet<string>(sectionNames.Select(NormalizeHeader), StringComparer.OrdinalIgnoreCase);
        var items = new List<string>();
        var inTargetSection = false;
        using var reader = new StringReader(ReviewFormatter.NormalizeSectionLayout(body));
        string? line;
        while ((line = reader.ReadLine()) is not null) {
            var trimmed = line.Trim();
            if (IsMarkdownHeading(trimmed)) {
                inTargetSection = IsH2Heading(trimmed) && targetSections.Contains(NormalizeHeader(trimmed));
                continue;
            }

            if (!inTargetSection || items.Count >= maxItems || !TryExtractSummaryItem(trimmed, out var item)) {
                continue;
            }

            items.Add(item);
        }

        return items;
    }

    private static IReadOnlyList<string> ExtractSectionParagraphs(string body, IReadOnlyList<string> sectionNames, int maxItems) {
        if (string.IsNullOrWhiteSpace(body) || maxItems <= 0 || sectionNames.Count == 0) {
            return Array.Empty<string>();
        }

        var targetSections = new HashSet<string>(sectionNames.Select(NormalizeHeader), StringComparer.OrdinalIgnoreCase);
        var items = new List<string>();
        var paragraph = new StringBuilder();
        var inTargetSection = false;
        using var reader = new StringReader(ReviewFormatter.NormalizeSectionLayout(body));
        string? line;
        while ((line = reader.ReadLine()) is not null) {
            var trimmed = line.Trim();
            if (IsMarkdownHeading(trimmed)) {
                FlushParagraph(paragraph, items, maxItems);
                inTargetSection = IsH2Heading(trimmed) && targetSections.Contains(NormalizeHeader(trimmed));
                continue;
            }

            if (!inTargetSection || items.Count >= maxItems || IsIgnoredLine(trimmed)) {
                FlushParagraph(paragraph, items, maxItems);
                continue;
            }

            if (paragraph.Length > 0) {
                paragraph.Append(' ');
            }
            paragraph.Append(trimmed);
        }

        FlushParagraph(paragraph, items, maxItems);
        return items;
    }

    private static void FlushParagraph(StringBuilder paragraph, List<string> items, int maxItems) {
        if (paragraph.Length == 0 || items.Count >= maxItems) {
            paragraph.Clear();
            return;
        }

        var text = paragraph.ToString().Trim();
        paragraph.Clear();
        if (!string.IsNullOrWhiteSpace(text) && !IsNoneLine(text)) {
            items.Add(text);
        }
    }

    private static bool TryExtractSummaryItem(string line, out string item) {
        item = string.Empty;
        if (IsIgnoredLine(line)) {
            return false;
        }

        var trimmed = line.Trim();
        if (trimmed.StartsWith("- [ ]", StringComparison.Ordinal) ||
            trimmed.StartsWith("- [x]", StringComparison.OrdinalIgnoreCase)) {
            item = trimmed.Substring(5).Trim();
        } else if (trimmed.StartsWith("-", StringComparison.Ordinal) ||
                   trimmed.StartsWith("*", StringComparison.Ordinal)) {
            item = trimmed.Substring(1).Trim();
        } else {
            return false;
        }

        return !string.IsNullOrWhiteSpace(item) && !IsNoneLine(item);
    }

    private static bool IsIgnoredLine(string value) {
        return string.IsNullOrWhiteSpace(value) ||
               value.StartsWith("<!--", StringComparison.Ordinal) ||
               value.StartsWith("|", StringComparison.Ordinal) ||
               value.StartsWith("```", StringComparison.Ordinal) ||
               IsNoneLine(value);
    }

    private static bool IsNoneLine(string value) {
        var normalized = value.Trim().TrimEnd('.').Trim();
        return normalized.Equals("none", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("none noted", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("n/a", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMarkdownHeading(string line) {
        if (string.IsNullOrWhiteSpace(line) || line[0] != '#') {
            return false;
        }

        var index = 0;
        while (index < line.Length && line[index] == '#') {
            index++;
        }

        return index > 0 && index < line.Length && char.IsWhiteSpace(line[index]);
    }

    private static bool IsH2Heading(string line) {
        if (!line.StartsWith("##", StringComparison.Ordinal) ||
            line.StartsWith("###", StringComparison.Ordinal)) {
            return false;
        }

        return line.Length > 2 && char.IsWhiteSpace(line[2]);
    }

    private static string NormalizeHeader(string header) {
        var trimmed = header.Trim();
        while (trimmed.StartsWith("#", StringComparison.Ordinal)) {
            trimmed = trimmed.Substring(1).TrimStart();
        }

        var sb = new StringBuilder(trimmed.Length);
        var pendingSpace = false;
        foreach (var ch in trimmed.ToLowerInvariant()) {
            if (char.IsLetterOrDigit(ch)) {
                if (pendingSpace && sb.Length > 0) {
                    sb.Append(' ');
                }
                sb.Append(ch);
                pendingSpace = false;
            } else {
                pendingSpace = true;
            }
        }

        return sb.ToString().Trim();
    }

    private static string TrimHighlightItem(string value) {
        var trimmed = NormalizeLine(value);
        const int maxLength = 500;
        return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength - 3).TrimEnd() + "...";
    }

    private static string FirstSentence(string value) {
        var trimmed = value.Trim();
        var index = trimmed.IndexOf(". ", StringComparison.Ordinal);
        return index < 0 ? trimmed : trimmed.Substring(0, index + 1);
    }

    private static string NormalizeLine(string value) {
        return (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }
}
