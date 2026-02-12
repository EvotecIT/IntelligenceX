using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IntelligenceX.Json;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Common request contract for tabular tool result shaping.
/// </summary>
public sealed class ToolTableViewRequest {
    /// <summary>
    /// Selected columns in preferred display order. Empty means default/all.
    /// </summary>
    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional column key used for sorting.
    /// </summary>
    public string? SortBy { get; init; }

    /// <summary>
    /// Sort direction.
    /// </summary>
    public ToolTableSortDirection SortDirection { get; init; } = ToolTableSortDirection.Asc;

    /// <summary>
    /// Optional output row cap.
    /// </summary>
    public int? Top { get; init; }
}

/// <summary>
/// Supported sort directions for tabular tool outputs.
/// </summary>
public enum ToolTableSortDirection {
    /// <summary>
    /// Ascending.
    /// </summary>
    Asc,

    /// <summary>
    /// Descending.
    /// </summary>
    Desc
}

/// <summary>
/// Describes a selectable/sortable tabular column backed by a typed row selector.
/// </summary>
/// <typeparam name="TRow">Row type.</typeparam>
public sealed class ToolTableColumnSpec<TRow> {
    /// <summary>
    /// Initializes a new column specification.
    /// </summary>
    public ToolTableColumnSpec(ToolColumn column, Func<TRow, object?> selector, Func<object?, string>? formatter = null) {
        Column = column;
        Selector = selector ?? throw new ArgumentNullException(nameof(selector));
        Formatter = formatter;
    }

    /// <summary>
    /// Render column definition.
    /// </summary>
    public ToolColumn Column { get; }

    /// <summary>
    /// Value selector used for projection and sorting.
    /// </summary>
    public Func<TRow, object?> Selector { get; }

    /// <summary>
    /// Optional display formatter for preview markdown.
    /// </summary>
    public Func<object?, string>? Formatter { get; }
}

/// <summary>
/// Result of applying a tabular view request to typed rows.
/// </summary>
public sealed class ToolTableViewResult {
    /// <summary>
    /// Selected render columns.
    /// </summary>
    public IReadOnlyList<ToolColumn> Columns { get; init; } = Array.Empty<ToolColumn>();

    /// <summary>
    /// Projected rows (JSON-safe values).
    /// </summary>
    public JsonArray Rows { get; init; } = new();

    /// <summary>
    /// Preview rows (string cells) for summary markdown.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> PreviewRows { get; init; } = Array.Empty<IReadOnlyList<string>>();

    /// <summary>
    /// Number of projected rows.
    /// </summary>
    public int Count { get; init; }

    /// <summary>
    /// Indicates whether rows were truncated by view-level constraints.
    /// </summary>
    public bool TruncatedByView { get; init; }
}

/// <summary>
/// Shared helpers for selecting columns and applying sort/top to typed row results.
/// </summary>
public static class ToolTableView {
    /// <summary>
    /// Parses optional view arguments: columns, sort_by, sort_direction, top.
    /// </summary>
    public static bool TryParse(
        JsonObject? arguments,
        IReadOnlyList<string> allowedColumns,
        int maxTop,
        out ToolTableViewRequest request,
        out string? error) {
        request = new ToolTableViewRequest();
        error = null;

        if (allowedColumns is null || allowedColumns.Count == 0) {
            error = "No columns are available for projection.";
            return false;
        }

        var allowed = new HashSet<string>(allowedColumns.Where(static x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
        if (allowed.Count == 0) {
            error = "No columns are available for projection.";
            return false;
        }

        var requestedColumns = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("columns"));
        if (requestedColumns.Count > 0) {
            foreach (var column in requestedColumns) {
                if (!allowed.Contains(column)) {
                    error = $"columns contains unsupported value '{column}'.";
                    return false;
                }
            }
        }

        var sortBy = ToolArgs.GetOptionalTrimmed(arguments, "sort_by");
        if (!string.IsNullOrWhiteSpace(sortBy) && !allowed.Contains(sortBy)) {
            error = $"sort_by must be one of: {string.Join(", ", allowedColumns)}.";
            return false;
        }

        var sortDirection = ToolTableSortDirection.Asc;
        var rawDirection = ToolArgs.GetOptionalTrimmed(arguments, "sort_direction");
        if (!string.IsNullOrWhiteSpace(rawDirection)) {
            if (string.Equals(rawDirection, "asc", StringComparison.OrdinalIgnoreCase)) {
                sortDirection = ToolTableSortDirection.Asc;
            } else if (string.Equals(rawDirection, "desc", StringComparison.OrdinalIgnoreCase)) {
                sortDirection = ToolTableSortDirection.Desc;
            } else {
                error = "sort_direction must be one of: asc, desc.";
                return false;
            }
        }

        var top = ToolArgs.ToPositiveInt32OrNull(arguments?.GetInt64("top"), maxInclusive: maxTop <= 0 ? int.MaxValue : maxTop);

        request = new ToolTableViewRequest {
            Columns = requestedColumns,
            SortBy = sortBy,
            SortDirection = sortDirection,
            Top = top
        };
        return true;
    }

    /// <summary>
    /// Applies selection/sort/top and returns projected JSON rows plus markdown preview rows.
    /// </summary>
    public static ToolTableViewResult Apply<TRow>(
        IEnumerable<TRow> sourceRows,
        ToolTableViewRequest request,
        IReadOnlyList<ToolTableColumnSpec<TRow>> columnSpecs,
        int previewMaxRows = 20) {
        if (sourceRows is null) {
            throw new ArgumentNullException(nameof(sourceRows));
        }
        if (request is null) {
            throw new ArgumentNullException(nameof(request));
        }
        if (columnSpecs is null || columnSpecs.Count == 0) {
            throw new ArgumentException("At least one column specification is required.", nameof(columnSpecs));
        }

        var specByKey = new Dictionary<string, ToolTableColumnSpec<TRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in columnSpecs) {
            if (string.IsNullOrWhiteSpace(spec.Column.Key)) {
                continue;
            }
            specByKey[spec.Column.Key] = spec;
        }

        var selected = SelectColumns(request.Columns, columnSpecs, specByKey);

        var rows = sourceRows as IReadOnlyList<TRow> ?? sourceRows.ToList();
        IEnumerable<TRow> query = rows;

        if (!string.IsNullOrWhiteSpace(request.SortBy) && specByKey.TryGetValue(request.SortBy, out var sortSpec)) {
            query = request.SortDirection == ToolTableSortDirection.Desc
                ? query.OrderByDescending(sortSpec.Selector, ToolValueComparer.Instance)
                : query.OrderBy(sortSpec.Selector, ToolValueComparer.Instance);
        }

        var truncatedByView = false;
        if (request.Top.HasValue && request.Top.Value > 0) {
            var top = request.Top.Value;
            if (rows.Count > top) {
                truncatedByView = true;
            }
            query = query.Take(top);
        }

        var projectedRows = new JsonArray();
        var previewRows = new List<IReadOnlyList<string>>();
        var previewLimit = previewMaxRows <= 0 ? 0 : previewMaxRows;

        var count = 0;
        foreach (var row in query) {
            var obj = new JsonObject(StringComparer.Ordinal);
            var previewCells = previewRows.Count < previewLimit ? new string[selected.Count] : null;

            for (var i = 0; i < selected.Count; i++) {
                var spec = selected[i];
                var value = spec.Selector(row);
                obj.Add(spec.Column.Key, JsonMapper.FromObject(NormalizeForJson(value)));

                if (previewCells is not null) {
                    var formatted = spec.Formatter is null ? FormatValue(value) : (spec.Formatter(value) ?? string.Empty);
                    previewCells[i] = formatted;
                }
            }

            projectedRows.Add(obj);
            if (previewCells is not null) {
                previewRows.Add(previewCells);
            }
            count++;
        }

        return new ToolTableViewResult {
            Columns = selected.Select(static x => x.Column).ToList(),
            Rows = projectedRows,
            PreviewRows = previewRows,
            Count = count,
            TruncatedByView = truncatedByView
        };
    }

