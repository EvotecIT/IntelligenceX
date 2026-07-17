using System;
using IntelligenceX.Chat.App.Native.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Expanded native table workspace for investigation, filtering, selection, and export.
/// </summary>
internal sealed class NativeTableWorkspaceControl : UserControl {
    private const int MaxRenderedRows = 100;
    private readonly NativeTranscriptTable _table;
    private readonly NativeTableWorkspaceViewModel _viewModel;
    private readonly NativeTableGridControl _tableGrid;
    private readonly TextBlock _summaryText;
    private readonly Button _previousWindow;
    private readonly Button _nextWindow;

    public NativeTableWorkspaceControl(NativeTranscriptTable table) {
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _viewModel = new NativeTableWorkspaceViewModel(table);
        _summaryText = new TextBlock {
            FontSize = 12,
            Foreground = NativeControlBrushes.TextSecondary
        };
        _previousWindow = BuildWindowButton("Previous 100", moveForward: false);
        _nextWindow = BuildWindowButton("Next 100", moveForward: true);
        _tableGrid = new NativeTableGridControl(
            table,
            _viewModel,
            MaxRenderedRows,
            showSelection: true,
            showFilters: true,
            maximumBodyHeight: 560);
        _tableGrid.ViewChanged += (_, _) => UpdateSummary();
        Content = Build();
        UpdateSummary();
    }

    private FrameworkElement Build() {
        var search = new TextBox {
            PlaceholderText = "Search every visible column",
            MinWidth = 300,
            MinHeight = 34,
            Background = NativeControlBrushes.Surface,
            BorderBrush = NativeControlBrushes.Border,
            Foreground = NativeControlBrushes.TextPrimary,
            PlaceholderForeground = NativeControlBrushes.TextMuted
        };
        search.TextChanged += (_, _) => {
            _viewModel.SetSearch(search.Text);
            _tableGrid.RefreshRows();
        };

        var toolbar = new StackPanel {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = {
                search,
                BuildActionButton("Auto-fit", () => _tableGrid.AutoFitColumns()),
                BuildActionButton("Clear filters", () => _tableGrid.ClearFilters()),
                BuildColumnsButton(),
                BuildCopyButton(),
                BuildActionButton("Select visible", () => {
                    _viewModel.SelectVisibleRows();
                    _tableGrid.RefreshRows();
                }),
                BuildActionButton("Clear selection", () => {
                    _viewModel.ClearSelection();
                    _tableGrid.RefreshRows();
                }),
                _previousWindow,
                _nextWindow
            }
        };
        var toolbarScroll = new ScrollViewer {
            Content = toolbar,
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        return new Grid {
            RowSpacing = 10,
            RowDefinitions = {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
            },
            Children = {
                toolbarScroll,
                Place(_summaryText, 1),
                Place(_tableGrid, 2)
            }
        };
    }

    private static T Place<T>(T element, int row) where T : FrameworkElement {
        Grid.SetRow(element, row);
        return element;
    }

    private static Button BuildActionButton(string label, Action action) {
        var button = new Button { Content = label, MinHeight = 34 };
        button.Click += (_, _) => action();
        return button;
    }

    private Button BuildWindowButton(string label, bool moveForward) {
        var button = new Button { Content = label, MinHeight = 34 };
        button.Click += (_, _) => {
            if (moveForward) _viewModel.MoveWindowForward(MaxRenderedRows);
            else _viewModel.MoveWindowBackward(MaxRenderedRows);
            _tableGrid.RefreshRows();
        };
        return button;
    }

    private Button BuildColumnsButton() {
        var flyout = new MenuFlyout();
        var reset = new MenuFlyoutItem { Text = "Show all columns" };
        reset.Click += (_, _) => {
            _viewModel.ResetColumnVisibility();
            _tableGrid.RebuildColumns();
        };
        flyout.Items.Add(reset);
        flyout.Items.Add(new MenuFlyoutSeparator());
        for (var index = 0; index < _viewModel.Headers.Count; index++) {
            var sourceColumn = index;
            var toggle = new ToggleMenuFlyoutItem {
                Text = _viewModel.Headers[index],
                IsChecked = _viewModel.IsColumnVisible(index)
            };
            toggle.Click += (_, _) => {
                _viewModel.SetColumnVisible(sourceColumn, toggle.IsChecked);
                toggle.IsChecked = _viewModel.IsColumnVisible(sourceColumn);
                _tableGrid.RebuildColumns();
            };
            flyout.Items.Add(toggle);
        }

        return new Button { Content = "Columns", MinHeight = 34, Flyout = flyout };
    }

    private Button BuildCopyButton() {
        var flyout = new MenuFlyout();
        AddCopyItem(flyout, "Visible rows as TSV", NativeTableExportFormat.Tsv, NativeTableExportScope.VisibleRows);
        AddCopyItem(flyout, "Selected rows as TSV", NativeTableExportFormat.Tsv, NativeTableExportScope.SelectedRows);
        AddCopyItem(flyout, "All rows as TSV", NativeTableExportFormat.Tsv, NativeTableExportScope.AllRows);
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddCopyItem(flyout, "Visible rows as CSV", NativeTableExportFormat.Csv, NativeTableExportScope.VisibleRows);
        AddCopyItem(flyout, "Selected rows as CSV", NativeTableExportFormat.Csv, NativeTableExportScope.SelectedRows);
        AddCopyItem(flyout, "All rows as CSV", NativeTableExportFormat.Csv, NativeTableExportScope.AllRows);
        return new Button { Content = "Copy", MinHeight = 34, Flyout = flyout };
    }

    private void AddCopyItem(
        MenuFlyout flyout,
        string label,
        NativeTableExportFormat format,
        NativeTableExportScope scope) {
        var item = new MenuFlyoutItem { Text = label };
        item.Click += (_, _) => CopyToClipboard(_viewModel.BuildExportText(format, scope));
        flyout.Items.Add(item);
    }

    private void UpdateSummary() {
        _previousWindow.IsEnabled = _viewModel.CanMoveWindowBackward();
        _nextWindow.IsEnabled = _viewModel.CanMoveWindowForward(MaxRenderedRows);
        var start = _viewModel.VisibleRows.Count == 0 ? 0 : _viewModel.WindowStartIndex + 1;
        var end = _viewModel.GetVisibleWindowEndExclusive(MaxRenderedRows);
        _summaryText.Text = "Showing "
            + start.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "–"
            + end.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " of "
            + _viewModel.VisibleRows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " visible · "
            + _viewModel.TotalRowCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " total · "
            + _viewModel.SelectedRowCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " selected · "
            + _viewModel.ActiveColumnFilterCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " active filters";
    }

    private static void CopyToClipboard(string text) {
        var package = new DataPackage();
        package.SetText(text ?? string.Empty);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }
}
