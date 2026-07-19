using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IntelligenceX.Chat.App.Native.Rendering;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Native table workspace state for search, sort, and copy/export projections.
/// </summary>
internal sealed class NativeTableWorkspaceViewModel {
    private readonly List<NativeTableWorkspaceRow> _sourceRows;
    private readonly HashSet<int> _selectedSourceIndexes = new();
    private readonly HashSet<int> _hiddenColumnIndexes = new();
    private readonly Dictionary<int, string> _columnFilters = new();
    private int _windowStartIndex;
    private string _searchText = string.Empty;

    public NativeTableWorkspaceViewModel(NativeTranscriptTable table) {
        if (table == null) throw new ArgumentNullException(nameof(table));
        Headers = table.Headers;
        _sourceRows = table.Rows
            .Select((row, index) => new NativeTableWorkspaceRow(index, row))
            .ToList();
        VisibleRows = _sourceRows;
    }

    public IReadOnlyList<string> Headers { get; }

    public IReadOnlyList<NativeTableWorkspaceRow> VisibleRows { get; private set; }

    public string SearchText => _searchText;

    public int? SortColumnIndex { get; private set; }

    public bool SortDescending { get; private set; }

    public int TotalRowCount => _sourceRows.Count;

    public int SelectedRowCount => _selectedSourceIndexes.Count;

    public int WindowStartIndex => _windowStartIndex;

    public IReadOnlyList<int> VisibleColumnIndexes => GetVisibleColumnIndexes();

    public int ActiveColumnFilterCount => _columnFilters.Count;

    public void SetSearch(string? value) {
        _searchText = (value ?? string.Empty).Trim();
        Refresh(resetWindow: true);
    }

    public void SortByColumn(int columnIndex) {
        if (columnIndex < 0 || columnIndex >= Headers.Count) {
            return;
        }

        if (SortColumnIndex == columnIndex) {
            SortDescending = !SortDescending;
        } else {
            SortColumnIndex = columnIndex;
            SortDescending = false;
        }

        Refresh(resetWindow: true);
    }

    public void SetSortDirection(bool descending) {
        if (!SortColumnIndex.HasValue || SortDescending == descending) {
            return;
        }

        SortDescending = descending;
        Refresh(resetWindow: true);
    }

    public void ClearSort() {
        if (!SortColumnIndex.HasValue) {
            return;
        }

        SortColumnIndex = null;
        SortDescending = false;
        Refresh(resetWindow: true);
    }

    public bool IsRowSelected(NativeTableWorkspaceRow row) {
        if (row == null) throw new ArgumentNullException(nameof(row));
        return _selectedSourceIndexes.Contains(row.SourceIndex);
    }

    public void SetRowSelected(NativeTableWorkspaceRow row, bool selected) {
        if (row == null) throw new ArgumentNullException(nameof(row));
        if (selected) {
            _selectedSourceIndexes.Add(row.SourceIndex);
        } else {
            _selectedSourceIndexes.Remove(row.SourceIndex);
        }
    }

    public void SelectVisibleRows() {
        foreach (var row in VisibleRows) {
            _selectedSourceIndexes.Add(row.SourceIndex);
        }
    }

    public void ClearSelection() =>
        _selectedSourceIndexes.Clear();

    public bool IsColumnVisible(int columnIndex) =>
        columnIndex >= 0
        && columnIndex < Headers.Count
        && !_hiddenColumnIndexes.Contains(columnIndex);

    public void SetColumnVisible(int columnIndex, bool visible) {
        if (columnIndex < 0 || columnIndex >= Headers.Count) {
            return;
        }

        if (visible) {
            if (_hiddenColumnIndexes.Remove(columnIndex)) {
                Refresh(resetWindow: true);
            }
            return;
        }

        if (VisibleColumnIndexes.Count <= 1) {
            return;
        }

        _hiddenColumnIndexes.Add(columnIndex);
        _columnFilters.Remove(columnIndex);
        if (SortColumnIndex == columnIndex) {
            SortColumnIndex = null;
            SortDescending = false;
        }
        Refresh(resetWindow: true);
    }

