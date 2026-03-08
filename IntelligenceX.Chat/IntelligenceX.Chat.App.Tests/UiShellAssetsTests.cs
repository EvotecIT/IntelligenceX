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
    private static void AssertContainsAll(string content, params string[] anchors) {
        foreach (var anchor in anchors) {
            Assert.Contains(anchor, content, StringComparison.Ordinal);
        }
    }

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
    /// Ensures top header status chip remains scoped to compact runtime/session states
    /// and rejects long queue/usage-limit operational messages.
    /// </summary>
    [Fact]
    public void Load_IncludesHeaderStatusChipGate_ForOperationalStatusMessages() {
        var scriptPath = Path.Combine(UiDirectory, "Shell.10.core.js");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("function shouldRenderHeaderStatusChip(value)", script, StringComparison.Ordinal);
        Assert.Contains("function parseStartupStatusContext(value)", script, StringComparison.Ordinal);
        Assert.Contains("function resolveHeaderStatusChipFromStructuredStartupContext()", script, StringComparison.Ordinal);
        Assert.Contains("function resolveHeaderStatusChipFallbackStatus()", script, StringComparison.Ordinal);
        Assert.Contains("return { text: \"Ready\", tone: \"warn\" };", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Ready (tool packs syncing in background)", script, StringComparison.Ordinal);
        Assert.Contains("hasExplicitUnauthenticatedProbeSnapshot: false", script, StringComparison.Ordinal);
        Assert.Contains("normalizeBool(state.hasExplicitUnauthenticatedProbeSnapshot)", script, StringComparison.Ordinal);
        Assert.Contains("var STARTUP_HEADER_STAGE_TOTAL = 4;", script, StringComparison.Ordinal);
        Assert.Contains("var STARTUP_HEADER_TIMEOUT_AFTER_MS = 45000;", script, StringComparison.Ordinal);
        Assert.Contains("function resolveHeaderStartupProgressStatus(rawStatus)", script, StringComparison.Ordinal);
        Assert.Contains("function shouldSuppressStartupMetadataContextFromDiagnostics(context)", script, StringComparison.Ordinal);
        Assert.Contains("function isStartupDiagnosticsPhaseResultKnown(value)", script, StringComparison.Ordinal);
        Assert.Contains("function isStartupSignInWaitStillRelevant(rawStatus, connected, requiresInteractiveSignIn, authenticated, loginInProgress, authGateActive)", script, StringComparison.Ordinal);
        Assert.Contains("function isStartupAuthVerificationStillRelevant(rawStatus, connected, requiresInteractiveSignIn, authenticated, loginInProgress, authGateActive)", script, StringComparison.Ordinal);
        Assert.Contains("metadataResult === \"failed\" || metadataResult === \"success\"", script, StringComparison.Ordinal);
        Assert.Contains("function isStaleStartupStatusContextCandidate(rawStatus, context, connected, toolsLoading, loginInProgress)", script, StringComparison.Ordinal);
        Assert.Contains("Checking sign-in", script, StringComparison.Ordinal);
        Assert.Contains("Loading tools", script, StringComparison.Ordinal);
        Assert.Contains("Sign in required", script, StringComparison.Ordinal);
        Assert.Contains("function buildStartupProgressSnapshot(rows, activeKey, rawStatus, elapsedMs, loginInProgress)", script, StringComparison.Ordinal);
        Assert.Contains("function applyStatusChipStartupProgress(statusEl, startupHeaderStatus)", script, StringComparison.Ordinal);
        Assert.Contains("lower.indexOf(\"usage limit\") >= 0", script, StringComparison.Ordinal);
        Assert.Contains("lower.indexOf(\"queue \") >= 0", script, StringComparison.Ordinal);
        Assert.Contains("if (!shouldRenderHeaderStatusChip(value)) {", script, StringComparison.Ordinal);
        Assert.Contains("var structuredStartupStatus = resolveHeaderStatusChipFromStructuredStartupContext();", script, StringComparison.Ordinal);
        Assert.Contains("var fallbackStatus = resolveHeaderStatusChipFallbackStatus();", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures header status diagnostics keep a bounded runtime lifecycle history for tooltip/debug visibility.
    /// </summary>
    [Fact]
    public void Load_IncludesRuntimeLifecycleStatusTimelineDiagnostics() {
        var coreScriptPath = Path.Combine(UiDirectory, "Shell.10.core.js");
        var coreScript = File.ReadAllText(coreScriptPath);
        var baseCssPath = Path.Combine(UiDirectory, "Shell.10.base.css");
        var baseCss = File.ReadAllText(baseCssPath);
        var toolsScriptPath = Path.Combine(UiDirectory, "Shell.15.core.tools.js");
        var toolsScript = File.ReadAllText(toolsScriptPath);
        var renderingScriptPath = Path.Combine(UiDirectory, "Shell.18.core.tools.rendering.js");
        var renderingScript = File.ReadAllText(renderingScriptPath);

        Assert.Contains("statusTimeline: []", coreScript, StringComparison.Ordinal);
        Assert.Contains("startupDiagnostics: null", coreScript, StringComparison.Ordinal);
        Assert.Contains("function appendStatusTimelineEntry(value)", coreScript, StringComparison.Ordinal);
        Assert.Contains("function buildStartupPhaseTimelineModel()", coreScript, StringComparison.Ordinal);
        Assert.Contains("function appendStatusChipBackgroundDetailLines(lines, normalizedRaw, startupHeaderStatus)", coreScript, StringComparison.Ordinal);
        Assert.Contains("statusEl.title = buildStatusChipTitle(displayValue, rawValue, startupHeaderStatus, runtimeSummary);", coreScript, StringComparison.Ordinal);
        Assert.Contains("lines.push(\"Runtime: \" + normalizedRuntimeSummary);", coreScript, StringComparison.Ordinal);
        Assert.DoesNotContain("displayValue = value + \" - \" + resolveStatusRuntimeSummary();", coreScript, StringComparison.Ordinal);
        Assert.Contains("Runtime lifecycle: \" + state.statusTimeline.join(\" > \")", coreScript, StringComparison.Ordinal);
        Assert.Contains("Background: tool metadata sync is degraded.", coreScript, StringComparison.Ordinal);
        Assert.Contains("Background: retrying tool metadata sync in background.", coreScript, StringComparison.Ordinal);
        Assert.Contains("Background: tool pack metadata is still syncing.", coreScript, StringComparison.Ordinal);
        Assert.Contains("Runtime lifecycle: \" + state.statusTimeline.join(\" > \")", toolsScript, StringComparison.Ordinal);
        Assert.Contains("var startupPhaseTimeline = byId(\"optStartupPhaseTimeline\");", toolsScript, StringComparison.Ordinal);
        Assert.Contains("var startupDiagnosticsState = byId(\"optStartupDiagnosticsState\");", toolsScript, StringComparison.Ordinal);
        Assert.Contains("Refresh Account", coreScript, StringComparison.Ordinal);
        Assert.Contains("justify-content: flex-start;", baseCss, StringComparison.Ordinal);
        Assert.Contains("text-align: left;", baseCss, StringComparison.Ordinal);
        Assert.Contains("appendStartupDiagKv(\"bootstrap cache\", cacheText);", toolsScript, StringComparison.Ordinal);
        Assert.Contains("appendStartupDiagKv(\"metadata recovery\", metadataRecoveryParts.join(\" | \"));", toolsScript, StringComparison.Ordinal);
        Assert.Contains("Metadata recovery rerun is queued.", toolsScript, StringComparison.Ordinal);
        Assert.Contains("Waiting for sign-in before loading tools...", toolsScript, StringComparison.Ordinal);
        Assert.Contains("Syncing tool packs in background...", toolsScript, StringComparison.Ordinal);
        Assert.Contains("if (Array.isArray(nextState.statusTimeline)) {", renderingScript, StringComparison.Ordinal);
        Assert.Contains("state.options.startupDiagnostics = nextOptions.startupDiagnostics || null;", renderingScript, StringComparison.Ordinal);
        Assert.Contains("state.hasExplicitUnauthenticatedProbeSnapshot = nextState.hasExplicitUnauthenticatedProbeSnapshot;", renderingScript, StringComparison.Ordinal);
        Assert.Contains(".status-chip.status-chip-progress {", baseCss, StringComparison.Ordinal);
        Assert.Contains("width: clamp(152px, 18vw, 188px);", baseCss, StringComparison.Ordinal);
        Assert.Contains("flex: 0 0 clamp(152px, 18vw, 188px);", baseCss, StringComparison.Ordinal);
        Assert.Contains(".status-chip-routing {", baseCss, StringComparison.Ordinal);
        Assert.Contains("width: auto;", baseCss, StringComparison.Ordinal);
        Assert.Contains("--ix-startup-progress", baseCss, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the debug tab exposes a dedicated startup diagnostics section for runtime bootstrap latency troubleshooting.
    /// </summary>
    [Fact]
    public void Load_IncludesStartupDiagnosticsDebugSection() {
        var html = UiShellAssets.Load();

        Assert.Contains("id=\"optStartupDiagnosticsState\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"optStartupDiagnosticsKv\"", html, StringComparison.Ordinal);
        Assert.Contains(">Startup Diagnostics<", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the data-view quick save button derives user-facing copy from current export preferences
    /// instead of falling back to a stale fixed label.
    /// </summary>
    [Fact]
    public void Load_IncludesDynamicDataViewQuickExportLabeling() {
        var html = UiShellAssets.Load();
        var dataviewScriptPath = Path.Combine(UiDirectory, "Shell.17.core.dataview.js");
        var dataviewScript = File.ReadAllText(dataviewScriptPath);
        var toolsScriptPath = Path.Combine(UiDirectory, "Shell.15.core.tools.js");
        var toolsScript = File.ReadAllText(toolsScriptPath);

        Assert.Contains(">Quick Save<", html, StringComparison.Ordinal);
        Assert.Contains("function updateDataViewQuickExportLabel()", dataviewScript, StringComparison.Ordinal);
        Assert.Contains("function getQuickExportButtonCopy(format, saveMode) {", dataviewScript, StringComparison.Ordinal);
        Assert.Contains("btnDataViewQuickExport.textContent = copy.text;", dataviewScript, StringComparison.Ordinal);
        Assert.Contains("btnDataViewQuickExport.title = copy.title;", dataviewScript, StringComparison.Ordinal);
        Assert.Contains("if (typeof updateDataViewQuickExportLabel === \"function\") {", toolsScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures routing-catalog normalization remains backward compatible for older payloads
    /// that do not include newer explicit-routing readiness counters.
    /// </summary>
    [Fact]
    public void Load_RoutingCatalogNormalization_UsesPresenceGatesForReadinessDerivation() {
        var scriptPath = Path.Combine(UiDirectory, "Shell.10.core.js");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("var hasIsExplicitRoutingReady = hasOwn.call(value, \"isExplicitRoutingReady\");", script, StringComparison.Ordinal);
        Assert.Contains("var hasInferredRoutingTools = hasOwn.call(value, \"inferredRoutingTools\");", script, StringComparison.Ordinal);
        Assert.Contains("isExplicitRoutingReady: hasIsExplicitRoutingReady ? value.isExplicitRoutingReady === true : true,", script, StringComparison.Ordinal);
        Assert.Contains("var canDeriveExplicitReadiness = hasMissingRoutingContractTools", script, StringComparison.Ordinal);
        Assert.Contains("if (canDeriveExplicitReadiness && explicitReadinessIssueCount > 0) {", script, StringComparison.Ordinal);
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
    /// Ensures OfficeIMO shared-visual helpers keep kind normalization and base64 guardrails aligned.
    /// </summary>
    [Fact]
    public void Load_IncludesOfficeImoSharedVisualGuardrails() {
        var scriptPath = Path.Combine(UiDirectory, "Shell.21.core.visuals.js");
        var script = File.ReadAllText(scriptPath);

        AssertContainsAll(
            script,
            "var predictedDecodedBytes = Math.max(0, Math.floor(text.length / 4) * 3 - padding);",
            "if (predictedDecodedBytes > maxDecodedBytes) {",
            "return normalizeVisualType(raw || fallbackKind || \"\");",
            "var canDecodeSharedConfig = !!sharedConfigB64 && (!hasContract || !configEncoding || configEncoding === \"base64-utf8\");",
            "return getOfficeImoVisualSource(element, entry.cachedSourceAttribute, entry.fallbackConfigAttribute);",
            "state && typeof state.maxSourceChars === \"number\" && source.length > state.maxSourceChars");
    }

    /// <summary>
    /// Ensures Mermaid transcript upgrades support both OfficeIMO-native blocks and plain fenced-code fallback shapes.
    /// </summary>
    [Fact]
    public void Load_IncludesMermaidFallbackSelectorsAndRuntimeDiagnostics() {
        var scriptPath = Path.Combine(UiDirectory, "Shell.21.core.visuals.js");
        var script = File.ReadAllText(scriptPath);

        AssertContainsAll(
            script,
            "window.ixGetVisualRuntimeDiagnostics = function() {",
            "function recordVisualRuntimeAssetState(kind, asset, state, url, detail) {",
            "function recordVisualRuntimeReady(kind, ready) {",
            ".bubble .markdown-body pre > code.language-mermaid",
            ".bubble .markdown-body pre.language-mermaid",
            "var blocks = collectMermaidBlocks(root);");
    }

    /// <summary>
    /// Ensures autonomy numeric inputs expose the full supported service range for long tool flows.
    /// </summary>
    [Fact]
    public void Load_UsesExtendedAutonomyInputBounds_ForToolRoundsAndCandidates() {
        var html = UiShellAssets.Load();

        Assert.Contains("id=\"optAutonomyMaxRounds\" class=\"options-input options-input-sm\" type=\"number\" min=\"1\" max=\"256\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"optAutonomyMaxCandidates\" class=\"options-input options-input-sm\" type=\"number\" min=\"0\" max=\"256\"", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures transcript debug defaults keep draft/thinking bubbles hidden unless explicitly enabled.
    /// </summary>
    [Fact]
    public void Load_DefaultsDraftBubbleVisibilityToOff() {
        var coreScriptPath = Path.Combine(UiDirectory, "Shell.10.core.js");
        var renderingScriptPath = Path.Combine(UiDirectory, "Shell.18.core.tools.rendering.js");
        var toolsScriptPath = Path.Combine(UiDirectory, "Shell.15.core.tools.js");
        var coreScript = File.ReadAllText(coreScriptPath);
        var renderingScript = File.ReadAllText(renderingScriptPath);
        var toolsScript = File.ReadAllText(toolsScriptPath);

        Assert.Contains("showDraftBubbles: false", coreScript, StringComparison.Ordinal);
        Assert.Contains("{ showTurnTrace: false, showDraftBubbles: false }", renderingScript, StringComparison.Ordinal);
        Assert.Contains("previousDebug.showDraftBubbles === \"boolean\" ? previousDebug.showDraftBubbles : false", renderingScript, StringComparison.Ordinal);
        Assert.Contains("showDraftBubblesToggle.checked = typeof debugOptions.showDraftBubbles === \"boolean\"", toolsScript, StringComparison.Ordinal);
        Assert.Contains("? debugOptions.showDraftBubbles", toolsScript, StringComparison.Ordinal);
        Assert.Contains(": false;", toolsScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures export visual-theme and DOCX visual sizing controls/messaging hooks are present in shell assets.
    /// </summary>
    [Fact]
    public void Load_IncludesExportVisualThemeModeBindingsAndControl() {
        var html = UiShellAssets.Load();

        AssertContainsAll(
            html,
            "id=\"optExportVisualThemeMode\"",
            "id=\"optExportDocxVisualMaxWidthPx\"",
            "set_export_visual_theme_mode",
            "set_export_docx_visual_max_width",
            "docxVisualMaxWidthPx",
            "visualThemeMode",
            "print_friendly",
            "preserve_ui_theme",
            "unexpected export visual theme mode",
            "normalizeExportDocxVisualMaxWidthPx");
    }

    /// <summary>
    /// Ensures DOCX visual width binding is null-guarded so options initialization cannot crash on partial asset drift.
    /// </summary>
    [Fact]
    public void Load_GuardsDocxVisualWidthBinding_WhenControlMissing() {
        var bindingsPath = Path.Combine(UiDirectory, "Shell.20.bindings.js");
        var script = File.ReadAllText(bindingsPath);

        Assert.Contains("var docxVisualMaxWidthInput = byId(\"optExportDocxVisualMaxWidthPx\");", script, StringComparison.Ordinal);
        Assert.Contains("if (docxVisualMaxWidthInput) {", script, StringComparison.Ordinal);
        Assert.Contains("docxVisualMaxWidthInput.addEventListener(\"change\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("byId(\"optExportDocxVisualMaxWidthPx\").addEventListener(\"change\"", script, StringComparison.Ordinal);
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
    /// Ensures options conversations expose inline model override selection (without prompt dialogs)
    /// and post explicit set_conversation_model events.
    /// </summary>
    [Fact]
    public void Load_IncludesConversationModelOverrideSelectorAndBinding() {
        var helpersPath = Path.Combine(UiDirectory, "Shell.12.core.helpers.js");
        var helpers = File.ReadAllText(helpersPath);
        var bindingsPath = Path.Combine(UiDirectory, "Shell.20.bindings.js");
        var bindings = File.ReadAllText(bindingsPath);

        Assert.Contains("options-conversation-model-select", helpers, StringComparison.Ordinal);
        Assert.Contains("Auto (runtime default)", helpers, StringComparison.Ordinal);
        Assert.Contains("set_conversation_model", bindings, StringComparison.Ordinal);
        Assert.Contains("optConversations.addEventListener(\"change\"", bindings, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures discovered runtime model dropdown stays provider-catalog focused and avoids
    /// prefixing list entries with recents/favorites labels.
    /// </summary>
    [Fact]
    public void Load_RuntimeDiscoveredModelDropdown_OmitsRecentAndFavoritePrefixedEntries() {
        var scriptPath = Path.Combine(UiDirectory, "Shell.15.core.tools.js");
        var script = File.ReadAllText(scriptPath);

        Assert.DoesNotContain("pushModelOption(recents[r], \"Recent:\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("pushModelOption(favorites[f], \"Favorite:\"", script, StringComparison.Ordinal);
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
            "normalized === \"network\" || normalized === \"visnetwork\"",
            "code.language-ix-network, code.language-visnetwork, code.language-network",
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
        var scriptPath = Path.Combine(UiDirectory, "Shell.18.core.tools.rendering.js");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("pre.classList && pre.classList.contains(\"mermaid\")", script, StringComparison.Ordinal);
        Assert.Contains("pre.querySelector(\"code.language-ix-chart, code.language-chart\")", script, StringComparison.Ordinal);
        Assert.Contains("pre.querySelector(\"code.language-ix-network, code.language-visnetwork, code.language-network\")", script, StringComparison.Ordinal);
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
    /// Ensures transcript auto-follow detaches when users scroll away from live output,
    /// so async visual rendering does not force-pin older readers back to the bottom.
    /// </summary>
    [Fact]
    public void TranscriptRendering_UsesUserAwareFollowStateForAutoScroll() {
        var scriptPath = Path.Combine(UiDirectory, "Shell.18.core.tools.rendering.js");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("var transcriptFollowState = {", script, StringComparison.Ordinal);
        Assert.Contains("transcript.addEventListener(\"scroll\", function()", script, StringComparison.Ordinal);
        Assert.Contains("if (!transcriptFollowState.enabled) {", script, StringComparison.Ordinal);
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
        Assert.Contains("Switching runtime updates the active provider profile without forcing a process restart.", script, StringComparison.Ordinal);
        Assert.Contains("bridgeSessionState: bridgeSessionState,", script, StringComparison.Ordinal);
        Assert.Contains("bridgeSessionDetail: bridgeSessionDetail,", script, StringComparison.Ordinal);
        Assert.Contains("\"Bridge session\"", script, StringComparison.Ordinal);
        Assert.Contains("useOpenAiRuntimeButton.classList.toggle(\"options-btn-active\", isNative);", script, StringComparison.Ordinal);
        Assert.Contains("useOpenAiRuntimeButton.classList.toggle(\"options-btn-ghost\", !isNative);", script, StringComparison.Ordinal);
        Assert.Contains("useOpenAiRuntimeButton.disabled = turnBusy;", script, StringComparison.Ordinal);
        Assert.Contains("connectLmStudioButton.classList.toggle(\"options-btn-active\", lmStudioConnected);", script, StringComparison.Ordinal);
        Assert.Contains("connectLmStudioButton.classList.toggle(\"options-btn-ghost\", !lmStudioConnected);", script, StringComparison.Ordinal);
        Assert.Contains("connectLmStudioButton.disabled = turnBusy;", script, StringComparison.Ordinal);
        Assert.Contains("useCopilotRuntimeButton.classList.toggle(\"options-btn-active\", isCopilotCli);", script, StringComparison.Ordinal);
        Assert.Contains("useCopilotRuntimeButton.classList.toggle(\"options-btn-ghost\", !isCopilotCli);", script, StringComparison.Ordinal);
        Assert.Contains("useCopilotRuntimeButton.disabled = turnBusy;", script, StringComparison.Ordinal);
        Assert.Contains("applyStage === \"queued\"", script, StringComparison.Ordinal);
        Assert.Contains(".options-runtime-capability", css, StringComparison.Ordinal);
        Assert.Contains(".options-runtime-capability-value-supported", css, StringComparison.Ordinal);
        Assert.Contains(".options-runtime-apply-progress", css, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures runtime UI supports focused provider/model/usage panel views so long settings pages stay navigable.
    /// </summary>
    [Fact]
    public void Load_IncludesRuntimePanelViewSelectorAndVisibilityGuards() {
        var html = UiShellAssets.Load();
        var scriptPath = Path.Combine(UiDirectory, "Shell.15.core.tools.js");
        var script = File.ReadAllText(scriptPath);
        var renderingScriptPath = Path.Combine(UiDirectory, "Shell.18.core.tools.rendering.js");
        var renderingScript = File.ReadAllText(renderingScriptPath);

        Assert.Contains("id=\"optRuntimePanelView\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"optRuntimePanelHint\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"optRuntimeSectionCatalogTitle\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"optRuntimeSectionUsageTitle\"", html, StringComparison.Ordinal);
        Assert.Contains("function runtimePanelViewStorageKey()", script, StringComparison.Ordinal);
        Assert.Contains("function setRuntimePanelView(value)", script, StringComparison.Ordinal);
        Assert.Contains("function mergeRuntimePanelVisibility(id, shouldShow)", script, StringComparison.Ordinal);
        Assert.Contains("mergeRuntimePanelVisibility(\"optRuntimeSectionCatalogTitle\", showModelPanel);", script, StringComparison.Ordinal);
        Assert.Contains("mergeRuntimePanelVisibility(\"optRuntimeSectionUsageTitle\", showUsagePanel);", script, StringComparison.Ordinal);
        Assert.Contains("mergeRuntimePanelVisibility(\"optAccountUsageList\", showUsagePanel);", script, StringComparison.Ordinal);
        Assert.Contains("ensureCustomSelect(\"optRuntimePanelView\");", renderingScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures sidebar and options conversation rows include runtime/model metadata when available.
    /// </summary>
    [Fact]
    public void Load_IncludesConversationRuntimeModelMetaRendering() {
        var scriptPath = Path.Combine(UiDirectory, "Shell.12.core.helpers.js");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("var runtimeLabel = item && item.runtimeLabel ? String(item.runtimeLabel).trim() : \"\";", script, StringComparison.Ordinal);
        Assert.Contains("var modelLabel = item && item.modelLabel ? String(item.modelLabel).trim() : \"\";", script, StringComparison.Ordinal);
        Assert.Contains("runtimeSummary += \" | \" + modelLabel;", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures conversation actions include per-chat model override controls using inline selects (without prompt dialogs).
    /// </summary>
    [Fact]
    public void Load_IncludesConversationModelOverrideControlsAndBindings() {
        var helpersPath = Path.Combine(UiDirectory, "Shell.12.core.helpers.js");
        var helpers = File.ReadAllText(helpersPath);
        var bindingsPath = Path.Combine(UiDirectory, "Shell.20.bindings.js");
        var bindings = File.ReadAllText(bindingsPath);

        Assert.Contains("options-conversation-model-select", helpers, StringComparison.Ordinal);
        Assert.Contains("buildConversationModelChoices(chat)", helpers, StringComparison.Ordinal);
        Assert.DoesNotContain("window.prompt(\"Conversation model override (blank = auto):\"", bindings, StringComparison.Ordinal);
        Assert.Contains("post(\"set_conversation_model\", { id: modelId, model: (modelSelect.value || \"\").trim() });", bindings, StringComparison.Ordinal);
        Assert.Contains("var modelSelect = e.target.closest(\".options-conversation-model-select\");", bindings, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures switching away from compatible runtimes clears stale model IDs to avoid native/copilot mismatch confusion.
    /// </summary>
    [Fact]
    public void Load_ClearsStaleModelInputWhenTransportSwitchesAwayFromCompatible() {
        var bindingsPath = Path.Combine(UiDirectory, "Shell.20.bindings.js");
        var script = File.ReadAllText(bindingsPath);

        Assert.Contains("var previousTransport = normalizeLocalTransportValue((((state.options || {}).localModel || {}).transport || \"native\"));", script, StringComparison.Ordinal);
        Assert.Contains("if (modelInput && previousTransport !== next && !isCompatible) {", script, StringComparison.Ordinal);
        Assert.Contains("modelInput.value = \"\";", script, StringComparison.Ordinal);
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
