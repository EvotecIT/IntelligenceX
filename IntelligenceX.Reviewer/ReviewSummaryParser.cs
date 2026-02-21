using System;
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

    internal static bool HasMergeBlockers(string? body) {
        if (string.IsNullOrWhiteSpace(body)) {
            return true;
        }

        var todo = string.Empty;
        var critical = string.Empty;
        var currentHeader = string.Empty;
        var sawTodoSection = false;
        var sawCriticalSection = false;
        using var reader = new StringReader(body);
        string? line;
        while ((line = reader.ReadLine()) is not null) {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("## ", StringComparison.Ordinal)) {
                currentHeader = NormalizeHeader(trimmed);
                if (currentHeader.Contains("todo list", StringComparison.Ordinal)) {
                    sawTodoSection = true;
                }
                if (currentHeader.Contains("critical issues", StringComparison.Ordinal)) {
                    sawCriticalSection = true;
                }
                continue;
            }

            if (currentHeader.Contains("todo list", StringComparison.Ordinal)) {
                todo += "\n" + trimmed;
                continue;
            }

            if (currentHeader.Contains("critical issues", StringComparison.Ordinal)) {
                critical += "\n" + trimmed;
            }
        }

        if (!sawTodoSection || !sawCriticalSection) {
            return true;
        }

        var hasTodo = SectionHasMergeBlockerItems(todo);
        var hasCritical = SectionHasMergeBlockerItems(critical);
        return hasTodo || hasCritical;
    }

    private static string NormalizeHeader(string header) {
        return header.Trim().ToLowerInvariant();
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
            if (trimmed.StartsWith("*Rationale:", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("*Why", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            return true;
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
}
