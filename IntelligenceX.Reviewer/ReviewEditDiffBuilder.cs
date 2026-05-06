using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace IntelligenceX.Reviewer;

internal static class ReviewEditDiffBuilder {
    private const int MaxSectionNames = 5;
    private const int MaxFindings = 100;

    private static readonly HashSet<string> IgnoredSections = new(StringComparer.OrdinalIgnoreCase) {
        "intelligencex review",
        "sticky edit diff",
        "model usage"
    };

    public static string BuildCommentBlock(IssueComment? previousSummary, string? nextCommentBody,
        ReviewSettings settings) {
        if (previousSummary is null || string.IsNullOrWhiteSpace(previousSummary.Body) ||
            string.IsNullOrWhiteSpace(nextCommentBody)) {
            return string.Empty;
        }

        var previousBody = ReviewHistoryMarker.Remove(previousSummary.Body);
        var nextBody = ReviewHistoryMarker.Remove(nextCommentBody);
        var previousSections = ExtractSections(previousBody);
        var nextSections = ExtractSections(nextBody);
        var sectionDelta = BuildSectionDelta(previousSections, nextSections);
        var blockerDelta = BuildBlockerDelta(previousBody, nextBody, settings);

        if (!sectionDelta.HasChanges && !blockerDelta.HasChanges) {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("## Sticky Edit Diff 🧾");
        sb.AppendLine();
        var comparedAt = previousSummary.UpdatedAt ?? previousSummary.CreatedAt;
        if (comparedAt.HasValue) {
            sb.AppendLine($"- **Compared with:** previous sticky update from {comparedAt.Value.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC.");
        } else {
            sb.AppendLine("- **Compared with:** previous sticky summary.");
        }
        sb.AppendLine($"- **Merge blockers:** {FormatCount(blockerDelta.NewOpen, "new open item")}, {FormatCount(blockerDelta.ResolvedOrRemoved, "resolved/removed item")}, {FormatCount(blockerDelta.UnchangedOpen, "unchanged open item")}.");
        AppendSectionLine(sb, "Sections added", sectionDelta.Added);
        AppendSectionLine(sb, "Sections removed", sectionDelta.Removed);
        AppendSectionLine(sb, "Sections changed", sectionDelta.Changed);
        return sb.ToString().TrimEnd();
    }

    private static SectionDelta BuildSectionDelta(IReadOnlyDictionary<string, ReviewSection> previousSections,
        IReadOnlyDictionary<string, ReviewSection> nextSections) {
        var added = nextSections
            .Where(pair => !previousSections.ContainsKey(pair.Key))
            .Select(pair => pair.Value.DisplayName)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var removed = previousSections
            .Where(pair => !nextSections.ContainsKey(pair.Key))
            .Select(pair => pair.Value.DisplayName)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var changed = nextSections
            .Where(pair => previousSections.TryGetValue(pair.Key, out var previous) &&
                           !string.Equals(previous.NormalizedContent, pair.Value.NormalizedContent, StringComparison.Ordinal))
            .Select(pair => pair.Value.DisplayName)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SectionDelta(added, removed, changed);
    }

    private static BlockerDelta BuildBlockerDelta(string previousBody, string nextBody, ReviewSettings settings) {
        var previousOpen = BuildOpenFindingSet(previousBody, settings);
        var nextOpen = BuildOpenFindingSet(nextBody, settings);
        var newOpen = nextOpen.Count(key => !previousOpen.Contains(key));
        var resolvedOrRemoved = previousOpen.Count(key => !nextOpen.Contains(key));
        var unchangedOpen = nextOpen.Count(key => previousOpen.Contains(key));
        return new BlockerDelta(newOpen, resolvedOrRemoved, unchangedOpen);
    }

    private static HashSet<string> BuildOpenFindingSet(string body, ReviewSettings settings) {
        var findings = ReviewSummaryParser.ExtractMergeBlockerFindings(body, settings, MaxFindings);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var finding in findings) {
            if (string.Equals(finding.Status, "open", StringComparison.OrdinalIgnoreCase)) {
                keys.Add(finding.Fingerprint);
            }
        }
        return keys;
    }

