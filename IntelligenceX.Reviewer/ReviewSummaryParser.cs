using System;
using System.Collections.Generic;
using System.IO;

namespace IntelligenceX.Reviewer;

internal static class ReviewSummaryParser {
    public static bool TryGetReviewedCommit(string? body, out string? commit) {
        commit = null;
        if (string.IsNullOrWhiteSpace(body)) {
            return false;
        }

        var marker = ReviewFormatter.ReviewedCommitMarker;
        using var reader = new StringReader(body);
        string? line;
        while ((line = reader.ReadLine()) is not null) {
            var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) {
                continue;
            }
            var slice = line.Substring(index + marker.Length);
            var start = slice.IndexOf('`');
            if (start < 0) {
                continue;
            }
            slice = slice.Substring(start + 1);
            var end = slice.IndexOf('`');
            if (end < 0) {
                continue;
            }
            var token = slice.Substring(0, end).Trim();
            if (token.Length == 0) {
                continue;
            }
            commit = token;
            return true;
        }

        return false;
    }

    internal static bool HasMergeBlockers(string? body, ReviewSettings? settings = null) {
        if (string.IsNullOrWhiteSpace(body)) {
            return true;
        }

        body = ReviewFormatter.NormalizeSectionLayout(body);

        var configuredSections = settings?.ResolveMergeBlockerSections()
                                 ?? new[] { "todo list", "critical issues" };
        var mergeBlockerSections = ReviewSettings.NormalizeMergeBlockerSections(configuredSections);
        if (mergeBlockerSections.Count == 0) {
            mergeBlockerSections = new[] { "todo list", "critical issues" };
        }

        var sectionText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seenSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentSection = string.Empty;
        using var reader = new StringReader(body);
        string? line;
        while ((line = reader.ReadLine()) is not null) {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("## ", StringComparison.Ordinal)) {
                currentSection = MatchConfiguredSection(trimmed, mergeBlockerSections);
                if (currentSection.Length > 0) {
                    seenSections.Add(currentSection);
                }
                continue;
            }

            if (currentSection.Length > 0) {
                sectionText.TryGetValue(currentSection, out var existing);
                sectionText[currentSection] = $"{existing}\n{trimmed}";
            }
        }

        var requireSectionMatch = settings?.MergeBlockerRequireSectionMatch ?? true;
        if (seenSections.Count == 0) {
            return requireSectionMatch;
        }
        if ((settings?.MergeBlockerRequireAllSections ?? true) &&
            seenSections.Count < mergeBlockerSections.Count) {
            return true;
        }

        foreach (var section in seenSections) {
            sectionText.TryGetValue(section, out var content);
            if (SectionHasMergeBlockerItems(content ?? string.Empty)) {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeHeader(string header) {
        return header.Trim().ToLowerInvariant();
    }

    private static string MatchConfiguredSection(string header, IReadOnlyList<string> configuredSections) {
        var normalizedHeader = NormalizeHeader(header);
        foreach (var section in configuredSections) {
            if (normalizedHeader.Contains(section, StringComparison.OrdinalIgnoreCase)) {
                return section;
            }
        }
        return string.Empty;
    }

    private static bool SectionHasMergeBlockerItems(string section) {
        if (string.IsNullOrWhiteSpace(section)) {
            return false;
        }

        using var reader = new StringReader(section);
        string? line;
        while ((line = reader.ReadLine()) is not null) {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) {
                continue;
            }
            if (trimmed.StartsWith("<!--", StringComparison.Ordinal)) {
                continue;
            }
            if (IsNoneLine(trimmed)) {
                continue;
            }
            if (IsPlaceholderLine(trimmed)) {
                continue;
            }
            if (trimmed.StartsWith("*Rationale:", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("*Why", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (trimmed.StartsWith("- [ ]", StringComparison.Ordinal)) {
                return !IsNoneLine(trimmed) && !IsPlaceholderLine(trimmed);
            }
            if (trimmed.StartsWith("- [x]", StringComparison.OrdinalIgnoreCase)) {
                // Checked checklist items represent already-addressed work and should not block merge.
                continue;
            }
            if (trimmed.StartsWith("-", StringComparison.Ordinal)) {
                return !IsNoneLine(trimmed) && !IsPlaceholderLine(trimmed);
            }
        }

        return false;
    }

    private static bool IsNoneLine(string line) {
        var normalized = line.Trim().Trim('*').Trim();
        if (normalized.StartsWith("- [ ]", StringComparison.Ordinal)) {
            normalized = normalized.Substring(5).Trim();
        } else if (normalized.StartsWith("- [x]", StringComparison.OrdinalIgnoreCase)) {
            normalized = normalized.Substring(5).Trim();
        } else if (normalized.StartsWith("-", StringComparison.Ordinal)) {
            normalized = normalized.Substring(1).Trim();
        }

        return string.Equals(normalized, "none", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "none.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlaceholderLine(string line) {
        var normalized = line.Trim().Trim('*').Trim();
        return string.Equals(normalized, "(if any)", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "if any", StringComparison.OrdinalIgnoreCase);
    }
}
