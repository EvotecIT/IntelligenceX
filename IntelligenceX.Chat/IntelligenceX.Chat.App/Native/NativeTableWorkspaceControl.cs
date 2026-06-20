using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Chat.App.Native.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Native table workspace surface for transcript table artifacts.
/// </summary>
internal sealed class NativeTableWorkspaceControl : UserControl {
    private const int MaxRenderedRows = 100;
    private readonly NativeTableWorkspaceViewModel _viewModel;
    private readonly TextBlock _summaryText;
    private readonly ComboBox _sortColumn;
    private readonly Button _sortDirection;
    private readonly Button _previousWindow;
    private readonly Button _nextWindow;
    private readonly Grid _grid;

    public NativeTableWorkspaceControl(NativeTranscriptTable table) {
        _viewModel = new NativeTableWorkspaceViewModel(table);
        _summaryText = new TextBlock {
            FontSize = 12,
            Foreground = NativeControlBrushes.Rgb(90, 105, 124)
        };
        _sortColumn = BuildSortColumnPicker(_viewModel.Headers);
        _sortDirection = BuildSortDirectionButton();
        _previousWindow = BuildWindowButton("Previous rows", moveForward: false);
        _nextWindow = BuildWindowButton("Next rows", moveForward: true);
        _grid = new Grid {
            BorderBrush = NativeControlBrushes.Rgb(214, 222, 232),
            BorderThickness = new Thickness(1)
        };

        Content = Build();
        RenderGrid();
    }

    private FrameworkElement Build() {
        var search = new TextBox {
            PlaceholderText = "Search rows",
            MinWidth = 220
        };
        search.TextChanged += (_, _) => {
            _viewModel.SetSearch(search.Text);
            RenderGrid();
        };

        _sortColumn.SelectionChanged += (_, _) => {
            if (_sortColumn.SelectedIndex > 0) {
                _viewModel.SortByColumn(_sortColumn.SelectedIndex - 1);
            } else {
                _viewModel.ClearSort();
            }

            UpdateSortDirectionButton();
            RenderGrid();
        };

        _sortDirection.Click += (_, _) => {
            _viewModel.SetSortDirection(!_viewModel.SortDescending);
            UpdateSortDirectionButton();
            RenderGrid();
        };

        var toolbar = new StackPanel {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = {
                search,
                _sortColumn,
                _sortDirection,
                _previousWindow,
                _nextWindow,
                BuildSelectVisibleButton(),
                BuildClearSelectionButton(),
                BuildCopyButton("Copy visible TSV", NativeTableExportFormat.Tsv, NativeTableExportScope.VisibleRows),
                BuildCopyButton("Copy selected TSV", NativeTableExportFormat.Tsv, NativeTableExportScope.SelectedRows),
                BuildCopyButton("Copy all TSV", NativeTableExportFormat.Tsv, NativeTableExportScope.AllRows),
                BuildCopyButton("Copy visible CSV", NativeTableExportFormat.Csv, NativeTableExportScope.VisibleRows),
                BuildCopyButton("Copy selected CSV", NativeTableExportFormat.Csv, NativeTableExportScope.SelectedRows),
                BuildCopyButton("Copy all CSV", NativeTableExportFormat.Csv, NativeTableExportScope.AllRows)
            }
        };
        var columns = BuildColumnVisibilityControls();
        var filters = BuildColumnFilterControls();

        return new StackPanel {
            Spacing = 10,
            Children = {
                toolbar,
                columns,
                filters,
                _summaryText,
                new ScrollViewer {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 460,
                    Content = _grid
                }
            }
        };
    }

    private static ComboBox BuildSortColumnPicker(IReadOnlyList<string> headers) {
        return BuildColumnPicker(headers, "Sort by...", "Sort");
    }

    private static ComboBox BuildColumnPicker(IReadOnlyList<string> headers, string firstItem, string placeholder) {
        var combo = new ComboBox {
            MinWidth = 180,
            PlaceholderText = placeholder
        };
        combo.Items.Add(firstItem);
        foreach (var header in headers) {
            combo.Items.Add(header);
        }

        combo.SelectedIndex = 0;
        return combo;
    }