    public void ResetColumnVisibility() {
        _hiddenColumnIndexes.Clear();
        Refresh(resetWindow: true);
    }

    public string GetColumnFilter(int columnIndex) =>
        _columnFilters.TryGetValue(columnIndex, out var value) ? value : string.Empty;

    public void SetColumnFilter(int columnIndex, string? value) {
        if (columnIndex < 0 || columnIndex >= Headers.Count) {
            return;
        }

        var filter = (value ?? string.Empty).Trim();
        if (filter.Length == 0) {
            _columnFilters.Remove(columnIndex);
        } else {
            _columnFilters[columnIndex] = filter;
        }

        Refresh(resetWindow: true);
    }

    public void ClearColumnFilters() {
        if (_columnFilters.Count == 0) {
            return;
        }

        _columnFilters.Clear();
        Refresh(resetWindow: true);
    }

    public IReadOnlyList<NativeTableWorkspaceRow> GetVisibleWindow(int windowSize) {
        var size = NormalizeWindowSize(windowSize);
        ClampWindowStart(size);
        return VisibleRows.Skip(_windowStartIndex).Take(size).ToArray();
    }

    public int GetVisibleWindowEndExclusive(int windowSize) {
        var size = NormalizeWindowSize(windowSize);
        ClampWindowStart(size);
        return Math.Min(VisibleRows.Count, _windowStartIndex + size);
    }

    public bool CanMoveWindowBackward() =>
        _windowStartIndex > 0;

    public bool CanMoveWindowForward(int windowSize) =>
        GetVisibleWindowEndExclusive(windowSize) < VisibleRows.Count;

    public void MoveWindowBackward(int windowSize) {
        var size = NormalizeWindowSize(windowSize);
        _windowStartIndex = Math.Max(0, _windowStartIndex - size);
    }

    public void MoveWindowForward(int windowSize) {
        var size = NormalizeWindowSize(windowSize);
        _windowStartIndex = Math.Min(GetMaxWindowStart(size), _windowStartIndex + size);
    }

    public string BuildVisibleTsv(bool includeHeaders = true) =>
        BuildTsv(VisibleRows, includeHeaders);

    public string BuildAllTsv(bool includeHeaders = true) =>
        BuildTsv(_sourceRows, includeHeaders);

    public string BuildVisibleCsv(bool includeHeaders = true) =>
        BuildCsv(VisibleRows, includeHeaders);

    public string BuildAllCsv(bool includeHeaders = true) =>
        BuildCsv(_sourceRows, includeHeaders);

    public string BuildExportText(NativeTableExportFormat format, NativeTableExportScope scope, bool includeHeaders = true) {
        var rows = scope switch {
            NativeTableExportScope.VisibleRows => VisibleRows,
            NativeTableExportScope.AllRows => _sourceRows,
            NativeTableExportScope.SelectedRows => GetSelectedRows(),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null)
        };

