using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Sanitizes Event Log table-view projection arguments to avoid avoidable invalid_argument failures.
/// </summary>
internal static class EventLogProjectionArgumentSanitizer {
    private static readonly IReadOnlyDictionary<string, string> ProjectionAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["timecreated"] = "time_created_utc",
            ["time"] = "time_created_utc",
            ["timestamp"] = "time_created_utc",
            ["computer"] = "machine_name",
            ["machine"] = "machine_name",
            ["host"] = "machine_name",
            ["eventid"] = "id",
            ["event"] = "id",
            ["recordid"] = "record_id",
            ["provider"] = "provider_name"
        };

    internal static JsonObject? SanitizeProjectionArguments(
        JsonObject? arguments,
        IReadOnlyCollection<string> availableColumns) {
        if (arguments is null || arguments.Count == 0) {
            return arguments;
        }

        var hasProjectionArguments = false;
        foreach (var pair in arguments) {
            var key = (pair.Key ?? string.Empty).Trim();
            if (IsProjectionArgumentName(key)) {
                hasProjectionArguments = true;
                break;
            }
        }

        if (!hasProjectionArguments) {
            return arguments;
        }

        var available = BuildAvailableColumnLookup(availableColumns);

        var requiresFallbackRemoval = false;
        var columnsChanged = false;
        var sortByChanged = false;
        IReadOnlyList<string>? sanitizedColumns = null;
        string? sanitizedSortBy = null;

        if (TryReadColumns(arguments, out var requestedColumns)) {
            var mappedColumns = new List<string>(requestedColumns.Count);
            for (var i = 0; i < requestedColumns.Count; i++) {
                if (!TryResolveColumnName(requestedColumns[i], available, out var resolved)) {
                    requiresFallbackRemoval = true;
                    break;
                }

                if (resolved.Length == 0) {
                    continue;
                }

                if (!mappedColumns.Contains(resolved, StringComparer.OrdinalIgnoreCase)) {
                    mappedColumns.Add(resolved);
                }
            }

            if (!requiresFallbackRemoval) {
                sanitizedColumns = mappedColumns;
                columnsChanged = HaveDifferentItems(requestedColumns, mappedColumns);
            }
        }

        if (!requiresFallbackRemoval && TryReadSortBy(arguments, out var requestedSortBy)) {
            if (!TryResolveColumnName(requestedSortBy, available, out var resolvedSortBy)) {
                requiresFallbackRemoval = true;
            } else {
                sanitizedSortBy = resolvedSortBy;
                sortByChanged = !string.Equals(requestedSortBy, resolvedSortBy, StringComparison.OrdinalIgnoreCase);
            }
        }

        if (requiresFallbackRemoval) {
            return RemoveProjectionArguments(arguments);
        }

        if (!columnsChanged && !sortByChanged) {
            return arguments;
        }

        var clone = new JsonObject(StringComparer.Ordinal);
        foreach (var pair in arguments) {
            var key = (pair.Key ?? string.Empty).Trim();
            if (key.Length == 0) {
                continue;
            }

            if (string.Equals(key, "columns", StringComparison.OrdinalIgnoreCase) && sanitizedColumns is not null) {
                var columnsArray = new JsonArray().AddRange(sanitizedColumns);
                clone.Add(key, columnsArray);
                continue;
            }

            if (string.Equals(key, "sort_by", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(sanitizedSortBy)) {
                clone.Add(key, sanitizedSortBy);
                continue;
            }

            clone.Add(key, pair.Value);
        }

        return clone;
    }

    private static JsonObject RemoveProjectionArguments(JsonObject arguments) {
        var clone = new JsonObject(StringComparer.Ordinal);
        foreach (var pair in arguments) {
            var key = (pair.Key ?? string.Empty).Trim();
            if (key.Length == 0 || IsProjectionArgumentName(key)) {
                continue;
            }

            clone.Add(key, pair.Value);
        }

        return clone;
    }

    private static bool HaveDifferentItems(IReadOnlyList<string> left, IReadOnlyList<string> right) {
        if (left.Count != right.Count) {
            return true;
        }

        for (var i = 0; i < left.Count; i++) {
            if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static (HashSet<string> Exact, Dictionary<string, string> CanonicalToExact) BuildAvailableColumnLookup(
        IReadOnlyCollection<string> availableColumns) {
        var exact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var canonicalToExact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (availableColumns is null || availableColumns.Count == 0) {
            return (exact, canonicalToExact);
        }

        foreach (var column in availableColumns) {
            var trimmed = (column ?? string.Empty).Trim();
            if (trimmed.Length == 0) {
                continue;
            }

            exact.Add(trimmed);
            var canonical = Canonicalize(trimmed);
            if (canonical.Length == 0 || canonicalToExact.ContainsKey(canonical)) {
                continue;
            }

            canonicalToExact.Add(canonical, trimmed);
        }

        return (exact, canonicalToExact);
    }

    private static bool TryResolveColumnName(
        string requestedColumn,
        (HashSet<string> Exact, Dictionary<string, string> CanonicalToExact) available,
        out string resolvedColumn) {
        resolvedColumn = string.Empty;
        var trimmed = (requestedColumn ?? string.Empty).Trim();
        if (trimmed.Length == 0) {
            return true;
        }

        if (available.Exact.Contains(trimmed)) {
            resolvedColumn = trimmed;
            return true;
        }

        var canonical = Canonicalize(trimmed);
        if (canonical.Length == 0) {
            return false;
        }

        if (available.CanonicalToExact.TryGetValue(canonical, out var exactMatch)) {
            resolvedColumn = exactMatch;
            return true;
        }

        if (ProjectionAliases.TryGetValue(canonical, out var alias)
            && available.Exact.Contains(alias)) {
            resolvedColumn = alias;
            return true;
        }

        return false;
    }

    private static bool TryReadColumns(JsonObject arguments, out IReadOnlyList<string> columns) {
        columns = Array.Empty<string>();

        foreach (var pair in arguments) {
            if (!string.Equals((pair.Key ?? string.Empty).Trim(), "columns", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (pair.Value?.AsArray() is not JsonArray array || array.Count == 0) {
                return false;
            }

            columns = ToolArgs.ReadDistinctStringArray(array)
                .Select(static value => (value ?? string.Empty).Trim())
                .Where(static value => value.Length > 0)
                .ToArray();
            return columns.Count > 0;
        }

        return false;
    }

    private static bool TryReadSortBy(JsonObject arguments, out string sortBy) {
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

    private static string Canonicalize(string value) {
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