    private static Button BuildSortDirectionButton() =>
        new() {
            Content = "Ascending",
            IsEnabled = false,
            MinWidth = 96
        };

    private void UpdateSortDirectionButton() {
        _sortDirection.IsEnabled = _viewModel.SortColumnIndex.HasValue;
        _sortDirection.Content = _viewModel.SortDescending ? "Descending" : "Ascending";
    }

    private Button BuildCopyButton(string label, NativeTableExportFormat format, NativeTableExportScope scope) {
        var button = new Button {
            Content = label,
            MinWidth = 116
        };
        button.Click += (_, _) => CopyToClipboard(_viewModel.BuildExportText(format, scope));
        return button;
    }

    private StackPanel BuildColumnVisibilityControls() {
        var panel = new StackPanel {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        var reset = new Button {
            Content = "Reset columns",
            MinWidth = 108
        };
        reset.Click += (_, _) => {
            _viewModel.ResetColumnVisibility();
            foreach (var child in panel.Children.OfType<CheckBox>()) {
                child.IsChecked = true;
            }

            RenderGrid();
        };
        panel.Children.Add(reset);

        for (var index = 0; index < _viewModel.Headers.Count; index++) {
            var columnIndex = index;
            var column = new CheckBox {
                Content = _viewModel.Headers[index],
                IsChecked = _viewModel.IsColumnVisible(index),
                VerticalAlignment = VerticalAlignment.Center
            };
            column.Checked += (_, _) => {
                _viewModel.SetColumnVisible(columnIndex, visible: true);
                RenderGrid();
            };
            column.Unchecked += (_, _) => {
                _viewModel.SetColumnVisible(columnIndex, visible: false);
                if (_viewModel.IsColumnVisible(columnIndex)) {
                    column.IsChecked = true;
                }

                RenderGrid();
            };
            panel.Children.Add(column);
        }

        return panel;
    }

    private StackPanel BuildColumnFilterControls() {
        var updating = false;
        var columnPicker = BuildColumnPicker(_viewModel.Headers, "Filter column...", "Column");
        var filterText = new TextBox {
            PlaceholderText = "Filter selected column",
            MinWidth = 220,
            IsEnabled = false
        };
        columnPicker.SelectionChanged += (_, _) => {
            updating = true;
            if (columnPicker.SelectedIndex > 0) {
                var columnIndex = columnPicker.SelectedIndex - 1;
                filterText.IsEnabled = true;
                filterText.Text = _viewModel.GetColumnFilter(columnIndex);
            } else {
                filterText.IsEnabled = false;
                filterText.Text = string.Empty;
            }

            updating = false;
        };
        filterText.TextChanged += (_, _) => {
            if (updating || columnPicker.SelectedIndex <= 0) {
                return;
            }

            _viewModel.SetColumnFilter(columnPicker.SelectedIndex - 1, filterText.Text);
            RenderGrid();
        };

        var clear = new Button {
            Content = "Clear filters",
            MinWidth = 96
        };
        clear.Click += (_, _) => {
            _viewModel.ClearColumnFilters();
            updating = true;
            filterText.Text = columnPicker.SelectedIndex > 0
                ? _viewModel.GetColumnFilter(columnPicker.SelectedIndex - 1)
                : string.Empty;
            updating = false;
            RenderGrid();
        };

        return new StackPanel {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = {
                columnPicker,
                filterText,
                clear
            }
        };
    }

    private Button BuildWindowButton(string label, bool moveForward) {
        var button = new Button {
            Content = label,
            MinWidth = 108
        };
        button.Click += (_, _) => {
            if (moveForward) {
                _viewModel.MoveWindowForward(MaxRenderedRows);
            } else {
                _viewModel.MoveWindowBackward(MaxRenderedRows);
            }

            RenderGrid();
        };
        return button;
    }

    private Button BuildSelectVisibleButton() {
        var button = new Button {
            Content = "Select visible",
            MinWidth = 108
        };
        button.Click += (_, _) => {
            _viewModel.SelectVisibleRows();
            RenderGrid();
        };
        return button;
    }

    private Button BuildClearSelectionButton() {
        var button = new Button {
            Content = "Clear selection",
            MinWidth = 112
        };
        button.Click += (_, _) => {
            _viewModel.ClearSelection();
            RenderGrid();
        };
        return button;
    }

    private void RenderGrid() {
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        _grid.ColumnDefinitions.Clear();

        var visibleColumns = _viewModel.VisibleColumnIndexes;
        var dataColumnCount = Math.Max(1, visibleColumns.Count);
        var totalColumnCount = dataColumnCount + 1;
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var column = 0; column < dataColumnCount; column++) {
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        AddHeaderRow(visibleColumns);
        var windowRows = _viewModel.GetVisibleWindow(MaxRenderedRows);
        for (var row = 0; row < windowRows.Count; row++) {
            AddDataRow(windowRows[row], row + 1, visibleColumns, totalColumnCount);
        }

        _previousWindow.IsEnabled = _viewModel.CanMoveWindowBackward();
        _nextWindow.IsEnabled = _viewModel.CanMoveWindowForward(MaxRenderedRows);

        var windowStart = _viewModel.VisibleRows.Count == 0 ? 0 : _viewModel.WindowStartIndex + 1;
        var windowEnd = _viewModel.GetVisibleWindowEndExclusive(MaxRenderedRows);
        var range = windowStart.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "-"
            + windowEnd.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var visible = _viewModel.VisibleRows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var total = _viewModel.TotalRowCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var selected = _viewModel.SelectedRowCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var filters = _viewModel.ActiveColumnFilterCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _summaryText.Text = "Showing " + range + " of " + visible + " visible rows, " + total + " total rows, " + selected + " selected, " + filters + " column filters";
    }

    private void AddHeaderRow(IReadOnlyList<int> visibleColumns) {
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddGridCell(new TextBlock { Text = string.Empty }, rowIndex: 0, columnIndex: 0, isHeader: true, isLastColumn: false);
        for (var column = 0; column < visibleColumns.Count; column++) {
            var sourceColumn = visibleColumns[column];
            AddGridCell(
                new TextBlock {
                    Text = sourceColumn < _viewModel.Headers.Count ? _viewModel.Headers[sourceColumn] : string.Empty,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = NativeControlBrushes.Rgb(36, 49, 68)
                },
                rowIndex: 0,
                columnIndex: column + 1,
                isHeader: true,
                isLastColumn: column + 1 == visibleColumns.Count);
        }
    }

    private void AddDataRow(NativeTableWorkspaceRow row, int rowIndex, IReadOnlyList<int> visibleColumns, int totalColumnCount) {
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var selector = new CheckBox {
            IsChecked = _viewModel.IsRowSelected(row),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        selector.Checked += (_, _) => {
            _viewModel.SetRowSelected(row, selected: true);
            RenderGrid();
        };
        selector.Unchecked += (_, _) => {
            _viewModel.SetRowSelected(row, selected: false);
            RenderGrid();
        };
        AddGridCell(selector, rowIndex, columnIndex: 0, isHeader: false, isLastColumn: false);

        for (var column = 0; column < visibleColumns.Count; column++) {
            var sourceColumn = visibleColumns[column];
            AddGridCell(
                new TextBlock {
                    Text = sourceColumn < row.Cells.Count ? row.Cells[sourceColumn] : string.Empty,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.Normal,
                    Foreground = NativeControlBrushes.Rgb(36, 49, 68)
                },
                rowIndex,
                columnIndex: column + 1,
                isHeader: false,
                isLastColumn: column + 2 == totalColumnCount);
        }
    }

    private void AddGridCell(UIElement content, int rowIndex, int columnIndex, bool isHeader, bool isLastColumn) {
        var cell = new Border {
            Padding = new Thickness(8, 6, 8, 6),
            BorderBrush = NativeControlBrushes.Rgb(226, 232, 240),
            BorderThickness = new Thickness(0, 0, isLastColumn ? 0 : 1, 1),
            Background = isHeader ? NativeControlBrushes.Rgb(241, 245, 249) : NativeControlBrushes.Rgb(255, 255, 255),
            Child = content
        };
        Grid.SetColumn(cell, columnIndex);
        Grid.SetRow(cell, rowIndex);
        _grid.Children.Add(cell);
    }

    private static void CopyToClipboard(string text) {
        var package = new DataPackage();
        package.SetText(text ?? string.Empty);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }
}