        return format switch {
            NativeTableExportFormat.Tsv => BuildTsv(rows, includeHeaders),
            NativeTableExportFormat.Csv => BuildCsv(rows, includeHeaders),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    private void Refresh(bool resetWindow = false) {
        IEnumerable<NativeTableWorkspaceRow> rows = _sourceRows;
        if (_searchText.Length > 0) {
            var visibleColumns = VisibleColumnIndexes;
            rows = rows.Where(row => visibleColumns.Any(column =>
                column < row.Cells.Count
                && row.Cells[column].Contains(_searchText, StringComparison.OrdinalIgnoreCase)));
        }

        if (_columnFilters.Count > 0) {
            rows = rows.Where(MatchesColumnFilters);
        }

        if (SortColumnIndex.HasValue) {
            var column = SortColumnIndex.Value;
            rows = SortDescending
                ? rows.OrderByDescending(row => GetSortKey(row, column), NativeTableValueComparer.Instance).ThenBy(row => row.SourceIndex)
                : rows.OrderBy(row => GetSortKey(row, column), NativeTableValueComparer.Instance).ThenBy(row => row.SourceIndex);
        }

        VisibleRows = rows.ToArray();
        if (resetWindow) {
            _windowStartIndex = 0;
        } else {
            ClampWindowStart(windowSize: 1);
        }
    }

    private IReadOnlyList<NativeTableWorkspaceRow> GetSelectedRows() =>
        _sourceRows.Where(row => _selectedSourceIndexes.Contains(row.SourceIndex)).ToArray();

    private bool MatchesColumnFilters(NativeTableWorkspaceRow row) {
        foreach (var filter in _columnFilters) {
            var cell = filter.Key < row.Cells.Count ? row.Cells[filter.Key] : string.Empty;
            if (!cell.Contains(filter.Value, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
        }

        return true;
    }

    private IReadOnlyList<int> GetVisibleColumnIndexes() {
        if (Headers.Count == 0) {
            return Array.Empty<int>();
        }

        return Enumerable.Range(0, Headers.Count)
            .Where(index => !_hiddenColumnIndexes.Contains(index))
            .ToArray();
    }

    private void ClampWindowStart(int windowSize) =>
        _windowStartIndex = Math.Min(_windowStartIndex, GetMaxWindowStart(windowSize));

    private int GetMaxWindowStart(int windowSize) {
        var size = NormalizeWindowSize(windowSize);
        if (VisibleRows.Count <= size) {
            return 0;
        }

        return ((VisibleRows.Count - 1) / size) * size;
    }

    private static int NormalizeWindowSize(int windowSize) =>
        Math.Max(1, windowSize);

    private string BuildTsv(IReadOnlyList<NativeTableWorkspaceRow> rows, bool includeHeaders) {
        return DelimitedTextFormatter.FormatTsv(ProjectRows(rows, includeHeaders));
    }

    private string BuildCsv(IReadOnlyList<NativeTableWorkspaceRow> rows, bool includeHeaders) {
        return DelimitedTextFormatter.FormatCsv(ProjectRows(rows, includeHeaders));
    }

    private IEnumerable<IReadOnlyList<string>> ProjectRows(
        IReadOnlyList<NativeTableWorkspaceRow> rows,
        bool includeHeaders) {
        var columns = VisibleColumnIndexes;
        if (includeHeaders && columns.Count > 0) {
            yield return ProjectHeaderCells(columns);
        }

        foreach (var row in rows) {
            yield return ProjectRowCells(row, columns);
        }
    }

    private IReadOnlyList<string> ProjectHeaderCells(IReadOnlyList<int> columns) =>
        columns.Select(index => Headers[index]).ToArray();

    private static IReadOnlyList<string> ProjectRowCells(NativeTableWorkspaceRow row, IReadOnlyList<int> columns) =>
        columns.Select(index => index < row.Cells.Count ? row.Cells[index] : string.Empty).ToArray();

    private static string GetSortKey(NativeTableWorkspaceRow row, int columnIndex) =>
        columnIndex < row.Cells.Count ? row.Cells[columnIndex] : string.Empty;
}

internal enum NativeTableExportFormat {
    Tsv,
    Csv
}

internal enum NativeTableExportScope {
    VisibleRows,
    AllRows,
    SelectedRows
}

/// <summary>
/// One native table workspace row.
/// </summary>
internal sealed class NativeTableWorkspaceRow {
    public NativeTableWorkspaceRow(int sourceIndex, IReadOnlyList<string> cells) {
        SourceIndex = sourceIndex;
        Cells = cells ?? Array.Empty<string>();
    }

    public int SourceIndex { get; }

    public IReadOnlyList<string> Cells { get; }

    public string OrdinalText => (SourceIndex + 1).ToString(CultureInfo.InvariantCulture);
}