    private static IReadOnlyDictionary<string, ReviewSection> ExtractSections(string body) {
        var sections = new Dictionary<string, ReviewSection>(StringComparer.OrdinalIgnoreCase);
        var normalized = ReviewFormatter.NormalizeSectionLayout(body ?? string.Empty);
        using var reader = new StringReader(normalized);
        var content = new StringBuilder();
        string? key = null;
        string? displayName = null;
        string? line;
        while ((line = reader.ReadLine()) is not null) {
            var trimmed = line.Trim();
            if (IsMarkdownHeading(trimmed)) {
                FlushSection(sections, key, displayName, content);
                content.Clear();
                if (IsH2Heading(trimmed)) {
                    displayName = ExtractHeadingText(trimmed);
                    key = NormalizeHeader(displayName);
                    if (IgnoredSections.Contains(key)) {
                        key = null;
                        displayName = null;
                    }
                } else {
                    key = null;
                    displayName = null;
                }
                continue;
            }

            if (key is null || trimmed.StartsWith("<!--", StringComparison.Ordinal)) {
                continue;
            }

            if (content.Length > 0) {
                content.Append('\n');
            }
            content.Append(trimmed);
        }

        FlushSection(sections, key, displayName, content);
        return sections;
    }

    private static void FlushSection(IDictionary<string, ReviewSection> sections, string? key, string? displayName,
        StringBuilder content) {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(displayName)) {
            return;
        }

        sections[key!] = new ReviewSection(displayName!, NormalizeContent(content.ToString()));
    }

    private static void AppendSectionLine(StringBuilder sb, string label, IReadOnlyList<string> values) {
        if (values.Count == 0) {
            sb.AppendLine($"- **{label}:** none.");
            return;
        }

        var visible = values.Take(MaxSectionNames).Select(static value => $"`{value}`").ToArray();
        var suffix = values.Count > MaxSectionNames ? $", +{values.Count - MaxSectionNames} more" : string.Empty;
        sb.AppendLine($"- **{label}:** {string.Join(", ", visible)}{suffix}.");
    }

    private static string FormatCount(int count, string singular) =>
        count == 1 ? $"1 {singular}" : $"{count} {singular}s";

    private static string NormalizeContent(string value) {
        var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        return string.Join("\n", lines.Select(static line => line.Trim()).Where(static line => line.Length > 0));
    }

    private static string ExtractHeadingText(string heading) {
        var trimmed = heading.Trim();
        while (trimmed.StartsWith("#", StringComparison.Ordinal)) {
            trimmed = trimmed.Substring(1).TrimStart();
        }
        return trimmed.Trim();
    }

    private static string NormalizeHeader(string header) {
        var sb = new StringBuilder(header.Length);
        var pendingSpace = false;
        foreach (var ch in header.ToLowerInvariant()) {
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

    private static bool IsH2Heading(string line) =>
        line.StartsWith("##", StringComparison.Ordinal) &&
        !line.StartsWith("###", StringComparison.Ordinal) &&
        line.Length > 2 &&
        char.IsWhiteSpace(line[2]);

    private readonly record struct ReviewSection(string DisplayName, string NormalizedContent);

    private readonly record struct SectionDelta(
        IReadOnlyList<string> Added,
        IReadOnlyList<string> Removed,
        IReadOnlyList<string> Changed) {
        public bool HasChanges => Added.Count > 0 || Removed.Count > 0 || Changed.Count > 0;
    }

    private readonly record struct BlockerDelta(int NewOpen, int ResolvedOrRemoved, int UnchangedOpen) {
        public bool HasChanges => NewOpen > 0 || ResolvedOrRemoved > 0;
    }
}
