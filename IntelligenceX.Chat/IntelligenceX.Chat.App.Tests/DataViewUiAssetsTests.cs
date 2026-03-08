using System;
using System.IO;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Regression tests for Data View UI assets.
/// </summary>
public sealed class DataViewUiAssetsTests {
    private static string UiDirectory => Path.Combine(AppContext.BaseDirectory, "Ui");

    /// <summary>
    /// Ensures Data View distinguishes quick-save from explicit save-as actions in the shell HTML.
    /// </summary>
    [Fact]
    public void Load_IncludesDataViewSaveAsAndColumnModeControls() {
        var html = UiShellAssets.Load();

        Assert.Contains("id=\"btnDataViewQuickExport\"", html, StringComparison.Ordinal);
        Assert.Contains(">Quick Save<", html, StringComparison.Ordinal);
        Assert.Contains(">Save As CSV<", html, StringComparison.Ordinal);
        Assert.Contains(">Save As Excel<", html, StringComparison.Ordinal);
        Assert.Contains(">Save As Word<", html, StringComparison.Ordinal);
        Assert.Contains("id=\"btnDataViewToggleColumnMode\"", html, StringComparison.Ordinal);
        Assert.Contains(">Expand Columns<", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures Data View scripts expose quick-save copy helpers and column-mode toggling for long tables.
    /// </summary>
    [Fact]
    public void Scripts_IncludeQuickSaveCopyAndColumnModeToggles() {
        var dataviewScriptPath = Path.Combine(UiDirectory, "Shell.17.core.dataview.js");
        var dataviewScript = File.ReadAllText(dataviewScriptPath);
        var dataviewActionsPath = Path.Combine(UiDirectory, "Shell.19.core.dataview.actions.js");
        var dataviewActionsScript = File.ReadAllText(dataviewActionsPath);
        var dataviewCssPath = Path.Combine(UiDirectory, "Shell.27.dataview.css");
        var dataviewCss = File.ReadAllText(dataviewCssPath);

        Assert.Contains("function getQuickExportButtonCopy(format, saveMode) {", dataviewScript, StringComparison.Ordinal);
        Assert.Contains("function updateDataViewQuickExportLabel() {", dataviewScript, StringComparison.Ordinal);
        Assert.Contains("function applyDataViewColumnMode(mode) {", dataviewScript, StringComparison.Ordinal);
        Assert.Contains("dataViewState.columnMode === \"expanded\" ? \"wrapped\" : \"expanded\"", dataviewActionsScript, StringComparison.Ordinal);
        Assert.Contains("data-view-columns-expanded", dataviewCss, StringComparison.Ordinal);
        Assert.Contains("Horizontal scroll enabled", dataviewCss, StringComparison.Ordinal);
    }
}
