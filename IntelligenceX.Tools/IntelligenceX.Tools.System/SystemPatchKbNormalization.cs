using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Shared KB extraction/normalization helpers for patch intelligence tools.
/// </summary>
internal static class SystemPatchKbNormalization {
    /// <summary>
    /// Normalizes KB-like values into distinct canonical tokens (for example: KB5034441).
    /// </summary>
    internal static IReadOnlyList<string> NormalizeDistinct(IEnumerable<string>? values) {
        if (values is null) {
            return Array.Empty<string>();
        }

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values) {
            foreach (var kb in EnumerateNormalized(value)) {
                normalized.Add(kb);
            }
        }

        return normalized
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Returns true when any candidate KB matches a case-insensitive contains filter.
    /// </summary>
    internal static bool MatchesContainsFilter(IEnumerable<string>? values, string? filter) {
        if (string.IsNullOrWhiteSpace(filter)) {
            return true;
        }
        if (values is null) {
            return false;
        }

        var rawFilter = filter.Trim();
        var normalizedFilterTokens = NormalizeDistinct(new[] { rawFilter });

        foreach (var value in values) {
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            if (value.Contains(rawFilter, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            foreach (var normalized in EnumerateNormalized(value)) {
                if (normalized.Contains(rawFilter, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }

                for (var i = 0; i < normalizedFilterTokens.Count; i++) {
                    if (normalized.Contains(normalizedFilterTokens[i], StringComparison.OrdinalIgnoreCase)) {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Enumerates canonical KB tokens from text.
    /// </summary>
    internal static IEnumerable<string> EnumerateNormalized(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            yield break;
        }

        if (TryNormalizeWholeValue(value, out var direct)) {
            yield return direct;
            yield break;
        }

        var s = value!;
        for (var i = 0; i < s.Length - 2; i++) {
            if ((s[i] == 'K' || s[i] == 'k') && (s[i + 1] == 'B' || s[i + 1] == 'b')) {
                var j = i + 2;
                while (j < s.Length && char.IsWhiteSpace(s[j])) {
                    j++;
                }

                var start = j;
                while (j < s.Length && char.IsDigit(s[j])) {
                    j++;
                }

                if (j > start) {
                    yield return "KB" + s.Substring(start, j - start);
                }

                i = j;
            }
        }
    }

    private static bool TryNormalizeWholeValue(string input, out string normalized) {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) {
            return false;
        }

        var candidate = input.Trim();
        if (candidate.StartsWith("KB", StringComparison.OrdinalIgnoreCase)) {
            candidate = candidate.Substring(2).Trim();
        }

        if (candidate.Length == 0) {
            return false;
        }

        for (var i = 0; i < candidate.Length; i++) {
            if (!char.IsDigit(candidate[i])) {
                return false;
            }
        }

        normalized = "KB" + candidate;
        return true;
    }
}
