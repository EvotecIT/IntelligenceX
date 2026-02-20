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
        Assert.Contains("case \"print_friendly\":", html, StringComparison.Ordinal);
        Assert.Contains("case \"preserve_ui_theme\":", html, StringComparison.Ordinal);
        Assert.Contains("return \"print_friendly\";", html, StringComparison.Ordinal);
        Assert.Contains("unexpected export visual theme mode", html, StringComparison.Ordinal);
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
    /// Ensures runtime apply supports queued submits while preserving optimistic UI updates.
    /// </summary>
    [Fact]
    public void Load_IncludesRuntimeApplyQueueStateAndOptimisticUiUpdate() {
        var bindingsPath = Path.Combine(UiDirectory, "Shell.20.bindings.js");
        var script = File.ReadAllText(bindingsPath);

        Assert.Contains("function scheduleLocalProviderApply(forceRefresh, clearApiKey, clearBasicAuth)", script, StringComparison.Ordinal);
        Assert.Contains("pendingLocalProviderApply.clearApiKey = pendingLocalProviderApply.clearApiKey || nextClearApiKey;", script, StringComparison.Ordinal);
        Assert.Contains("pendingLocalProviderApply.clearBasicAuth = pendingLocalProviderApply.clearBasicAuth || nextClearBasicAuth;", script, StringComparison.Ordinal);
        Assert.Contains("var requestId = nextLocalProviderApplyRequestId();", script, StringComparison.Ordinal);
        Assert.Contains("runtimeApply.stage = wasApplying ? \"queued\" : \"applying\";", script, StringComparison.Ordinal);
        Assert.Contains("runtimeApply.detail = wasApplying", script, StringComparison.Ordinal);
        Assert.Contains("runtimeApply.requestId = requestId;", script, StringComparison.Ordinal);
        Assert.Contains("requestId: requestId,", script, StringComparison.Ordinal);
        Assert.Contains("state.options.localModel.isApplying = true;", script, StringComparison.Ordinal);
        Assert.Contains("renderLocalModelOptions();", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures runtime apply payload carries reasoning controls so model + reasoning configuration travels together.
    /// </summary>
    [Fact]
    public void Load_IncludesReasoningFieldsInApplyLocalProviderPayload() {
        var bindingsPath = Path.Combine(UiDirectory, "Shell.20.bindings.js");
        var script = File.ReadAllText(bindingsPath);

        Assert.Contains("reasoningEffort: reasoningEffort,", script, StringComparison.Ordinal);
        Assert.Contains("reasoningSummary: reasoningSummary,", script, StringComparison.Ordinal);
        Assert.Contains("textVerbosity: textVerbosity,", script, StringComparison.Ordinal);
        Assert.Contains("temperature: temperature,", script, StringComparison.Ordinal);
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

    /// <summary>
    /// Ensures runtime apply updates remain monotonic when options payloads arrive out of order.
    /// </summary>
    [Fact]
    public void Load_IncludesRuntimeApplyRequestOrderingGuardForOptionsSync() {
        var scriptPath = Path.Combine(UiDirectory, "Shell.18.core.tools.rendering.js");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("function resolveRuntimeApplyRequestId(localModel)", script, StringComparison.Ordinal);
        Assert.Contains("if (currentRequestId > 0 && incomingRequestId > 0 && incomingRequestId < currentRequestId)", script, StringComparison.Ordinal);
        Assert.Contains("incomingLocalModel.runtimeApply = currentLocalModel.runtimeApply;", script, StringComparison.Ordinal);
        Assert.Contains("window.ixRememberRuntimeApplyRequestId(resolveRuntimeApplyRequestId(incomingLocalModel));", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures runtime UI keeps explicit manual model guidance and does not regress to legacy restart commands.
    /// </summary>
    [Fact]
    public void Load_IncludesManualModelHint_AndOmitsLegacyRestartRuntimeCommand() {
        var html = UiShellAssets.Load();

        Assert.Contains("id=\"optLocalModelManualHint\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"optRuntimeApplyProgress\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"optRuntimeCapabilities\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("restart_runtime", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Restarting local runtime...", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures runtime options include capability matrix rendering for model, reasoning, auth, and usage transparency.
    /// </summary>
    [Fact]
    public void Load_IncludesRuntimeCapabilitiesRendererAndStyles() {
        var scriptPath = Path.Combine(UiDirectory, "Shell.15.core.tools.js");
        var script = File.ReadAllText(scriptPath);
        var cssPath = Path.Combine(UiDirectory, "Shell.30.options.css");
        var css = File.ReadAllText(cssPath);

        Assert.Contains("function renderRuntimeCapabilities(options)", script, StringComparison.Ordinal);
        Assert.Contains("function normalizeBridgeSessionState(value)", script, StringComparison.Ordinal);
        Assert.Contains("function resolveBridgeSessionStatus(state)", script, StringComparison.Ordinal);
        Assert.Contains("function resolveBridgeSessionValue(state)", script, StringComparison.Ordinal);
        Assert.Contains("appendRuntimeCapabilityRow(", script, StringComparison.Ordinal);
        Assert.Contains("renderRuntimeCapabilities({", script, StringComparison.Ordinal);
        Assert.Contains("var runtimeCapabilities = local.runtimeCapabilities", script, StringComparison.Ordinal);
        Assert.Contains("var runtimeApplyProgress = byId(\"optRuntimeApplyProgress\")", script, StringComparison.Ordinal);
        Assert.Contains("var runtimeApply = local.runtimeApply", script, StringComparison.Ordinal);
        Assert.Contains("runtimeCapabilities.supportsLiveApply", script, StringComparison.Ordinal);
        Assert.Contains("bridgeSessionState: bridgeSessionState,", script, StringComparison.Ordinal);
        Assert.Contains("bridgeSessionDetail: bridgeSessionDetail,", script, StringComparison.Ordinal);
        Assert.Contains("\"Bridge session\"", script, StringComparison.Ordinal);
        Assert.Contains("useOpenAiRuntimeButton.disabled = false;", script, StringComparison.Ordinal);
        Assert.Contains("connectLmStudioButton.disabled = false;", script, StringComparison.Ordinal);
        Assert.Contains("useCopilotRuntimeButton.disabled = false;", script, StringComparison.Ordinal);
        Assert.Contains("applyStage === \"queued\"", script, StringComparison.Ordinal);
        Assert.Contains(".options-runtime-capability", css, StringComparison.Ordinal);
        Assert.Contains(".options-runtime-capability-value-supported", css, StringComparison.Ordinal);
        Assert.Contains(".options-runtime-apply-progress", css, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures account usage retry countdowns refresh while options stay open.
    /// </summary>
    [Fact]
    public void Load_IncludesAccountUsageRetryCountdownRefreshTicker() {
        var scriptPath = Path.Combine(UiDirectory, "Shell.22.bindings.wheel.js");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("refreshAccountUsageRetryCountdowns();", script, StringComparison.Ordinal);
        Assert.Contains("setInterval(function()", script, StringComparison.Ordinal);
    }
}
