using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IntelligenceX.Chat.App.Native.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Compact native preview for a projected Markdown table.
/// </summary>
internal sealed class NativeTranscriptTablePreviewControl : UserControl {
    private const int MaxPreviewRows = 6;
    private readonly NativeTranscriptTable _table;
    private readonly string _title;
    private readonly NativeTableWorkspaceViewModel _viewModel;
    private readonly Grid _grid;
    private readonly TextBlock _summaryText;
    private readonly ComboBox _sortColumn;
    private readonly Button _sortDirection;

    public NativeTranscriptTablePreviewControl(NativeTranscriptTable table, string? title = null) {
        if (table == null) throw new ArgumentNullException(nameof(table));
        _table = table;
        _title = string.IsNullOrWhiteSpace(title) ? "Table evidence" : title.Trim();
        _viewModel = new NativeTableWorkspaceViewModel(table);
        _grid = CreateGrid();
        _summaryText = new TextBlock {
            FontSize = 12,
            Foreground = NativeControlBrushes.TextMuted
        };
        _sortColumn = BuildSortColumnPicker(table.Headers);
        _sortDirection = BuildSortDirectionButton();
        Content = Build();
        RenderGrid();
    }

    private FrameworkElement Build() {
        var panel = new StackPanel {
            Spacing = 8
        };
        panel.Children.Add(BuildHeader());
        panel.Children.Add(BuildPreviewToolbar());
        panel.Children.Add(new ScrollViewer {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _grid
        });
        panel.Children.Add(_summaryText);
        return new Border {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(7),
            BorderBrush = NativeControlBrushes.BorderStrong,
            BorderThickness = new Thickness(1),
            Background = NativeControlBrushes.SurfaceMuted,
            Child = panel
        };
    }

    private FrameworkElement BuildHeader() {
        var grid = new Grid {
            ColumnSpacing = 8
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new StackPanel {
            Spacing = 2
        };
        title.Children.Add(new TextBlock {
            Text = _title,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = NativeControlBrushes.TextPrimary
        });
        title.Children.Add(new TextBlock {
            Text = _table.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " rows / native Markdown table",
            FontSize = 12,
            Foreground = NativeControlBrushes.TextSecondary
        });
        Grid.SetColumn(title, 0);
        grid.Children.Add(title);

        var openButton = new Button {
            Content = "Open",
            MinWidth = 72,
            MinHeight = 32
        };
        openButton.Click += async (_, _) => await ShowWorkspaceAsync(openButton, _table, _title).ConfigureAwait(true);
        Grid.SetColumn(openButton, 1);
        grid.Children.Add(openButton);
        return grid;
    }

    private FrameworkElement BuildPreviewToolbar() {
        var search = new TextBox {
            PlaceholderText = "Search visible evidence",
            MinWidth = 0,
            MinHeight = 32,
            FontSize = 12,
            Padding = new Thickness(9, 4, 9, 4),
            Background = NativeControlBrushes.Surface,
            BorderBrush = NativeControlBrushes.Border,
            Foreground = NativeControlBrushes.TextPrimary,
            PlaceholderForeground = NativeControlBrushes.TextMuted,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        search.TextChanged += (_, _) => {
            _viewModel.SetSearch(search.Text);
            RenderGrid();
        };

        var sortRow = new Grid {
            ColumnSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        sortRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sortRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

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

        _sortColumn.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(_sortColumn, 0);
        sortRow.Children.Add(_sortColumn);
        Grid.SetColumn(_sortDirection, 1);
        sortRow.Children.Add(_sortDirection);

        return new StackPanel {
            Spacing = 7,
            Children = {
                search,
                sortRow
            }
        };
    }

    private static ComboBox BuildSortColumnPicker(IReadOnlyList<string> headers) {
        var combo = new ComboBox {
            MinWidth = 0,
            MinHeight = 32,
            PlaceholderText = "Sort"
        };
        combo.Items.Add("No sort");
        foreach (var header in headers) {
            combo.Items.Add(header);
        }

        combo.SelectedIndex = 0;
        return combo;
    }

    private static Button BuildSortDirectionButton() =>
        new() {
            Content = "Asc",
            IsEnabled = false,
            MinWidth = 62,
            MinHeight = 32
        };

    private void UpdateSortDirectionButton() {
        _sortDirection.IsEnabled = _viewModel.SortColumnIndex.HasValue;
        _sortDirection.Content = _viewModel.SortDescending ? "Desc" : "Asc";
    }

    private static Grid CreateGrid() =>
        new() {
            BorderBrush = NativeControlBrushes.Rgb(218, 225, 234),
            BorderThickness = new Thickness(1),
            ColumnSpacing = 0,
            RowSpacing = 0,
            Background = NativeControlBrushes.Surface
        };

    private void RenderGrid() {
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        _grid.ColumnDefinitions.Clear();

        var columnCount = Math.Max(1, _viewModel.Headers.Count);
        for (var column = 0; column < columnCount; column++) {
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        AddTableRow(_grid, _viewModel.Headers, rowIndex: 0, isHeader: true, columnCount);
        var rows = _viewModel.GetVisibleWindow(MaxPreviewRows);
        for (var row = 0; row < rows.Count; row++) {
            AddTableRow(_grid, rows[row].Cells, row + 1, isHeader: false, columnCount);
        }

        var visible = _viewModel.VisibleRows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var total = _viewModel.TotalRowCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _summaryText.Text = "Previewing " + rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " of " + visible + " visible rows, "
            + total + " total";
    }

    private static void AddTableRow(Grid grid, IReadOnlyList<string> values, int rowIndex, bool isHeader, int columnCount) {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var column = 0; column < columnCount; column++) {
            var cell = new Border {
                Padding = new Thickness(8, 6, 8, 6),
                BorderBrush = NativeControlBrushes.Border,
                BorderThickness = new Thickness(0, 0, column + 1 == columnCount ? 0 : 1, 1),
                Background = isHeader ? NativeControlBrushes.Rgb(238, 243, 249) : NativeControlBrushes.Surface,
                Child = new TextBlock {
                    Text = column < values.Count ? values[column] : string.Empty,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                    Foreground = NativeControlBrushes.TextPrimary
                }
            };
            Grid.SetColumn(cell, column);
            Grid.SetRow(cell, rowIndex);
            grid.Children.Add(cell);
        }
    }

    private static async Task ShowWorkspaceAsync(FrameworkElement owner, NativeTranscriptTable table, string title) {
        var dialog = new ContentDialog {
            XamlRoot = owner.XamlRoot,
            Title = title,
            Content = new NativeTableWorkspaceControl(table),
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };

        _ = await dialog.ShowAsync();
    }
}
