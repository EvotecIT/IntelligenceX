using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IntelligenceX.Json;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Shared helper for dynamic dictionary-backed rows (for example LDAP attribute bags).
/// </summary>
public static class ToolDynamicTableViewEnvelope {
    /// <summary>
    /// Applies tabular view arguments to dynamic bag rows and returns projected view rows.
    /// </summary>
    public static bool TryApply(
        JsonObject? arguments,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        out ToolTableViewResult viewResult,
        out string[] availableColumns,
        out string? error,
        int maxTop = 5000) {
        error = null;
        availableColumns = CollectColumns(rows).ToArray();

        if (availableColumns.Length == 0) {
            if ((arguments?.GetArray("columns")?.Count ?? 0) > 0 ||
                !string.IsNullOrWhiteSpace(ToolArgs.GetOptionalTrimmed(arguments, "sort_by"))) {
                viewResult = new ToolTableViewResult();
                error = "No columns are available for projection.";
                return false;
            }

            viewResult = new ToolTableViewResult {
                Columns = Array.Empty<ToolColumn>(),
                Rows = new JsonArray(),
                PreviewRows = Array.Empty<IReadOnlyList<string>>(),
                Count = 0,
                TruncatedByView = false
            };
            return true;
        }

        if (!ToolTableView.TryParse(arguments, availableColumns, maxTop: maxTop, out var viewRequest, out error)) {
            viewResult = new ToolTableViewResult();
            return false;
        }

        var specs = new List<ToolTableColumnSpec<IReadOnlyDictionary<string, object?>>>(availableColumns.Length);
        for (var i = 0; i < availableColumns.Length; i++) {
            var key = availableColumns[i];
            specs.Add(new ToolTableColumnSpec<IReadOnlyDictionary<string, object?>>(
                new ToolColumn(key, key, InferType(rows, key)),
                row => ReadValue(row, key),
                static value => FormatValue(value)));
        }

        viewResult = ToolTableView.Apply(rows, viewRequest, specs, previewMaxRows: 20);
        return true;
    }

    /// <summary>
    /// Builds a standard raw+view response using dynamic bag rows.
    /// </summary>
    public static bool TryBuildModelResponseFromBags<TModel>(
        JsonObject? arguments,
        TModel model,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        string title,
        string rowsPath,
        bool baseTruncated,
        out string response,
        int? scanned = null,
        Action<JsonObject>? metaMutate = null,
        int maxTop = 5000) {
        if (!TryApply(arguments, rows, out var viewResult, out var availableColumns, out var error, maxTop)) {
            response = ToolResponse.Error("invalid_argument", error ?? "Invalid tabular view arguments.");
            return false;
        }

        var root = ToolJson.ToJsonObjectSnakeCase(model);
        root.Add(rowsPath, viewResult.Rows);

        response = ToolResponse.OkTablePreviewModel(
            model: root,
            title: title,
            rowsPath: rowsPath,
            headers: viewResult.Columns.Select(static c => c.Label).ToArray(),
            previewRows: viewResult.PreviewRows,
            count: viewResult.Count,
            truncated: baseTruncated || viewResult.TruncatedByView,
            scanned: scanned,
            metaMutate: meta => {
                metaMutate?.Invoke(meta);
                meta.Add("available_columns", new JsonArray().AddRange(availableColumns));
            },
            columns: viewResult.Columns.ToArray());
        return true;
    }

    private static List<string> CollectColumns(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < rows.Count; i++) {
            foreach (var kv in rows[i]) {
                if (string.IsNullOrWhiteSpace(kv.Key)) {
                    continue;
                }

                if (seen.Add(kv.Key)) {
                    list.Add(kv.Key);
                }
            }
        }

        return list;
    }

    private static string InferType(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string key) {
        for (var i = 0; i < rows.Count; i++) {
            var value = ReadValue(rows[i], key);
            if (value is null) {
                continue;
            }

            return value switch {
                bool => "bool",
                byte or sbyte or short or ushort or int or uint or long or ulong => "int",
                float or double or decimal => "number",
                DateTime or DateTimeOffset => "datetime",
                IEnumerable when value is not string => "array",
                _ => "string"
            };
        }

        return "string";
    }

    private static object? ReadValue(IReadOnlyDictionary<string, object?> row, string key) {
        if (row.TryGetValue(key, out var value)) {
            return value;
        }

        foreach (var pair in row) {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) {
                return pair.Value;
            }
        }

        return null;
    }

    private static string FormatValue(object? value) {
        return value switch {
            null => string.Empty,
            DateTime dt => dt.ToUniversalTime().ToString("O"),
            DateTimeOffset dto => dto.ToUniversalTime().ToString("O"),
            IEnumerable<string> strings => string.Join(", ", strings),
            IEnumerable enumerable when value is not string => JoinEnumerable(enumerable),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static string JoinEnumerable(IEnumerable values) {
        var list = new List<string>();
        foreach (var value in values) {
            if (value is null) {
                continue;
            }

            list.Add(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
            if (list.Count >= 8) {
                break;
            }
        }

        return string.Join(", ", list);
    }
}
