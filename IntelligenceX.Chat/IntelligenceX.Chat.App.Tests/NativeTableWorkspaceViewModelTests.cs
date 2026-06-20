using System;
using System.Collections.Generic;
using IntelligenceX.Chat.App.Native;
using IntelligenceX.Chat.App.Native.Rendering;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests native table workspace behavior without constructing WinUI controls.
/// </summary>
public sealed class NativeTableWorkspaceViewModelTests {
    /// <summary>
    /// Ensures search filters visible rows across all cells.
    /// </summary>
    [Fact]
    public void SetSearch_FiltersRowsAcrossCells() {
        var model = new NativeTableWorkspaceViewModel(CreateTable());

        model.SetSearch("offline");

        var row = Assert.Single(model.VisibleRows);
        Assert.Equal("Server2", row.Cells[0]);
    }

    /// <summary>
    /// Ensures sorting by a column is stable and toggles direction on repeated selection.
    /// </summary>
    [Fact]
    public void SortByColumn_SortsAndTogglesDirection() {
        var model = new NativeTableWorkspaceViewModel(CreateTable());

        model.SortByColumn(0);

        Assert.Collection(
            model.VisibleRows,
            row => Assert.Equal("Server1", row.Cells[0]),
            row => Assert.Equal("Server2", row.Cells[0]),
            row => Assert.Equal("Server3", row.Cells[0]));

        model.SortByColumn(0);

        Assert.Collection(
            model.VisibleRows,
            row => Assert.Equal("Server3", row.Cells[0]),
            row => Assert.Equal("Server2", row.Cells[0]),
            row => Assert.Equal("Server1", row.Cells[0]));
    }

    /// <summary>
    /// Ensures sort direction can be set explicitly for native controls that do not repeat selection events.
    /// </summary>
    [Fact]
    public void SetSortDirection_UpdatesExistingSortWithoutChangingColumn() {
        var model = new NativeTableWorkspaceViewModel(CreateTable());
        model.SortByColumn(0);

        model.SetSortDirection(true);

        Assert.True(model.SortDescending);
        Assert.Equal(0, model.SortColumnIndex);
        Assert.Collection(
            model.VisibleRows,
            row => Assert.Equal("Server3", row.Cells[0]),
            row => Assert.Equal("Server2", row.Cells[0]),
            row => Assert.Equal("Server1", row.Cells[0]));

        model.SetSortDirection(false);

        Assert.False(model.SortDescending);
        Assert.Equal(0, model.SortColumnIndex);
        Assert.Collection(
            model.VisibleRows,
            row => Assert.Equal("Server1", row.Cells[0]),
            row => Assert.Equal("Server2", row.Cells[0]),
            row => Assert.Equal("Server3", row.Cells[0]));
    }

    /// <summary>
    /// Ensures native controls can clear sort state and return to source row order.
    /// </summary>
    [Fact]
    public void ClearSort_RestoresSourceOrder() {
        var model = new NativeTableWorkspaceViewModel(CreateTable());
        model.SortByColumn(0);

        model.ClearSort();

        Assert.Null(model.SortColumnIndex);
        Assert.False(model.SortDescending);
        Assert.Collection(
            model.VisibleRows,
            row => Assert.Equal("Server3", row.Cells[0]),
            row => Assert.Equal("Server1", row.Cells[0]),
            row => Assert.Equal("Server2", row.Cells[0]));
    }

    /// <summary>
    /// Ensures visible TSV includes headers and sanitizes embedded tab/newline characters.
    /// </summary>
    [Fact]
    public void BuildVisibleTsv_ExportsFilteredRowsAndSanitizesCells() {
        var model = new NativeTableWorkspaceViewModel(new NativeTranscriptTable(
            new[] { "Name", "Status" },
            new IReadOnlyList<string>[] {
                new[] { "Server\t1", "Online" },
                new[] { "Server2", "Offline\nMaintenance" }
            }));
        model.SetSearch("maintenance");

        var tsv = model.BuildVisibleTsv();

        Assert.Equal("Name\tStatus" + Environment.NewLine + "Server2\tOffline Maintenance", tsv);
    }

    /// <summary>
    /// Ensures visible CSV includes headers and preserves RFC-style quoted cell content.
    /// </summary>
    [Fact]
    public void BuildVisibleCsv_ExportsFilteredRowsAndQuotesCells() {
        var model = new NativeTableWorkspaceViewModel(new NativeTranscriptTable(
            new[] { "Name", "Status" },
            new IReadOnlyList<string>[] {
                new[] { "Server,1", "Online" },
                new[] { "Server2", "Offline\nMaintenance" }
            }));
        model.SetSearch("maintenance");

        var csv = model.BuildVisibleCsv();

        Assert.Equal("Name,Status" + Environment.NewLine + "Server2,\"Offline\nMaintenance\"", csv);
    }