    private static List<ToolTableColumnSpec<TRow>> SelectColumns<TRow>(
        IReadOnlyList<string> requestedColumns,
        IReadOnlyList<ToolTableColumnSpec<TRow>> allColumns,
        IReadOnlyDictionary<string, ToolTableColumnSpec<TRow>> byKey) {
        if (requestedColumns is null || requestedColumns.Count == 0) {
            return allColumns.ToList();
        }

        var selected = new List<ToolTableColumnSpec<TRow>>(requestedColumns.Count);
        for (var i = 0; i < requestedColumns.Count; i++) {
            var key = requestedColumns[i];
            if (string.IsNullOrWhiteSpace(key)) {
                continue;
            }
            if (byKey.TryGetValue(key, out var spec)) {
                selected.Add(spec);
            }
        }

        return selected.Count == 0 ? allColumns.ToList() : selected;
    }

    private static object? NormalizeForJson(object? value) {
        return value switch {
            null => null,
            DateTime dt => dt.ToUniversalTime().ToString("O"),
            DateTimeOffset dto => dto.ToUniversalTime().ToString("O"),
            TimeSpan ts => ts.ToString(),
            _ => value
        };
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

    private sealed class ToolValueComparer : IComparer<object?> {
        public static readonly ToolValueComparer Instance = new();

        public int Compare(object? x, object? y) {
            if (ReferenceEquals(x, y)) {
                return 0;
            }
            if (x is null) {
                return 1;
            }
            if (y is null) {
                return -1;
            }

            if (TryToDecimal(x, out var xDec) && TryToDecimal(y, out var yDec)) {
                return xDec.CompareTo(yDec);
            }

            if (x is DateTime xDt && y is DateTime yDt) {
                return xDt.ToUniversalTime().CompareTo(yDt.ToUniversalTime());
            }
            if (x is DateTimeOffset xDto && y is DateTimeOffset yDto) {
                return xDto.ToUniversalTime().CompareTo(yDto.ToUniversalTime());
            }
            if (x is TimeSpan xTs && y is TimeSpan yTs) {
                return xTs.CompareTo(yTs);
            }

            if (x is IComparable xComp && y.GetType() == x.GetType()) {
                return xComp.CompareTo(y);
            }

            var xs = Convert.ToString(x, CultureInfo.InvariantCulture) ?? string.Empty;
            var ys = Convert.ToString(y, CultureInfo.InvariantCulture) ?? string.Empty;
            return StringComparer.OrdinalIgnoreCase.Compare(xs, ys);
        }

        private static bool TryToDecimal(object value, out decimal number) {
            switch (value) {
                case byte b:
                    number = b;
                    return true;
                case sbyte sb:
                    number = sb;
                    return true;
                case short s:
                    number = s;
                    return true;
                case ushort us:
                    number = us;
                    return true;
                case int i:
                    number = i;
                    return true;
                case uint ui:
                    number = ui;
                    return true;
                case long l:
                    number = l;
                    return true;
                case ulong ul:
                    number = ul;
                    return true;
                case float f:
                    number = (decimal)f;
                    return true;
                case double d:
                    number = (decimal)d;
                    return true;
                case decimal dec:
                    number = dec;
                    return true;
                default:
                    number = 0;
                    return false;
            }
        }
    }
}
