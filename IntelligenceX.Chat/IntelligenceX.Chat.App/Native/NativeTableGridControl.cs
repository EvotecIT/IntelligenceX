using System;
using System.Collections.Generic;
using IntelligenceX.Chat.App.Native.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Shared native table grid with direct header sorting, column filters, auto-fit, and resizing.
/// </summary>
internal sealed class NativeTableGridControl : UserControl {
    private const double SelectionColumnWidth = 44;
    private readonly NativeTableWorkspaceViewModel _viewModel;
    private readonly NativeTranscriptTable _table;
    private readonly int _maxRenderedRows;
    private readonly bool _showSelection;
    private readonly bool _showFilters;
    private readonly IReadOnlyList<double> _preferredWidths;
    private readonly Dictionary<int, double> _columnWidths = new();
    private readonly Dictionary<int, TextBlock> _headerLabels = new();
    private readonly Dictionary<int, TextBox> _filterBoxes = new();
    private readonly Grid _headerGrid = CreateGrid();
    private readonly Grid _filterGrid = CreateGrid();
    private readonly Grid _bodyGrid = CreateGrid();
    private readonly ScrollViewer _horizontalScroll;
    private readonly ScrollViewer _bodyScroll;
    private bool _updatingFilters;
    private bool _userResized;
    private double _lastViewportWidth;

    public NativeTableGridControl(
        NativeTranscriptTable table,
        NativeTableWorkspaceViewModel viewModel,
        int maxRenderedRows,
        bool showSelection,
        bool showFilters,
        double? maximumBodyHeight) {
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _maxRenderedRows = Math.Max(1, maxRenderedRows);
        _showSelection = showSelection;
        _showFilters = showFilters;
        _preferredWidths = NativeTableColumnLayout.MeasurePreferredWidths(table);

        _bodyScroll = new ScrollViewer {
            Content = _bodyGrid,
            VerticalScrollMode = maximumBodyHeight is > 0 ? ScrollMode.Enabled : ScrollMode.Disabled,
            VerticalScrollBarVisibility = maximumBodyHeight is > 0 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        if (maximumBodyHeight is > 0) _bodyScroll.MaxHeight = maximumBodyHeight.Value;
        var stack = new StackPanel {
            Spacing = 0,
            Children = {
                _headerGrid,
                _filterGrid,
                _bodyScroll
            }
        };
        _horizontalScroll = new ScrollViewer {
            Content = stack,
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Content = new Border {
            BorderBrush = NativeControlBrushes.BorderStrong,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = NativeControlBrushes.Surface,
            Child = _horizontalScroll
        };
        SizeChanged += OnSizeChanged;
        RebuildColumns();
    }

    public event EventHandler? ViewChanged;

    public IReadOnlyList<NativeTableWorkspaceRow> RenderedRows => _viewModel.GetVisibleWindow(_maxRenderedRows);

    public void RefreshRows() {
        RenderBodyRows();
        UpdateHeaderLabels();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RebuildColumns() {
        _headerLabels.Clear();
        _filterBoxes.Clear();
        _headerGrid.Children.Clear();
        _filterGrid.Children.Clear();
        _updatingFilters = true;
        BuildColumnDefinitions();
        BuildHeader();
        BuildFilters();
        _updatingFilters = false;
        RenderBodyRows();
        FitColumnsToViewport();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AutoFitColumns() {
        _columnWidths.Clear();
        _userResized = false;
        FitColumnsToViewport();
    }

    public void ClearFilters() {
        _viewModel.ClearColumnFilters();
        _updatingFilters = true;
        foreach (var filter in _filterBoxes) filter.Value.Text = string.Empty;
        _updatingFilters = false;
        RefreshRows();
    }

    private static Grid CreateGrid() =>
        new() {
            ColumnSpacing = 0,
            RowSpacing = 0,
            Background = NativeControlBrushes.Surface
        };

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) {
        if (_userResized || args.NewSize.Width <= 0 || Math.Abs(args.NewSize.Width - _lastViewportWidth) < 1) return;
        _lastViewportWidth = args.NewSize.Width;
        FitColumnsToViewport();
    }

    private void BuildColumnDefinitions() {
        _headerGrid.ColumnDefinitions.Clear();
        _filterGrid.ColumnDefinitions.Clear();
        _bodyGrid.ColumnDefinitions.Clear();
        if (_showSelection) AddColumnDefinition(SelectionColumnWidth);
        foreach (var sourceColumn in _viewModel.VisibleColumnIndexes) {
            AddColumnDefinition(ResolveColumnWidth(sourceColumn));
        }
    }

    private void AddColumnDefinition(double width) {
        AddColumnDefinition(_headerGrid, width);
        AddColumnDefinition(_filterGrid, width);
        AddColumnDefinition(_bodyGrid, width);
    }

    private static void AddColumnDefinition(Grid grid, double width) =>
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });

    private void BuildHeader() {
        _headerGrid.RowDefinitions.Clear();
        _headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var targetColumn = 0;
        if (_showSelection) {
            AddCell(_headerGrid, new TextBlock(), 0, targetColumn++, isHeader: true);
        }

        foreach (var sourceColumn in _viewModel.VisibleColumnIndexes) {
            var column = sourceColumn;
            var label = new TextBlock {
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = NativeControlBrushes.Accent,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            _headerLabels[column] = label;
            var sort = new Button {
                Content = label,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 7, 18, 7),
                MinHeight = 34
            };
            ToolTipService.SetToolTip(sort, "Sort by " + _viewModel.Headers[column]);
            sort.Click += (_, _) => {
                _viewModel.SortByColumn(column);
                RefreshRows();
            };

            var resize = new Thumb {
                Width = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent)
            };
            resize.DragDelta += (_, args) => ResizeColumn(column, args.HorizontalChange);
            var host = new Grid {
                Children = { sort, resize }
            };
            AddCell(_headerGrid, host, 0, targetColumn++, isHeader: true);
        }

        UpdateHeaderLabels();
    }