    /// <summary>
    /// Ensures all-row CSV export is not narrowed by the current search filter.
    /// </summary>
    [Fact]
    public void BuildAllCsv_ExportsAllRowsAndEscapesQuotes() {
        var model = new NativeTableWorkspaceViewModel(new NativeTranscriptTable(
            new[] { "Name", "Status" },
            new IReadOnlyList<string>[] {
                new[] { "Server,1", "Online" },
                new[] { "Server2", "Needs \"review\"" }
            }));
        model.SetSearch("online");

        var csv = model.BuildAllCsv();

        Assert.Equal(
            "Name,Status" + Environment.NewLine
            + "\"Server,1\",Online" + Environment.NewLine
            + "Server2,\"Needs \"\"review\"\"\"",
            csv);
    }

    /// <summary>
    /// Ensures the generic export contract routes format and scope without UI-specific branching.
    /// </summary>
    [Fact]
    public void BuildExportText_RoutesFormatAndScope() {
        var model = new NativeTableWorkspaceViewModel(new NativeTranscriptTable(
            new[] { "Name", "Status" },
            new IReadOnlyList<string>[] {
                new[] { "Server,1", "Online" },
                new[] { "Server2", "Offline" }
            }));
        model.SetSearch("offline");

        Assert.Equal(
            "Name\tStatus" + Environment.NewLine + "Server2\tOffline",
            model.BuildExportText(NativeTableExportFormat.Tsv, NativeTableExportScope.VisibleRows));
        Assert.Equal(
            "Name,Status" + Environment.NewLine + "\"Server,1\",Online" + Environment.NewLine + "Server2,Offline",
            model.BuildExportText(NativeTableExportFormat.Csv, NativeTableExportScope.AllRows));
    }

    /// <summary>
    /// Ensures selected-row export follows explicit row selection state.
    /// </summary>
    [Fact]
    public void BuildExportText_SelectedRows_ExportsSelectedRowsOnly() {
        var model = new NativeTableWorkspaceViewModel(CreateTable());
        model.SetRowSelected(model.VisibleRows[1], selected: true);

        var tsv = model.BuildExportText(NativeTableExportFormat.Tsv, NativeTableExportScope.SelectedRows);

        Assert.Equal(1, model.SelectedRowCount);
        Assert.True(model.IsRowSelected(model.VisibleRows[1]));
        Assert.Equal("Name\tStatus" + Environment.NewLine + "Server1\tOnline", tsv);

        model.SetRowSelected(model.VisibleRows[1], selected: false);

        Assert.Equal(0, model.SelectedRowCount);
        Assert.False(model.IsRowSelected(model.VisibleRows[1]));
    }

    /// <summary>
    /// Ensures selecting visible rows respects the current filtered row set.
    /// </summary>
    [Fact]
    public void SelectVisibleRows_SelectsFilteredRowsOnly() {
        var model = new NativeTableWorkspaceViewModel(CreateTable());
        model.SetSearch("online");

        model.SelectVisibleRows();

        Assert.Equal(2, model.SelectedRowCount);
        Assert.Equal(
            "Name,Status" + Environment.NewLine
            + "Server3,Online" + Environment.NewLine
            + "Server1,Online",
            model.BuildExportText(NativeTableExportFormat.Csv, NativeTableExportScope.SelectedRows));

        model.ClearSelection();

        Assert.Equal(0, model.SelectedRowCount);
    }

    /// <summary>
    /// Ensures large visible row sets can be rendered in stable windows.
    /// </summary>
    [Fact]
    public void GetVisibleWindow_MovesThroughVisibleRows() {
        var model = new NativeTableWorkspaceViewModel(CreateLargeTable(205));

        var firstWindow = model.GetVisibleWindow(100);

        Assert.Equal(100, firstWindow.Count);
        Assert.Equal("Server000", firstWindow[0].Cells[0]);
        Assert.False(model.CanMoveWindowBackward());
        Assert.True(model.CanMoveWindowForward(100));

        model.MoveWindowForward(100);
        var secondWindow = model.GetVisibleWindow(100);

        Assert.Equal(100, secondWindow.Count);
        Assert.Equal("Server100", secondWindow[0].Cells[0]);
        Assert.True(model.CanMoveWindowBackward());
        Assert.True(model.CanMoveWindowForward(100));

        model.MoveWindowForward(100);
        var finalWindow = model.GetVisibleWindow(100);

        Assert.Equal(5, finalWindow.Count);
        Assert.Equal("Server200", finalWindow[0].Cells[0]);
        Assert.False(model.CanMoveWindowForward(100));

        model.MoveWindowBackward(100);

        Assert.Equal(100, model.WindowStartIndex);
    }

    /// <summary>
    /// Ensures render windowing does not narrow visible-row export.
    /// </summary>
    [Fact]
    public void BuildExportText_VisibleRows_IgnoresRenderWindow() {
        var model = new NativeTableWorkspaceViewModel(CreateTable());
        model.MoveWindowForward(1);

        var tsv = model.BuildExportText(NativeTableExportFormat.Tsv, NativeTableExportScope.VisibleRows);

        Assert.Equal(
            "Name\tStatus" + Environment.NewLine
            + "Server3\tOnline" + Environment.NewLine
            + "Server1\tOnline" + Environment.NewLine
            + "Server2\tOffline",
            tsv);
    }

