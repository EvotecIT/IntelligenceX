using System;
using System.IO;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

public sealed partial class UiShellAssetsTests {
    /// <summary>
    /// Ensures visual runtime script is part of the shell composition and transcript hook.
    /// </summary>
    [Fact]
    public void Load_IncludesVisualRuntimeAndTranscriptHooks() {
        var html = UiShellAssets.Load();

        AssertContainsAll(
            html,
            "/* IXCHAT_PART:Shell.21.core.visuals.js */",
            "window.ixDisposeTranscriptVisuals",
            "window.ixRenderTranscriptVisuals",
            "window.ixMaterializeVisualFencesForDocx",
            "renderIxChartBlock",
            "renderIxNetworkBlock",
            "renderOfficeImoChartBlock",
            "renderOfficeImoNetworkBlock",
            "ixNativeVisualRegistry",
            "getNativeVisualRegistryEntry",
            "getOfficeImoVisualHash",
            "getOfficeImoVisualSource",
            "getOfficeImoVisualSourceByKind",
            "getOfficeImoVisualKind",
            "getOfficeImoVisualSelector",
            "collectOfficeImoVisualBlocks",
            "collectRegisteredOfficeImoVisualBlocks",
            "renderTranscriptVisualKind",
            "data-omd-visual-kind",
            "data-omd-visual-contract",
            "data-omd-config-encoding",
            "data-omd-visual-rendered",
            "data-omd-config-b64",
            "ixRenderTranscriptVisuals(transcript)",
            "ixDisposeTranscriptVisuals(transcript)");
    }

    /// <summary>
    /// Ensures Mermaid runtime resolution survives the vendor bundle shape and later visual phases
    /// still run when an earlier renderer throws.
    /// </summary>
    [Fact]
    public void Load_IncludesMermaidRuntimeFallbackAndSafeTranscriptPhaseChaining() {
        var scriptPath = Path.Combine(UiDirectory, "Shell.21.core.visuals.js");
        var script = File.ReadAllText(scriptPath);

        AssertContainsAll(
            script,
            "function resolveMermaidRuntimeCandidate() {",
            "function getMermaidRuntime() {",
            "globalThis.__esbuild_esm_mermaid_nm",
            "window.mermaid = runtime;",
            "function runTranscriptVisualPhaseSafely(root, renderPhase) {",
            "console.warn(\"transcript visual phase failed\", error);",
            "return runTranscriptVisualPhaseSafely(root, renderTranscriptCharts)",
            "return runTranscriptVisualPhaseSafely(root, renderTranscriptNetworks)",
            "return runTranscriptVisualPhaseSafely(root, renderTranscriptMermaid)");
    }

    /// <summary>
    /// Ensures visual popout panel and export callbacks are present for Mermaid/Chart/Network large-view workflows.
    /// </summary>
    [Fact]
    public void Load_IncludesVisualViewPanelAndExportCallbacks() {
        var html = UiShellAssets.Load();

        AssertContainsAll(
            html,
            "id=\"visualViewPanel\"",
            "id=\"visualViewBody\"",
            "id=\"btnVisualViewClose\"",
            "id=\"btnVisualViewPopout\"",
            "id=\"btnVisualViewToggleSize\"",
            "aria-label=\"Close visual view\"",
            "window.ixOpenVisualView",
            "window.ixCloseVisualView",
            "window.ixOnVisualExportPathSelected",
            "window.ixOnVisualExportResult",
            "window.ixOnVisualPopoutResult",
            "function initializeVisualViewLifecycleGuards()",
            "initializeVisualViewLifecycleGuards();",
            "function ensureVisualViewClosedState()",
            "visualViewBodyClassObserver.observe(document.body",
            "attributeFilter: [\"class\"]",
            "pick_visual_export_path",
            "export_visual_artifact",
            "visual_export_action",
            "open_visual_popout");
    }

    /// <summary>
    /// Ensures visual export and popout show actionable prep failures instead of generic pre-save errors.
    /// </summary>
    [Fact]
    public void Load_IncludesVisualExportPreparationDiagnosticsAndCanvasFallback() {
        var html = UiShellAssets.Load();

        AssertContainsAll(
            html,
            "function resolveVisualExportBuildFailureMessage(visualType, format)",
            "function tryCaptureVisualViewCanvasPayload(visualType)",
            "function resolveDocxRenderSize(visualType, docxVisualMaxWidthPx)",
            "var renderSize = resolveDocxRenderSize(normalizedFenceLanguage, docxVisualMaxWidthPx);",
            "convertSvgPayloadToPng(rendered, themeMode, renderSize)",
            "SVG export is only available for Mermaid diagrams.",
            "Visual export couldn't prepare the image payload before save.",
            "Visual popout couldn't prepare the image payload.");
    }

    /// <summary>
    /// Ensures visual renderers use theme-aware defaults while preserving payload-level customization paths.
    /// </summary>
    [Fact]
    public void Load_IncludesThemeAwareVisualDefaultsAndNetworkEdgeAliases() {
        var html = UiShellAssets.Load();

        AssertContainsAll(
            html,
            "function compareVisualBlockDocumentOrder(left, right) {",
            "function buildOrderedVisualEntries(fenceBlocks, nativeBlocks) {",
            "ensureMermaidThemeInitialized",
            "decodeBase64Utf8Value",
            "normalizeMermaidExportSvg",
            "htmlLabels: normalizedRenderProfile !== \"export\"",
            "svg.replace(/<br\\s*\\/?\\s*>/gi, \"<br/>\")",
            "themeVariables",
            "applyChartThemeDefaults",
            "host.style.width = String(exportWidth) + \"px\"",
            "(!parsedData || !parsedData.dataBase64) && canvas && typeof canvas.toDataURL === \"function\"",
            "window.requestAnimationFrame(function()",
            "normalized === \"network\"",
            "code.language-network",
            "canvas.omd-chart",
            ".omd-network",
            "data-chart-config-b64",
            "data-network-config-b64",
            "Object.prototype.hasOwnProperty.call(rawEdge, \"source\")",
            "Object.prototype.hasOwnProperty.call(rawEdge, \"target\")");
    }

    /// <summary>
    /// Ensures code-copy adorners skip Mermaid and chart blocks so rendered visuals do not expose large payload copies.
    /// </summary>
    [Fact]
    public void TranscriptRendering_SkipsCodeCopyButtonsForVisualBlocks() {
        var scriptPath = Path.Combine(UiDirectory, TranscriptRenderingScriptFile);
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("pre.classList && pre.classList.contains(\"mermaid\")", script, StringComparison.Ordinal);
        Assert.Contains("pre.querySelector(\"code.language-chart\")", script, StringComparison.Ordinal);
        Assert.Contains("pre.querySelector(\"code.language-network\")", script, StringComparison.Ordinal);
    }
}

