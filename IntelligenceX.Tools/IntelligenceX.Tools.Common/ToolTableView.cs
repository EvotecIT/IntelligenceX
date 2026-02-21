using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using IntelligenceX.Json;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Common request contract for tabular tool result shaping.
/// </summary>
public sealed class ToolTableViewRequest {
    private IReadOnlyList<string> _columns = Array.Empty<string>();

    /// <summary>
    /// Selected columns in preferred display order. Empty means default/all.
    /// </summary>
    public IReadOnlyList<string> Columns {
        get => _columns;
        init => _columns = NormalizeColumns(value);
    }

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

    private static IReadOnlyList<string> NormalizeColumns(IReadOnlyList<string>? columns) {
        if (columns is null || columns.Count == 0) {
            return Array.Empty<string>();
        }

        var normalized = new List<string>(columns.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < columns.Count; i++) {
            var column = columns[i];
            if (string.IsNullOrWhiteSpace(column)) {
                continue;
            }

            var trimmed = column.Trim();
            if (seen.Add(trimmed)) {
                normalized.Add(trimmed);
            }
        }

        if (normalized.Count == 0) {
            return Array.Empty<string>();
        }

        return new ReadOnlyCollection<string>(normalized);
    }
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
    private IReadOnlyList<ToolColumn> _columns = Array.Empty<ToolColumn>();
    private IReadOnlyList<IReadOnlyList<string>> _previewRows = Array.Empty<IReadOnlyList<string>>();

    /// <summary>
    /// Selected render columns.
    /// </summary>
    public IReadOnlyList<ToolColumn> Columns {
        get => _columns;
        init => _columns = NormalizeColumns(value);
    }

    /// <summary>
    /// Projected rows (JSON-safe values).
    /// </summary>
    public JsonArray Rows { get; init; } = new();

    /// <summary>
    /// Preview rows (string cells) for summary markdown.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> PreviewRows {
        get => _previewRows;
        init => _previewRows = NormalizePreviewRows(value);
    }

    /// <summary>
    /// Number of projected rows.
    /// </summary>
    public int Count { get; init; }

    /// <summary>
    /// Indicates whether rows were truncated by view-level constraints.
    /// </summary>
    public bool TruncatedByView { get; init; }

    private static IReadOnlyList<ToolColumn> NormalizeColumns(IReadOnlyList<ToolColumn>? columns) {
        if (columns is null || columns.Count == 0) {
            return Array.Empty<ToolColumn>();
        }

        return new ReadOnlyCollection<ToolColumn>(columns.ToList());
    }

    private static IReadOnlyList<IReadOnlyList<string>> NormalizePreviewRows(IReadOnlyList<IReadOnlyList<string>>? previewRows) {
        if (previewRows is null || previewRows.Count == 0) {
            return Array.Empty<IReadOnlyList<string>>();
        }

        var normalized = new List<IReadOnlyList<string>>(previewRows.Count);
        for (var i = 0; i < previewRows.Count; i++) {
            var row = previewRows[i];
            if (row is null || row.Count == 0) {
                normalized.Add(Array.Empty<string>());
                continue;
            }

            normalized.Add(new ReadOnlyCollection<string>(row.Select(static cell => cell ?? string.Empty).ToList()));
        }

        return new ReadOnlyCollection<IReadOnlyList<string>>(normalized);
    }
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

        var allowedList = allowedColumns
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var allowed = new HashSet<string>(allowedList, StringComparer.OrdinalIgnoreCase);
        if (allowed.Count == 0) {
            error = "No columns are available for projection.";
            return false;
        }

        var requestedColumnsRaw = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("columns"));
        var requestedColumns = new List<string>(requestedColumnsRaw.Count);
        var seenRequestedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (requestedColumnsRaw.Count > 0) {
            foreach (var column in requestedColumnsRaw) {
                if (!TryResolveAllowedColumnKey(column, allowedList, allowed, out var resolved)) {
                    error = $"columns contains unsupported value '{column}'.";
                    return false;
                }

                if (seenRequestedColumns.Add(resolved)) {
                    requestedColumns.Add(resolved);
                }
            }
        }

        var sortBy = ToolArgs.GetOptionalTrimmed(arguments, "sort_by");
        if (!string.IsNullOrWhiteSpace(sortBy)) {
            if (!TryResolveAllowedColumnKey(sortBy, allowedList, allowed, out var resolvedSortBy)) {
                error = $"sort_by must be one of: {string.Join(", ", allowedColumns)}.";
                return false;
            }

            sortBy = resolvedSortBy;
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

    private static bool TryResolveAllowedColumnKey(
        string? requested,
        IReadOnlyList<string> allowedList,
        IReadOnlySet<string> allowedSet,
        out string resolved) {
        resolved = string.Empty;
        var requestedTrimmed = (requested ?? string.Empty).Trim();
        if (requestedTrimmed.Length == 0) {
            return false;
        }

        if (allowedSet.Contains(requestedTrimmed)) {
            // Keep canonical casing/spelling from allowed columns.
            for (var i = 0; i < allowedList.Count; i++) {
                if (string.Equals(allowedList[i], requestedTrimmed, StringComparison.OrdinalIgnoreCase)) {
                    resolved = allowedList[i];
                    return true;
                }
            }

            resolved = requestedTrimmed;
            return true;
        }

        var requestedCanonical = CanonicalizeColumnKey(requestedTrimmed);
        if (requestedCanonical.Length == 0) {
            return false;
        }

        var canonicalMatches = FindCanonicalMatches(requestedCanonical, allowedList, requireSuffixMatch: false);
        if (canonicalMatches.Count == 1) {
            resolved = canonicalMatches[0];
            return true;
        }
        if (canonicalMatches.Count > 1) {
            return false;
        }

        // Fuzzy fallback for common model aliases like "deprecated" -> "is_deprecated".
        var suffixMatches = FindCanonicalMatches(requestedCanonical, allowedList, requireSuffixMatch: true);
        if (suffixMatches.Count == 1) {
            resolved = suffixMatches[0];
            return true;
        }

        return false;
    }

    private static List<string> FindCanonicalMatches(string requestedCanonical, IReadOnlyList<string> allowedList, bool requireSuffixMatch) {
        var matches = new List<string>();
        for (var i = 0; i < allowedList.Count; i++) {
            var candidate = allowedList[i];
            var candidateCanonical = CanonicalizeColumnKey(candidate);
            if (candidateCanonical.Length == 0) {
                continue;
            }

            if (!requireSuffixMatch) {
                if (string.Equals(candidateCanonical, requestedCanonical, StringComparison.OrdinalIgnoreCase)) {
                    matches.Add(candidate);
                }
                continue;
            }

            if (candidateCanonical.Length >= requestedCanonical.Length
                && candidateCanonical.EndsWith(requestedCanonical, StringComparison.OrdinalIgnoreCase)) {
                matches.Add(candidate);
            }
        }

        return matches;
    }

    private static string CanonicalizeColumnKey(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[value.Length];
        var index = 0;
        for (var i = 0; i < value.Length; i++) {
            var ch = value[i];
            if (!char.IsLetterOrDigit(ch)) {
                continue;
            }

            buffer[index++] = char.ToLowerInvariant(ch);
        }

        return index == 0 ? string.Empty : new string(buffer[..index]);
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
