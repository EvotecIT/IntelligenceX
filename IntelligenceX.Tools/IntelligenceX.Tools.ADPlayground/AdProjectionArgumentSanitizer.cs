using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Sanitizes table-view projection arguments when requested columns do not exist in the current row shape.
/// </summary>
internal static class AdProjectionArgumentSanitizer {
    /// <summary>
    /// Removes projection arguments when columns/sort values are unsupported for the available column set.
    /// </summary>
    internal static JsonObject? RemoveUnsupportedProjectionArguments(
        JsonObject? arguments,
        IReadOnlyCollection<string> availableColumns) {
        if (arguments is null || arguments.Count == 0) {
            return arguments;
        }

        var available = BuildNormalizedAvailableColumns(availableColumns);
        if (!HasUnsupportedProjectionArguments(arguments, available)) {
            return arguments;
        }

        var clone = new JsonObject(StringComparer.Ordinal);
        foreach (var pair in arguments) {
            var key = (pair.Key ?? string.Empty).Trim();
            if (key.Length == 0) {
                continue;
            }

            if (IsProjectionArgumentName(key)) {
                continue;
            }

            clone.Add(key, pair.Value);
        }

        return clone;
    }

    private static bool HasUnsupportedProjectionArguments(
        JsonObject arguments,
        (HashSet<string> Exact, HashSet<string> Canonical) availableColumns) {
        if (TryGetColumnsArgument(arguments, out var requestedColumns) && requestedColumns.Count > 0) {
            for (var i = 0; i < requestedColumns.Count; i++) {
                if (!IsSupported(requestedColumns[i], availableColumns.Exact, availableColumns.Canonical)) {
                    return true;
                }
            }
        }

        if (TryGetSortByArgument(arguments, out var sortBy) && sortBy.Length > 0) {
            if (!IsSupported(sortBy, availableColumns.Exact, availableColumns.Canonical)) {
                return true;
            }
        }

        return false;
    }

    private static (HashSet<string> Exact, HashSet<string> Canonical) BuildNormalizedAvailableColumns(
        IReadOnlyCollection<string> availableColumns) {
        var exact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var canonical = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (availableColumns is null || availableColumns.Count == 0) {
            return (exact, canonical);
        }

        foreach (var column in availableColumns) {
            var trimmed = (column ?? string.Empty).Trim();
            if (trimmed.Length == 0) {
                continue;
            }

            exact.Add(trimmed);
            var normalized = CanonicalizeColumnKey(trimmed);
            if (normalized.Length > 0) {
                canonical.Add(normalized);
            }
        }

        return (exact, canonical);
    }

    private static bool IsSupported(
        string requested,
        IReadOnlySet<string> availableColumns,
        IReadOnlySet<string> canonicalAvailableColumns) {
        var trimmed = (requested ?? string.Empty).Trim();
        if (trimmed.Length == 0) {
            return true;
        }

        if (availableColumns.Contains(trimmed)) {
            return true;
        }

        var canonical = CanonicalizeColumnKey(trimmed);
        if (canonical.Length == 0) {
            return false;
        }

        return canonicalAvailableColumns.Contains(canonical);
    }

    private static bool TryGetColumnsArgument(JsonObject arguments, out IReadOnlyList<string> columns) {
        columns = Array.Empty<string>();

        foreach (var pair in arguments) {
            if (!string.Equals((pair.Key ?? string.Empty).Trim(), "columns", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (pair.Value?.AsArray() is not JsonArray array || array.Count == 0) {
                return false;
            }

            columns = ToolArgs.ReadDistinctStringArray(array)
                .Select(static x => (x ?? string.Empty).Trim())
                .Where(static x => x.Length > 0)
                .ToArray();
            return columns.Count > 0;
        }

        return false;
    }

    private static bool TryGetSortByArgument(JsonObject arguments, out string sortBy) {
        sortBy = string.Empty;

        foreach (var pair in arguments) {
            if (!string.Equals((pair.Key ?? string.Empty).Trim(), "sort_by", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            sortBy = (pair.Value?.AsString() ?? string.Empty).Trim();
            return sortBy.Length > 0;
        }

        return false;
    }

    private static bool IsProjectionArgumentName(string key) {
        return string.Equals(key, "columns", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "sort_by", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "sort_direction", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "top", StringComparison.OrdinalIgnoreCase);
    }

    private static string CanonicalizeColumnKey(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        var buffer = new char[value.Length];
        var index = 0;
        for (var i = 0; i < value.Length; i++) {
            var ch = value[i];
            if (!char.IsLetterOrDigit(ch)) {
                continue;
            }

            buffer[index++] = char.ToLowerInvariant(ch);
        }

        return index == 0 ? string.Empty : new string(buffer, 0, index);
    }
}