    private void BuildFilters() {
        _filterGrid.Visibility = _showFilters ? Visibility.Visible : Visibility.Collapsed;
        _filterGrid.RowDefinitions.Clear();
        if (!_showFilters) return;
        _filterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var targetColumn = 0;
        if (_showSelection) {
            AddCell(_filterGrid, new TextBlock(), 0, targetColumn++, isHeader: false, isFilter: true);
        }

        foreach (var sourceColumn in _viewModel.VisibleColumnIndexes) {
            var column = sourceColumn;
            var filter = new TextBox {
                Text = _viewModel.GetColumnFilter(column),
                PlaceholderText = "Filter " + _viewModel.Headers[column],
                FontSize = 11,
                MinHeight = 30,
                Padding = new Thickness(7, 3, 7, 3),
                Background = NativeControlBrushes.Surface,
                BorderBrush = NativeControlBrushes.Border,
                Foreground = NativeControlBrushes.TextPrimary,
                PlaceholderForeground = NativeControlBrushes.TextMuted
            };
            _filterBoxes[column] = filter;
            filter.TextChanged += (_, _) => {
                if (_updatingFilters) return;
                _viewModel.SetColumnFilter(column, filter.Text);
                RenderBodyRows();
                ViewChanged?.Invoke(this, EventArgs.Empty);
            };
            AddCell(_filterGrid, filter, 0, targetColumn++, isHeader: false, isFilter: true);
        }
    }

    private void UpdateHeaderLabels() {
        foreach (var header in _headerLabels) {
            var glyph = _viewModel.SortColumnIndex == header.Key
                ? _viewModel.SortDescending ? "  ↓" : "  ↑"
                : "  ↕";
            header.Value.Text = _viewModel.Headers[header.Key] + glyph;
        }
    }

