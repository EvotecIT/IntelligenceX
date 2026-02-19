using System;
using System.IO;
using System.Linq;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Regression tests for shell asset composition.
/// </summary>
public sealed class UiShellAssetsTests {
    private static string UiDirectory => Path.Combine(AppContext.BaseDirectory, "Ui");

    /// <summary>
    /// Ensures the tool-source helpers are present in generated shell script.
    /// Missing helpers break tools rendering at runtime.
    /// </summary>
    [Fact]
    public void Load_IncludesPackSourceHelpers_ForToolsRendering() {
        var html = UiShellAssets.Load();

        Assert.Contains("function packSourceKind(", html, StringComparison.Ordinal);
        Assert.Contains("function packSourceLabel(", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures split JavaScript files are explicitly tracked by manifest,
    /// so adding/renaming files cannot silently change runtime composition.
    /// </summary>
    [Fact]
    public void JavaScriptManifest_MatchesSplitFilesInOutput() {
        var manifest = UiShellAssets.JavaScriptManifest.ToArray();
        var actual = Directory.EnumerateFiles(UiDirectory, "Shell.*.js", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(manifest.Length, actual.Length);
        foreach (var file in manifest) {
            Assert.Contains(file, actual, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Ensures split CSS files are explicitly tracked by manifest.
    /// </summary>
    [Fact]
    public void CssManifest_MatchesSplitFilesInOutput() {
        var manifest = UiShellAssets.StyleManifest.ToArray();
        var actual = Directory.EnumerateFiles(UiDirectory, "Shell.*.css", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(manifest.Length, actual.Length);
        foreach (var file in manifest) {
            Assert.Contains(file, actual, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Ensures composed HTML is not the fallback diagnostics page in normal test output.
    /// </summary>
    [Fact]
    public void Load_DoesNotReturnFallbackDiagnosticsPage() {
        var html = UiShellAssets.Load();
        Assert.DoesNotContain("UI shell assets are invalid", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures shell JavaScript chunks are emitted in manifest order.
    /// </summary>
    [Fact]
    public void Load_EmitsJavaScriptChunksInManifestOrder() {
        var html = UiShellAssets.Load();
        var previousIndex = -1;

        foreach (var file in UiShellAssets.JavaScriptManifest) {
            var marker = $"/* IXCHAT_PART:{file} */";
            var index = html.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(index >= 0, $"Missing marker for {file}");
            Assert.True(index > previousIndex, $"Marker order invalid for {file}");
            previousIndex = index;
        }
    }

    /// <summary>
    /// Ensures autonomy review-loop controls propagate through the UI set_autonomy payload.
    /// </summary>
    [Fact]
    public void Load_IncludesAutonomyReviewLoopFieldsInSetAutonomyPayload() {
        var html = UiShellAssets.Load();

        Assert.Contains("post(\"set_autonomy\", {", html, StringComparison.Ordinal);
        Assert.Contains("planExecuteReviewLoop: (byId(\"optAutonomyPlanReview\").value || \"default\").trim()", html, StringComparison.Ordinal);
        Assert.Contains("maxReviewPasses: (byId(\"optAutonomyMaxReviewPasses\").value || \"\").trim()", html, StringComparison.Ordinal);
        Assert.Contains("modelHeartbeatSeconds: (byId(\"optAutonomyModelHeartbeat\").value || \"\").trim()", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures export visual-theme controls and messaging hooks are present in shell assets.
    /// </summary>
    [Fact]
    public void Load_IncludesExportVisualThemeModeBindingsAndControl() {
        var html = UiShellAssets.Load();

        Assert.Contains("id=\"optExportVisualThemeMode\"", html, StringComparison.Ordinal);
        Assert.Contains("post(\"set_export_visual_theme_mode\", { value: e.target.value || \"preserve_ui_theme\" });", html, StringComparison.Ordinal);
        Assert.Contains("visualThemeMode: \"preserve_ui_theme\"", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures transport switching keeps draft compatible-http credentials in the form,
    /// instead of clearing hidden values when changing to non-compatible transports.
    /// </summary>
    [Fact]
    public void Load_DoesNotClearCompatibleDraftFields_OnTransportToggle() {
        var bindingsPath = Path.Combine(UiDirectory, "Shell.20.bindings.js");
        var script = File.ReadAllText(bindingsPath);

        Assert.DoesNotContain("if (!isCompatible) {\n        baseInput.value = \"\";", script, StringComparison.Ordinal);
        Assert.DoesNotContain("if (!isCompatible) {\n        apiKeyInput.value = \"\";", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures runtime apply guards duplicate submits and flips runtime UI state immediately.
    /// </summary>
    [Fact]
    public void Load_IncludesRuntimeApplyInFlightGuardAndOptimisticUiUpdate() {
        var bindingsPath = Path.Combine(UiDirectory, "Shell.20.bindings.js");
        var script = File.ReadAllText(bindingsPath);

        Assert.Contains("if (local.isApplying === true) {", script, StringComparison.Ordinal);
        Assert.Contains("state.options.localModel.isApplying = true;", script, StringComparison.Ordinal);
        Assert.Contains("renderLocalModelOptions();", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures Data View plain-table fallback preserves falsy scalar values like 0/false.
    /// </summary>
    [Fact]
    public void DataViewScript_UsesNullishSafeCellRenderingInPlainTableFallback() {
        var scriptPath = Path.Combine(UiDirectory, "Shell.17.core.dataview.js");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("td.textContent = cell == null ? \"\" : String(cell);", script, StringComparison.Ordinal);
        Assert.DoesNotContain("td.textContent = bodyRows[r][c] || \"\";", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures Data View ignores stale/unknown export callbacks so old failures cannot overwrite current export feedback.
    /// </summary>
    [Fact]
    public void DataViewActions_IgnoresStaleExportCallbacks() {
        var scriptPath = Path.Combine(UiDirectory, "Shell.19.core.dataview.actions.js");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("if (!exportId || !Object.prototype.hasOwnProperty.call(pendingExports, exportId)) {", script, StringComparison.Ordinal);
        Assert.Contains("if (pending && pending.sessionId && pending.sessionId !== activeDataViewSessionId) {", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures visual runtime script is part of the shell composition and transcript hook.
    /// </summary>
    [Fact]
    public void Load_IncludesVisualRuntimeAndTranscriptHooks() {
        var html = UiShellAssets.Load();

        Assert.Contains("/* IXCHAT_PART:Shell.21.core.visuals.js */", html, StringComparison.Ordinal);
        Assert.Contains("window.ixDisposeTranscriptVisuals = function(root) {", html, StringComparison.Ordinal);
        Assert.Contains("window.ixRenderTranscriptVisuals = function(root) {", html, StringComparison.Ordinal);
        Assert.Contains("window.ixMaterializeVisualFencesForDocx = async function(request) {", html, StringComparison.Ordinal);
        Assert.Contains("function renderIxChartBlock(", html, StringComparison.Ordinal);
        Assert.Contains("function renderIxNetworkBlock(", html, StringComparison.Ordinal);
        Assert.Contains("window.ixRenderTranscriptVisuals(transcript);", html, StringComparison.Ordinal);
        Assert.Contains("window.ixDisposeTranscriptVisuals(transcript);", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures code-copy adorners skip Mermaid and chart blocks so rendered visuals do not expose large payload copies.
    /// </summary>
    [Fact]
    public void TranscriptRendering_SkipsCodeCopyButtonsForVisualBlocks() {
        var scriptPath = Path.Combine(UiDirectory, "Shell.18.core.tools.rendering.js");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("pre.classList && pre.classList.contains(\"mermaid\")", script, StringComparison.Ordinal);
        Assert.Contains("pre.querySelector(\"code.language-ix-chart, code.language-chart\")", script, StringComparison.Ordinal);
        Assert.Contains("pre.querySelector(\"code.language-ix-network\")", script, StringComparison.Ordinal);
    }
}