    /// <summary>
    /// Ensures search resets the render window to the beginning of the filtered set.
    /// </summary>
    [Fact]
    public void SetSearch_ResetsVisibleWindow() {
        var model = new NativeTableWorkspaceViewModel(CreateLargeTable(205));
        model.MoveWindowForward(100);

        model.SetSearch("Server204");

        var row = Assert.Single(model.GetVisibleWindow(100));
        Assert.Equal(0, model.WindowStartIndex);
        Assert.Equal("Server204", row.Cells[0]);
    }

    /// <summary>
    /// Ensures hidden columns are excluded from text exports and can be restored.
    /// </summary>
    [Fact]
    public void SetColumnVisible_ControlsExportedColumns() {
        var model = new NativeTableWorkspaceViewModel(CreateTable());

        model.SetColumnVisible(1, visible: false);

        Assert.False(model.IsColumnVisible(1));
        Assert.Equal(new[] { 0 }, model.VisibleColumnIndexes);
        Assert.Equal(
            "Name" + Environment.NewLine
            + "Server3" + Environment.NewLine
            + "Server1" + Environment.NewLine
            + "Server2",
            model.BuildExportText(NativeTableExportFormat.Tsv, NativeTableExportScope.AllRows));

        model.ResetColumnVisibility();

        Assert.True(model.IsColumnVisible(1));
        Assert.Equal(new[] { 0, 1 }, model.VisibleColumnIndexes);
    }

    /// <summary>
    /// Ensures the model keeps at least one column visible for rendering and export.
    /// </summary>
    [Fact]
    public void SetColumnVisible_DoesNotHideLastVisibleColumn() {
        var model = new NativeTableWorkspaceViewModel(CreateTable());

        model.SetColumnVisible(1, visible: false);
        model.SetColumnVisible(0, visible: false);

        Assert.True(model.IsColumnVisible(0));
        Assert.False(model.IsColumnVisible(1));
        Assert.Equal(new[] { 0 }, model.VisibleColumnIndexes);
    }

    /// <summary>
    /// Ensures per-column filters narrow visible rows and visible-row exports.
    /// </summary>
    [Fact]
    public void SetColumnFilter_FiltersRowsBySpecificColumn() {
        var model = new NativeTableWorkspaceViewModel(CreateTable());

        model.SetColumnFilter(1, "online");

        Assert.Equal(1, model.ActiveColumnFilterCount);
        Assert.Equal("online", model.GetColumnFilter(1));
        Assert.Collection(
            model.VisibleRows,
            row => Assert.Equal("Server3", row.Cells[0]),
            row => Assert.Equal("Server1", row.Cells[0]));
        Assert.Equal(
            "Name,Status" + Environment.NewLine
            + "Server3,Online" + Environment.NewLine
            + "Server1,Online",
            model.BuildExportText(NativeTableExportFormat.Csv, NativeTableExportScope.VisibleRows));
    }

    /// <summary>
    /// Ensures global search and per-column filters combine without broadening results.
    /// </summary>
    [Fact]
    public void SetColumnFilter_CombinesWithGlobalSearch() {
        var model = new NativeTableWorkspaceViewModel(CreateTable());

        model.SetSearch("server");
        model.SetColumnFilter(1, "offline");

        var row = Assert.Single(model.VisibleRows);
        Assert.Equal("Server2", row.Cells[0]);

        model.ClearColumnFilters();

        Assert.Equal(0, model.ActiveColumnFilterCount);
        Assert.Equal(3, model.VisibleRows.Count);
    }

    /// <summary>
    /// Ensures column filter changes reset the render window to the start of filtered rows.
    /// </summary>
    [Fact]
    public void SetColumnFilter_ResetsVisibleWindow() {
        var model = new NativeTableWorkspaceViewModel(CreateLargeTable(205));
        model.MoveWindowForward(100);

        model.SetColumnFilter(0, "Server204");

        var row = Assert.Single(model.GetVisibleWindow(100));
        Assert.Equal(0, model.WindowStartIndex);
        Assert.Equal("Server204", row.Cells[0]);
    }

    private static NativeTranscriptTable CreateTable() =>
        new(
            new[] { "Name", "Status" },
            new IReadOnlyList<string>[] {
                new[] { "Server3", "Online" },
                new[] { "Server1", "Online" },
                new[] { "Server2", "Offline" }
            });

    private static NativeTranscriptTable CreateLargeTable(int rowCount) {
        var rows = new List<IReadOnlyList<string>>();
        for (var index = 0; index < rowCount; index++) {
            rows.Add(new[] { "Server" + index.ToString("000", System.Globalization.CultureInfo.InvariantCulture), "Online" });
        }

        return new NativeTranscriptTable(new[] { "Name", "Status" }, rows);
    }
}
