using System;
using IntelligenceX.Chat.App.Native.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Compact sortable and filterable native preview for a projected Markdown table.
/// </summary>
internal sealed class NativeTranscriptTablePreviewControl : UserControl {
    private const int MaxPreviewRows = 8;
    private readonly NativeTranscriptTable _table;
    private readonly string _title;
    private readonly NativeTableWorkspaceViewModel _viewModel;
    private readonly NativeTableGridControl _tableGrid;
    private readonly TextBlock _summaryText;

    public NativeTranscriptTablePreviewControl(NativeTranscriptTable table, string? title = null) {
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _title = string.IsNullOrWhiteSpace(title) ? "Table evidence" : title.Trim();
        _viewModel = new NativeTableWorkspaceViewModel(table);
        _summaryText = new TextBlock {
            FontSize = 12,
            Foreground = NativeControlBrushes.TextMuted
        };
        _tableGrid = new NativeTableGridControl(
            table,
            _viewModel,
            MaxPreviewRows,
            showSelection: false,
            showFilters: true,
            maximumBodyHeight: null);
        _tableGrid.ViewChanged += (_, _) => UpdateSummary();
        Content = Build();
        UpdateSummary();
    }

    private FrameworkElement Build() {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(BuildHeader());
        panel.Children.Add(BuildToolbar());
        panel.Children.Add(_tableGrid);
        panel.Children.Add(_summaryText);
        return new Border {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            BorderBrush = NativeControlBrushes.BorderStrong,
            BorderThickness = new Thickness(1),
            Background = NativeControlBrushes.Surface,
            Child = panel
        };
    }

    private FrameworkElement BuildHeader() {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new StackPanel { Spacing = 2 };
        title.Children.Add(new TextBlock {
            Text = _title,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = NativeControlBrushes.TextPrimary
        });
        title.Children.Add(new TextBlock {
            Text = _table.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " rows / interactive Markdown table",
            FontSize = 12,
            Foreground = NativeControlBrushes.TextSecondary
        });
        grid.Children.Add(title);

        var openButton = new Button {
            Content = "Open table",
            MinWidth = 96,
            MinHeight = 32,
            Background = NativeControlBrushes.AccentSoft,
            BorderBrush = NativeControlBrushes.UserBorder,
            Foreground = NativeControlBrushes.Accent
        };
        openButton.Click += (_, _) => NativeArtifactWindow.Show(
            _title,
            () => new NativeTableWorkspaceControl(_table),
            width: 1280,
            height: 780);
        Grid.SetColumn(openButton, 1);
        grid.Children.Add(openButton);
        return grid;
    }

    private FrameworkElement BuildToolbar() {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var search = new TextBox {
            PlaceholderText = "Search all rows",
            MinHeight = 32,
            FontSize = 12,
            Padding = new Thickness(9, 4, 9, 4),
            Background = NativeControlBrushes.Surface,
            BorderBrush = NativeControlBrushes.Border,
            Foreground = NativeControlBrushes.TextPrimary,
            PlaceholderForeground = NativeControlBrushes.TextMuted
        };
        search.TextChanged += (_, _) => {
            _viewModel.SetSearch(search.Text);
            _tableGrid.RefreshRows();
        };
        grid.Children.Add(search);

        var clear = new Button { Content = "Clear filters", MinHeight = 32 };
        clear.Click += (_, _) => _tableGrid.ClearFilters();
        Grid.SetColumn(clear, 1);
        grid.Children.Add(clear);

        var fit = new Button { Content = "Auto-fit", MinHeight = 32 };
        fit.Click += (_, _) => _tableGrid.AutoFitColumns();
        Grid.SetColumn(fit, 2);
        grid.Children.Add(fit);
        return grid;
    }

    private void UpdateSummary() {
        var rows = _tableGrid.RenderedRows;
        var visible = _viewModel.VisibleRows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var total = _viewModel.TotalRowCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _summaryText.Text = "Previewing " + rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " of " + visible + " visible rows, " + total + " total · click a column heading to sort";
    }
}