    private void RenderBodyRows() {
        _bodyGrid.Children.Clear();
        _bodyGrid.RowDefinitions.Clear();
        var rendered = _viewModel.GetVisibleWindow(_maxRenderedRows);
        for (var rowIndex = 0; rowIndex < rendered.Count; rowIndex++) {
            _bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var targetColumn = 0;
            var row = rendered[rowIndex];
            if (_showSelection) {
                var selector = new CheckBox {
                    IsChecked = _viewModel.IsRowSelected(row),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                selector.Checked += (_, _) => {
                    _viewModel.SetRowSelected(row, selected: true);
                    ViewChanged?.Invoke(this, EventArgs.Empty);
                };
                selector.Unchecked += (_, _) => {
                    _viewModel.SetRowSelected(row, selected: false);
                    ViewChanged?.Invoke(this, EventArgs.Empty);
                };
                AddCell(_bodyGrid, selector, rowIndex, targetColumn++, isHeader: false, alternate: rowIndex % 2 == 1);
            }

            foreach (var sourceColumn in _viewModel.VisibleColumnIndexes) {
                var text = sourceColumn < row.Cells.Count ? row.Cells[sourceColumn] : string.Empty;
                AddCell(
                    _bodyGrid,
                    new TextBlock {
                        Text = text,
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true,
                        FontSize = 12,
                        Foreground = NativeControlBrushes.TextPrimary
                    },
                    rowIndex,
                    targetColumn++,
                    isHeader: false,
                    alternate: rowIndex % 2 == 1);
            }
        }
    }

    private static void AddCell(
        Grid grid,
        UIElement content,
        int row,
        int column,
        bool isHeader,
        bool isFilter = false,
        bool alternate = false) {
        var cell = new Border {
            Padding = isHeader ? new Thickness(0) : new Thickness(7, 5, 7, 5),
            BorderBrush = NativeControlBrushes.Border,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Background = isHeader
                ? NativeControlBrushes.AccentSoft
                : isFilter
                    ? NativeControlBrushes.SurfaceMuted
                    : alternate ? NativeControlBrushes.SurfaceMuted : NativeControlBrushes.Surface,
            Child = content
        };
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private void ResizeColumn(int sourceColumn, double delta) {
        _userResized = true;
        var width = NativeTableColumnLayout.Resize(ResolveColumnWidth(sourceColumn), delta);
        _columnWidths[sourceColumn] = width;
        ApplyColumnWidths();
    }

    private void FitColumnsToViewport() {
        var visible = _viewModel.VisibleColumnIndexes;
        var preferred = new double[visible.Count];
        for (var index = 0; index < visible.Count; index++) {
            var sourceColumn = visible[index];
            preferred[index] = sourceColumn < _preferredWidths.Count
                ? _preferredWidths[sourceColumn]
                : NativeTableColumnLayout.MinimumWidth;
        }

        var viewport = ActualWidth > 0 ? ActualWidth : preferred.Length * NativeTableColumnLayout.MinimumWidth;
        var fitted = NativeTableColumnLayout.FitToViewport(preferred, viewport, _showSelection ? SelectionColumnWidth : 0);
        for (var index = 0; index < visible.Count; index++) _columnWidths[visible[index]] = fitted[index];
        ApplyColumnWidths();
    }

    private double ResolveColumnWidth(int sourceColumn) {
        if (_columnWidths.TryGetValue(sourceColumn, out var width)) return width;
        return sourceColumn < _preferredWidths.Count ? _preferredWidths[sourceColumn] : NativeTableColumnLayout.MinimumWidth;
    }

    private void ApplyColumnWidths() {
        var offset = _showSelection ? 1 : 0;
        var visible = _viewModel.VisibleColumnIndexes;
        for (var index = 0; index < visible.Count; index++) {
            var width = new GridLength(ResolveColumnWidth(visible[index]));
            _headerGrid.ColumnDefinitions[index + offset].Width = width;
            _filterGrid.ColumnDefinitions[index + offset].Width = width;
            _bodyGrid.ColumnDefinitions[index + offset].Width = width;
        }
    }
}
