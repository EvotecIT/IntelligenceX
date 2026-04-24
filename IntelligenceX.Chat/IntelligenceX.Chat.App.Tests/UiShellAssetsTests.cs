using System;
using System.IO;
using System.Linq;
using IntelligenceX.Chat.App;
using IntelligenceX.OpenAI;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Regression tests for shell asset composition.
/// </summary>
public sealed partial class UiShellAssetsTests {
    private static string UiDirectory => Path.Combine(AppContext.BaseDirectory, "Ui");
    private const string RenderingScriptFile = "Shell.18.core.tools.rendering.js";
    private const string TranscriptRenderingScriptFile = "Shell.18a.transcript.rendering.js";
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
    /// Ensures the tools panel exposes autonomy contract hints from tool catalog metadata
    /// instead of flattening remote/setup/handoff/recovery capabilities away.
    /// </summary>
    [Fact]
    public void Load_IncludesToolAutonomyContractRenderingAndSearchTerms() {
        var scriptPath = Path.Combine(UiDirectory, "Shell.15.core.tools.js");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("function formatExecutionScopeLabel(executionScope)", script, StringComparison.Ordinal);
        Assert.Contains("if (tool.isEnvironmentDiscoverTool) {", script, StringComparison.Ordinal);
        Assert.Contains("if (tool.supportsRemoteHostTargeting || String(tool.executionScope || \"\").toLowerCase() === \"local_or_remote\") {", script, StringComparison.Ordinal);
        Assert.Contains("function appendToolDetailsLine(label, values) {", script, StringComparison.Ordinal);
        Assert.Contains("appendToolDetailsLine(\"Target arguments\", Array.isArray(tool.targetScopeArguments) ? tool.targetScopeArguments : []);", script, StringComparison.Ordinal);
        Assert.Contains("appendToolDetailsLine(\"Handoff packs\", Array.isArray(tool.handoffTargetPackIds) ? tool.handoffTargetPackIds : []);", script, StringComparison.Ordinal);
        Assert.Contains("appendToolDetailsLine(\"Recovery tools\", Array.isArray(tool.recoveryToolNames) ? tool.recoveryToolNames : []);", script, StringComparison.Ordinal);
        Assert.Contains("tool.isEnvironmentDiscoverTool ? \"environment discover preflight bootstrap\" : \"\",", script, StringComparison.Ordinal);
        Assert.Contains("tool.isHandoffAware ? \"handoff pivot continuation\" : \"\",", script, StringComparison.Ordinal);
        Assert.Contains("tool.supportsTransientRetry ? \"transient retry\" : \"\",", script, StringComparison.Ordinal);
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
    /// Ensures the static shell source receives its initial model default from the C# catalog during composition.
    /// </summary>
    [Fact]
    public void Load_InjectsCatalogBackedDefaultLocalModel() {
        var scriptPath = Path.Combine(UiDirectory, "Shell.10.core.js");
        var script = File.ReadAllText(scriptPath);
        var html = UiShellAssets.Load();

        Assert.Contains("var defaultLocalModel = \"{{IXCHAT_DEFAULT_LOCAL_MODEL}}\";", script, StringComparison.Ordinal);
        Assert.Contains("model: defaultLocalModel", script, StringComparison.Ordinal);
        Assert.DoesNotContain("model: \"gpt-5.5\"", script, StringComparison.Ordinal);
        Assert.Contains("var defaultLocalModel = \"" + OpenAIModelCatalog.DefaultModel + "\";", html, StringComparison.Ordinal);
        Assert.DoesNotContain("{{IXCHAT_DEFAULT_LOCAL_MODEL}}", html, StringComparison.Ordinal);
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
        var renderingScriptPath = Path.Combine(UiDirectory, RenderingScriptFile);
        var renderingScript = File.ReadAllText(renderingScriptPath);

        Assert.Contains("statusTimeline: []", coreScript, StringComparison.Ordinal);
        Assert.Contains("routingPromptExposureHistory: []", coreScript, StringComparison.Ordinal);
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
        Assert.Contains("Routing exposure: " , toolsScript, StringComparison.Ordinal);
        Assert.Contains("normalizeRoutingPromptExposure(state.routingPromptExposureHistory[r])", toolsScript, StringComparison.Ordinal);
        Assert.Contains("routingExposureText += \" [\" + (routingExposure.requestId || \"n/a\") + \" @ \" + (routingExposure.threadId || \"n/a\") + \"]\";", toolsScript, StringComparison.Ordinal);
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
        Assert.Contains("Showing startup preview", toolsScript, StringComparison.Ordinal);
        Assert.Contains("Final tool catalog is still loading, so pack and tool counts may increase automatically.", toolsScript, StringComparison.Ordinal);
        Assert.Contains("if (Array.isArray(nextState.statusTimeline)) {", renderingScript, StringComparison.Ordinal);
        Assert.Contains("if (Array.isArray(nextState.routingPromptExposureHistory)) {", renderingScript, StringComparison.Ordinal);
        Assert.Contains("state.routingPromptExposureHistory = nextState.routingPromptExposureHistory;", renderingScript, StringComparison.Ordinal);
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
    /// Ensures the sidebar delete affordance is suppressed after a confirmed delete
    /// so the next row does not inherit the same hover-visible X under the cursor.
    /// </summary>
    [Fact]
    public void Load_IncludesSidebarDeleteHoverSuppression_AfterConfirmedDelete() {
        var bindingsScriptPath = Path.Combine(UiDirectory, "Shell.20.bindings.js");
        var bindingsScript = File.ReadAllText(bindingsScriptPath);
        var baseCssPath = Path.Combine(UiDirectory, "Shell.10.base.css");
        var baseCss = File.ReadAllText(baseCssPath);

        Assert.Contains("function removeConversationFromClientState(conversationId)", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("function suppressSidebarDeleteHover()", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("function releaseSidebarDeleteHoverSuppression()", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("removeConversationFromClientState(delId);", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("suppressSidebarDeleteHover();", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("chatSidebar.addEventListener(\"pointermove\", function() {", bindingsScript, StringComparison.Ordinal);
        Assert.Contains(".chat-sidebar.suppress-delete-hover .chat-sidebar-item-delete", baseCss, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures new chats get a client-visible sidebar entry immediately and that send payloads
    /// stay pinned to the selected conversation id before the host round-trip completes.
    /// </summary>
    [Fact]
    public void Load_IncludesOptimisticConversationSidebarCreationAndSendRouting() {
        var bindingsScriptPath = Path.Combine(UiDirectory, "Shell.20.bindings.js");
        var bindingsScript = File.ReadAllText(bindingsScriptPath);
        var helpersScriptPath = Path.Combine(UiDirectory, "Shell.12.core.helpers.js");
        var helpersScript = File.ReadAllText(helpersScriptPath);
        var renderingScriptPath = Path.Combine(UiDirectory, RenderingScriptFile);
        var renderingScript = File.ReadAllText(renderingScriptPath);

        Assert.Contains("function nextClientConversationId()", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("function ensurePendingConversationInClientState(conversationId)", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("function updateConversationDraftInClientState(conversationId, text, countMessage)", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("state.options.conversations = sortConversationsForDisplay(remaining);", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("post(\"new_conversation\", { id: conversationId });", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("post(\"send\", { text: text, conversationId: queuedConversationId });", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("post(\"send\", { text: text, conversationId: conversationId });", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("function sortConversationsForDisplay(items)", helpersScript, StringComparison.Ordinal);
        Assert.Contains("var items = sortConversationsForDisplay(state.options.conversations || []);", helpersScript, StringComparison.Ordinal);
        Assert.Contains("state.options.conversations = sortConversationsForDisplay(nextOptions.conversations || []);", renderingScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the runtime panel exposes an explicit action to clear tracked account usage history.
    /// </summary>
    [Fact]
    public void Load_IncludesTrackedAccountUsageClearAction() {
        var htmlPath = Path.Combine(UiDirectory, "ShellTemplate.html");
        var html = File.ReadAllText(htmlPath);
        var bindingsPath = Path.Combine(UiDirectory, "Shell.20.bindings.js");
        var bindingsScript = File.ReadAllText(bindingsPath);

        Assert.Contains("btnClearTrackedAccountUsage", html, StringComparison.Ordinal);
        Assert.Contains("Clear Tracked Accounts", html, StringComparison.Ordinal);
        Assert.Contains("post(\"clear_account_usage\");", bindingsScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the Tools tab enters a syncing state when opened against an empty
    /// connected snapshot, instead of flashing "No tools registered" before refresh completes.
    /// </summary>
    [Fact]
    public void Load_IncludesToolsTabEmptySnapshotLoadingGate() {
        var coreScriptPath = Path.Combine(UiDirectory, "Shell.10.core.js");
        var coreScript = File.ReadAllText(coreScriptPath);
        var renderingScriptPath = Path.Combine(UiDirectory, RenderingScriptFile);
        var renderingScript = File.ReadAllText(renderingScriptPath);

        Assert.Contains("if (tabId === \"tools\") {", coreScript, StringComparison.Ordinal);
        Assert.Contains("state.options.toolLocalityFilter = \"all\";", coreScript, StringComparison.Ordinal);
        Assert.Contains("if (typeof renderTools === \"function\") {", coreScript, StringComparison.Ordinal);
        Assert.Contains("state.connected && !hasVisibleToolState", coreScript, StringComparison.Ordinal);
        Assert.Contains("state.options.toolsLoading = true;", coreScript, StringComparison.Ordinal);
        Assert.Contains("post(\"options_refresh\");", coreScript, StringComparison.Ordinal);
        Assert.Contains("function queueActiveToolsTabRender()", renderingScript, StringComparison.Ordinal);
        Assert.Contains("activeTab.dataset.tab !== \"tools\"", renderingScript, StringComparison.Ordinal);
        Assert.Contains("var incomingReportsToolLoading = nextOptions.toolsLoading === true || incomingPendingCatalogCount > 0;", renderingScript, StringComparison.Ordinal);
        Assert.Contains("var keepLoadingForConnectedEmptyState = !incomingHasVisibleToolState", renderingScript, StringComparison.Ordinal);
        Assert.Contains("&& state.connected", renderingScript, StringComparison.Ordinal);
        Assert.Contains("&& toolsTabOpen", renderingScript, StringComparison.Ordinal);
        Assert.Contains("&& incomingReportsToolLoading;", renderingScript, StringComparison.Ordinal);
        Assert.Contains("queueActiveToolsTabRender();", renderingScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures pack-level status/action chrome follows runtime pack activation state
    /// instead of misreading safe-default per-tool toggles as a partially loaded pack.
    /// </summary>
    [Fact]
    public void Load_IncludesPackActivationStateDrivenToolsRendering() {
        var helperScriptPath = Path.Combine(UiDirectory, "Shell.12.core.helpers.js");
        var helperScript = File.ReadAllText(helperScriptPath);
        var toolsScriptPath = Path.Combine(UiDirectory, "Shell.15.core.tools.js");
        var toolsScript = File.ReadAllText(toolsScriptPath);

        Assert.Contains("function normalizePackActivationState(value)", helperScript, StringComparison.Ordinal);
        Assert.Contains("function packActivationState(packId)", helperScript, StringComparison.Ordinal);
        Assert.Contains("function packCanActivateOnDemand(packId)", helperScript, StringComparison.Ordinal);
        Assert.Contains("var packEnabledByRuntime = !packMetadata || normalizeBool(packMetadata.enabled);", toolsScript, StringComparison.Ordinal);
        Assert.Contains("var packActivation = packActivationState(currentPackId);", toolsScript, StringComparison.Ordinal);
        Assert.Contains("var packDeferred = packActivation === \"deferred\";", toolsScript, StringComparison.Ordinal);
        Assert.Contains("} else if (!packHasTools && packDeferred && packCanLoadOnDemand) {", toolsScript, StringComparison.Ordinal);
        Assert.Contains("pill.textContent = \"On-demand\";", toolsScript, StringComparison.Ordinal);
        Assert.DoesNotContain("pill.textContent = allEnabled ? \"Loaded\" : (someEnabled ? \"Partial\" : \"Disabled\");", toolsScript, StringComparison.Ordinal);
        Assert.Contains("packToggle.className = \"options-toggle options-toggle-pack\";", toolsScript, StringComparison.Ordinal);
        Assert.Contains("packToggle.checked = packEnabledByRuntime;", toolsScript, StringComparison.Ordinal);
        Assert.Contains("setPackEnabled(packIdForToggle, groupToolsForToggle, e.target.checked);", toolsScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures shell theme application tracks the active preset name in the DOM
    /// so live profile theme changes remain inspectable and consistent across UI surfaces.
    /// </summary>
    [Fact]
    public void Load_IncludesActiveThemeDomTracking() {
        var renderingScriptPath = Path.Combine(UiDirectory, RenderingScriptFile);
        var renderingScript = File.ReadAllText(renderingScriptPath);

        Assert.Contains("function applyThemeName(themeName)", renderingScript, StringComparison.Ordinal);
        Assert.Contains("document.documentElement.setAttribute(\"data-ix-theme\", normalizedThemeName);", renderingScript, StringComparison.Ordinal);
        Assert.Contains("document.body.setAttribute(\"data-ix-theme\", normalizedThemeName);", renderingScript, StringComparison.Ordinal);
        Assert.Contains("window.ixResetTheme = function(themeName)", renderingScript, StringComparison.Ordinal);
        Assert.Contains("applyThemeName(themeName || \"default\");", renderingScript, StringComparison.Ordinal);
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
    /// Ensures the session tab exposes runtime scheduler operator controls and rendering hooks.
    /// </summary>
    [Fact]
    public void Load_IncludesBackgroundSchedulerOperatorSurface() {
        var html = UiShellAssets.Load();
        var coreScriptPath = Path.Combine(UiDirectory, "Shell.10.core.js");
        var coreScript = File.ReadAllText(coreScriptPath);
        var toolsScriptPath = Path.Combine(UiDirectory, "Shell.15.core.tools.js");
        var toolsScript = File.ReadAllText(toolsScriptPath);
        var bindingsScriptPath = Path.Combine(UiDirectory, "Shell.20.bindings.js");
        var bindingsScript = File.ReadAllText(bindingsScriptPath);
        var renderingScriptPath = Path.Combine(UiDirectory, RenderingScriptFile);
        var renderingScript = File.ReadAllText(renderingScriptPath);

        Assert.Contains("id=\"optRuntimeSchedulerState\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"optRuntimeSchedulerKv\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"optRuntimeSchedulerMaintenanceList\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"optRuntimeSchedulerBlockedPackList\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"optRuntimeSchedulerBlockedThreadList\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"optRuntimeSchedulerActivityList\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"optRuntimeSchedulerThreadList\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"optSchedulerScopePack\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"optSchedulerScopeThread\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"optSchedulerSuppressMinutes\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"btnSchedulerScopeTogglePackMute\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"btnSchedulerScopeTempPackMute\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"btnSchedulerClearPackBlocks\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"btnSchedulerScopeToggleMute\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"btnSchedulerScopeTempMute\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"btnSchedulerClearThreadBlocks\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"optSchedulerMaintenancePackId\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"optSchedulerMaintenanceThreadId\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"btnSchedulerRefresh\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"btnSchedulerPause\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"btnSchedulerResume\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"btnSchedulerAddMaintenance\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"btnSchedulerClearMaintenance\"", html, StringComparison.Ordinal);
        Assert.Contains("runtimeScheduler: null", coreScript, StringComparison.Ordinal);
        Assert.Contains("runtimeSchedulerScoped: null", coreScript, StringComparison.Ordinal);
        Assert.Contains("runtimeSchedulerGlobal: null", coreScript, StringComparison.Ordinal);
        Assert.Contains("if (Object.prototype.hasOwnProperty.call(nextOptions, \"runtimeScheduler\")) {", renderingScript, StringComparison.Ordinal);
        Assert.Contains("state.options.runtimeScheduler = nextOptions.runtimeScheduler;", renderingScript, StringComparison.Ordinal);
        Assert.Contains("if (Object.prototype.hasOwnProperty.call(nextOptions, \"runtimeSchedulerScoped\")) {", renderingScript, StringComparison.Ordinal);
        Assert.Contains("state.options.runtimeSchedulerScoped = nextOptions.runtimeSchedulerScoped;", renderingScript, StringComparison.Ordinal);
        Assert.Contains("if (Object.prototype.hasOwnProperty.call(nextOptions, \"runtimeSchedulerGlobal\")) {", renderingScript, StringComparison.Ordinal);
        Assert.Contains("state.options.runtimeSchedulerGlobal = nextOptions.runtimeSchedulerGlobal;", renderingScript, StringComparison.Ordinal);
        Assert.DoesNotContain("state.options.runtimeScheduler = nextOptions.runtimeScheduler || null;", renderingScript, StringComparison.Ordinal);
        Assert.DoesNotContain("state.options.runtimeSchedulerScoped = nextOptions.runtimeSchedulerScoped || null;", renderingScript, StringComparison.Ordinal);
        Assert.DoesNotContain("state.options.runtimeSchedulerGlobal = nextOptions.runtimeSchedulerGlobal || null;", renderingScript, StringComparison.Ordinal);
        Assert.Contains("function renderRuntimeScheduler()", toolsScript, StringComparison.Ordinal);
        Assert.Contains("renderRuntimeScheduler();", toolsScript, StringComparison.Ordinal);
        Assert.Contains("buildSchedulerThreadOptions()", toolsScript, StringComparison.Ordinal);
        Assert.Contains("buildSchedulerPackOptions()", toolsScript, StringComparison.Ordinal);
        Assert.Contains("normalizedPackIdArray(value)", toolsScript, StringComparison.Ordinal);
        Assert.Contains("normalizedSchedulerSuppressionArray(value, isPack)", toolsScript, StringComparison.Ordinal);
        Assert.Contains("appendSchedulerPackActionBar(host, packId, blocked)", toolsScript, StringComparison.Ordinal);
        Assert.Contains("tempButton.textContent = \"Mute 30m\";", toolsScript, StringComparison.Ordinal);
        Assert.Contains("tempLongButton.textContent = \"Mute 2h\";", toolsScript, StringComparison.Ordinal);
        Assert.Contains("untilMaintenanceButton.textContent = \"Until End\";", toolsScript, StringComparison.Ordinal);
        Assert.Contains("untilMaintenanceStartButton.textContent = \"Until Start\";", toolsScript, StringComparison.Ordinal);
        Assert.Contains("durationMinutes: \"30\"", toolsScript, StringComparison.Ordinal);
        Assert.Contains("durationMinutes: \"120\"", toolsScript, StringComparison.Ordinal);
        Assert.Contains("untilNextMaintenanceWindow: true", toolsScript, StringComparison.Ordinal);
        Assert.Contains("untilNextMaintenanceWindowStart: true", toolsScript, StringComparison.Ordinal);
        Assert.Contains("No muted scheduler packs", toolsScript, StringComparison.Ordinal);
        Assert.Contains("normalizedThreadIdArray(value)", toolsScript, StringComparison.Ordinal);
        Assert.Contains("appendSchedulerThreadActionBar(host, threadId, blocked)", toolsScript, StringComparison.Ordinal);
        Assert.Contains("No muted scheduler threads", toolsScript, StringComparison.Ordinal);
        Assert.Contains("scopeTogglePackMuteButton.dataset.packId = scopedPackId;", toolsScript, StringComparison.Ordinal);
        Assert.Contains("scopeTogglePackMuteButton.textContent = scopedPackBlocked ? \"Unmute Scoped Pack\" : \"Mute Scoped Pack\";", toolsScript, StringComparison.Ordinal);
        Assert.Contains("scopeTempPackMuteButton.disabled = !connected || !scopedPackId || scopedPackBlocked;", toolsScript, StringComparison.Ordinal);
        Assert.Contains("scopePackMuteUntilMaintenanceButton.disabled = !connected || !scopedPackId || scopedPackBlocked;", toolsScript, StringComparison.Ordinal);
        Assert.Contains("scopePackMuteUntilMaintenanceStartButton.disabled = !connected || !scopedPackId || scopedPackBlocked;", toolsScript, StringComparison.Ordinal);
        Assert.Contains("scopeToggleMuteButton.dataset.threadId = scopedThreadId;", toolsScript, StringComparison.Ordinal);
        Assert.Contains("scopeToggleMuteButton.textContent = scopedThreadBlocked ? \"Unmute Scoped Thread\" : \"Mute Scoped Thread\";", toolsScript, StringComparison.Ordinal);
        Assert.Contains("scopeTempMuteButton.disabled = !connected || !scopedThreadId || scopedThreadBlocked;", toolsScript, StringComparison.Ordinal);
        Assert.Contains("scopeThreadMuteUntilMaintenanceButton.disabled = !connected || !scopedThreadId || scopedThreadBlocked;", toolsScript, StringComparison.Ordinal);
        Assert.Contains("scopeThreadMuteUntilMaintenanceStartButton.disabled = !connected || !scopedThreadId || scopedThreadBlocked;", toolsScript, StringComparison.Ordinal);
        Assert.Contains("scheduler.scopeThreadId", toolsScript, StringComparison.Ordinal);
        Assert.Contains("scheduler.recentActivity", toolsScript, StringComparison.Ordinal);
        Assert.Contains("function resolveActivityPackId(activity)", toolsScript, StringComparison.Ordinal);
        Assert.Contains("resolveSchedulerPackLabel(packIdForActivity)", toolsScript, StringComparison.Ordinal);
        Assert.Contains("activityPackMutedPill.textContent = (formatSchedulerSuppressionMode(packSuppressionForActivity) || \"pack muted\") + (packSuppressionForActivity ? \" pack\" : \"\");", toolsScript, StringComparison.Ordinal);
        Assert.Contains("appendSchedulerPackActionBar(activityCard, packIdForActivity, isSchedulerPackBlocked(packIdForActivity));", toolsScript, StringComparison.Ordinal);
        Assert.Contains("appendSchedulerThreadActionBar(activityCard, threadIdForActivity, isSchedulerThreadBlocked(threadIdForActivity));", toolsScript, StringComparison.Ordinal);
        Assert.Contains("activityMutedPill.textContent = formatSchedulerSuppressionMode(threadSuppressionForActivity) || \"muted\";", toolsScript, StringComparison.Ordinal);
        Assert.Contains("scheduler.threadSummaries", toolsScript, StringComparison.Ordinal);
        Assert.Contains("post(\"scheduler_set_pack_block\", {", toolsScript, StringComparison.Ordinal);
        Assert.Contains("post(\"scheduler_set_thread_block\", {", toolsScript, StringComparison.Ordinal);
        Assert.Contains("post(\"scheduler_refresh\", {", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("post(\"scheduler_set_pack_block\", {", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("post(\"scheduler_set_pack_block\", {\n        packId: packId,\n        blocked: true,\n        durationMinutes: (byId(\"optSchedulerSuppressMinutes\").value || \"\").trim()", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("var schedulerScopePackMuteUntilMaintenanceButton = byId(\"btnSchedulerScopePackMuteUntilMaintenance\");", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("var schedulerScopePackMuteUntilMaintenanceStartButton = byId(\"btnSchedulerScopePackMuteUntilMaintenanceStart\");", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("post(\"scheduler_clear_pack_blocks\");", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("post(\"scheduler_set_thread_block\", {", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("post(\"scheduler_set_thread_block\", {\n        threadId: threadId,\n        blocked: true,\n        durationMinutes: (byId(\"optSchedulerSuppressMinutes\").value || \"\").trim()", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("var schedulerScopeThreadMuteUntilMaintenanceButton = byId(\"btnSchedulerScopeThreadMuteUntilMaintenance\");", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("var schedulerScopeThreadMuteUntilMaintenanceStartButton = byId(\"btnSchedulerScopeThreadMuteUntilMaintenanceStart\");", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("untilNextMaintenanceWindow: true", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("untilNextMaintenanceWindowStart: true", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("post(\"scheduler_clear_thread_blocks\");", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("post(\"scheduler_pause\"", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("post(\"scheduler_resume\")", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("post(\"scheduler_add_maintenance\"", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("post(\"scheduler_clear_maintenance\")", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("post(\"scheduler_remove_maintenance\"", toolsScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures sidebar conversation rendering can surface per-thread background scheduler hints
    /// without losing global visibility after a scoped scheduler refresh.
    /// </summary>
    [Fact]
    public void Load_IncludesSidebarBackgroundSchedulerHints() {
        var helpersScriptPath = Path.Combine(UiDirectory, "Shell.12.core.helpers.js");
        var helpersScript = File.ReadAllText(helpersScriptPath);
        var baseCssPath = Path.Combine(UiDirectory, "Shell.10.base.css");
        var baseCss = File.ReadAllText(baseCssPath);

        Assert.Contains("function getGlobalRuntimeScheduler()", helpersScript, StringComparison.Ordinal);
        Assert.Contains("function getConversationSchedulerHint(chat)", helpersScript, StringComparison.Ordinal);
        Assert.Contains("var schedulerSuppression = findConversationSchedulerSuppression(chat);", helpersScript, StringComparison.Ordinal);
        Assert.Contains("var schedulerBlocked = isConversationSchedulerBlocked(chat);", helpersScript, StringComparison.Ordinal);
        Assert.Contains("var schedulerHint = getConversationSchedulerHint(chat);", helpersScript, StringComparison.Ordinal);
        Assert.Contains("var runningThreadIds = Array.isArray(scheduler.runningThreadIds)", helpersScript, StringComparison.Ordinal);
        Assert.Contains("var readyThreadIds = Array.isArray(scheduler.readyThreadIds)", helpersScript, StringComparison.Ordinal);
        Assert.Contains("chat-sidebar-item-pill chat-sidebar-item-pill-scheduler", helpersScript, StringComparison.Ordinal);
        Assert.Contains("BG muted", helpersScript, StringComparison.Ordinal);
        Assert.Contains("BG temp", helpersScript, StringComparison.Ordinal);
        Assert.Contains("BG ready ", helpersScript, StringComparison.Ordinal);
        Assert.Contains(".chat-sidebar-item-pill-scheduler {", baseCss, StringComparison.Ordinal);
        Assert.Contains(".chat-sidebar-item-pill-scheduler.tone-ready {", baseCss, StringComparison.Ordinal);
        Assert.Contains(".chat-sidebar-item-pill-scheduler.tone-running {", baseCss, StringComparison.Ordinal);
        Assert.Contains(".chat-sidebar-item-pill-scheduler.tone-queued {", baseCss, StringComparison.Ordinal);
        Assert.Contains(".chat-sidebar-item-pill-scheduler.tone-muted {", baseCss, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the main chat surface no longer exposes background-scheduler operator controls,
    /// while still updating the active sidebar selection deterministically from incoming options state.
    /// </summary>
    [Fact]
    public void Load_RemovesMainSurfaceSchedulerControls_AndKeepsActiveConversationStateDeterministic() {
        var html = UiShellAssets.Load();
        var helpersScriptPath = Path.Combine(UiDirectory, "Shell.12.core.helpers.js");
        var helpersScript = File.ReadAllText(helpersScriptPath);
        var bindingsScriptPath = Path.Combine(UiDirectory, "Shell.20.bindings.js");
        var bindingsScript = File.ReadAllText(bindingsScriptPath);
        var renderingScriptPath = Path.Combine(UiDirectory, RenderingScriptFile);
        var renderingScript = File.ReadAllText(renderingScriptPath);
        var baseCssPath = Path.Combine(UiDirectory, "Shell.10.base.css");
        var baseCss = File.ReadAllText(baseCssPath);

        Assert.Contains("id=\"optBackgroundSchedulerSection\"", html, StringComparison.Ordinal);
        Assert.Contains("function isConversationSchedulerBlocked(chat)", helpersScript, StringComparison.Ordinal);
        Assert.Contains("function findConversationSchedulerSuppression(chat)", helpersScript, StringComparison.Ordinal);
        Assert.Contains("function formatSchedulerSuppressionExpiry(utcTicks)", helpersScript, StringComparison.Ordinal);
        Assert.Contains("threadId: threadId,\n          queuedItemCount: 0,\n          readyItemCount: 0,\n          runningItemCount: 1", helpersScript, StringComparison.Ordinal);
        Assert.Contains("threadId: threadId,\n          queuedItemCount: 0,\n          readyItemCount: 1,\n          runningItemCount: 0", helpersScript, StringComparison.Ordinal);
        Assert.Contains("if (Object.prototype.hasOwnProperty.call(nextOptions, \"activeConversationId\")) {", renderingScript, StringComparison.Ordinal);
        Assert.Contains("state.options.activeConversationId = nextOptions.activeConversationId || \"\";", renderingScript, StringComparison.Ordinal);
        Assert.DoesNotContain("state.options.activeConversationId = nextOptions.activeConversationId || state.options.activeConversationId;", renderingScript, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"activeThreadSchedulerBanner\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"btnActiveThreadSchedulerOpen\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("function findActiveConversation()", helpersScript, StringComparison.Ordinal);
        Assert.DoesNotContain("function renderActiveConversationSchedulerHint()", helpersScript, StringComparison.Ordinal);
        Assert.DoesNotContain("function buildActiveConversationSchedulerActivityText(chat)", helpersScript, StringComparison.Ordinal);
        Assert.DoesNotContain("post(\"scheduler_refresh\", { threadId: threadId });", bindingsScript, StringComparison.Ordinal);
        Assert.DoesNotContain("switchOptionsTab(\"session\");", bindingsScript, StringComparison.Ordinal);
        Assert.DoesNotContain(".chat-thread-banner {", baseCss, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures debug surfaces expose transcript forensics export so persisted/raw/normalized/rendered states
    /// can be captured without modifying the main transcript pipeline.
    /// </summary>
    [Fact]
    public void Load_IncludesTranscriptForensicsDebugActions() {
        var html = UiShellAssets.Load();
        var bindingsScriptPath = Path.Combine(UiDirectory, "Shell.20.bindings.js");
        var bindingsScript = File.ReadAllText(bindingsScriptPath);
        var coreScriptPath = Path.Combine(UiDirectory, "Shell.10.core.js");
        var coreScript = File.ReadAllText(coreScriptPath);

        Assert.Contains("id=\"menuExportTranscriptForensics\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"btnDebugExportTranscriptForensics\"", html, StringComparison.Ordinal);
        Assert.Contains("debug_export_transcript_forensics", bindingsScript, StringComparison.Ordinal);
        Assert.Contains("menuExportTranscriptForensics", coreScript, StringComparison.Ordinal);
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
        Assert.Contains("var autonomyReadinessHighlightsRaw = Array.isArray(value.autonomyReadinessHighlights) ? value.autonomyReadinessHighlights : [];", script, StringComparison.Ordinal);
        Assert.Contains("autonomyReadinessHighlights: autonomyReadinessHighlights", script, StringComparison.Ordinal);
        Assert.Contains("toolCatalogRoutingCatalog: null", script, StringComparison.Ordinal);
        Assert.Contains("toolCatalogPlugins: []", script, StringComparison.Ordinal);
        Assert.Contains("toolCatalogCapabilitySnapshot: null", script, StringComparison.Ordinal);
        Assert.Contains("latestRoutingPromptExposure: null", script, StringComparison.Ordinal);
        Assert.Contains("var pluginDetailsEl = byId(\"policyPluginDetails\");", script, StringComparison.Ordinal);
        Assert.Contains("var fallbackRoutingCatalog = normalizeRoutingCatalog(state.options ? state.options.toolCatalogRoutingCatalog : null);", script, StringComparison.Ordinal);
        Assert.Contains("var fallbackPlugins = normalizePlugins(state.options ? state.options.toolCatalogPlugins : null);", script, StringComparison.Ordinal);
        Assert.Contains("fallbackPlugins = resolveCapabilitySnapshotPlugins(fallbackCapabilitySnapshot);", script, StringComparison.Ordinal);
        Assert.Contains("var routingPromptExposure = normalizeRoutingPromptExposure(state.options ? state.options.latestRoutingPromptExposure : null);", script, StringComparison.Ordinal);
        Assert.Contains("renderPluginPolicyDetails(pluginDetailsEl, fallbackPlugins);", script, StringComparison.Ordinal);
        Assert.Contains("var fallbackCapabilitySnapshot = normalizeCapabilitySnapshot(state.options ? state.options.toolCatalogCapabilitySnapshot : null);", script, StringComparison.Ordinal);
        Assert.Contains("renderPolicyList(pluginsEl, formatPluginPolicyLines(fallbackPlugins), \"Plugin sources\");", script, StringComparison.Ordinal);
        Assert.Contains("renderRoutingCatalogPolicy(routingCatalogEl, fallbackRoutingCatalog, routingPromptExposure);", script, StringComparison.Ordinal);
        Assert.Contains("renderCapabilitySnapshotPolicy(capabilitySnapshotEl, fallbackCapabilitySnapshot);", script, StringComparison.Ordinal);
        Assert.Contains("function normalizeCapabilitySnapshot(value) {", script, StringComparison.Ordinal);
        Assert.Contains("function normalizeRoutingPromptExposure(value) {", script, StringComparison.Ordinal);
        Assert.Contains("function resolveCapabilitySnapshotPlugins(capabilitySnapshot) {", script, StringComparison.Ordinal);
        Assert.Contains("function normalizePlugins(value) {", script, StringComparison.Ordinal);
        Assert.Contains("function appendOptionsKv(host, label, value) {", script, StringComparison.Ordinal);
        Assert.Contains("function formatPluginPolicyLines(plugins) {", script, StringComparison.Ordinal);
        Assert.Contains("function renderPluginPolicyDetails(host, plugins) {", script, StringComparison.Ordinal);
        Assert.Contains("function renderRoutingCatalogPolicy(host, routingCatalog, routingPromptExposure) {", script, StringComparison.Ordinal);
        Assert.Contains("requestId: typeof value.requestId === \"string\" ? value.requestId.trim() : \"\",", script, StringComparison.Ordinal);
        Assert.Contains("lines.push(\"prompt source: \" + (routingPromptExposure.requestId || \"n/a\") + \" @ \" + (routingPromptExposure.threadId || \"n/a\"));", script, StringComparison.Ordinal);
        Assert.Contains("lines.push(\"latest prompt exposure: \" + routingPromptExposure.strategy + \" (\" + routingPromptExposure.selectedToolCount + \"/\" + routingPromptExposure.totalToolCount + \")\");", script, StringComparison.Ordinal);
        Assert.Contains("lines.push(\"prompt top tools: \" + routingPromptExposure.topToolNames.join(\", \"));", script, StringComparison.Ordinal);
        Assert.Contains("function renderCapabilitySnapshotPolicy(host, capabilitySnapshot) {", script, StringComparison.Ordinal);
        Assert.Contains("toolingSnapshot: !toolingSnapshotRaw ? null :", script, StringComparison.Ordinal);
        Assert.Contains("plugins = resolveCapabilitySnapshotPlugins(capabilitySnapshot);", script, StringComparison.Ordinal);
        Assert.Contains("lines.push(\"tooling snapshot: \" + toolingSource + \", packs \" + capabilitySnapshot.toolingSnapshot.packs.length + \", plugins \" + capabilitySnapshot.toolingSnapshot.plugins.length);", script, StringComparison.Ordinal);
        Assert.Contains("isExplicitRoutingReady: hasIsExplicitRoutingReady ? value.isExplicitRoutingReady === true : true,", script, StringComparison.Ordinal);
        Assert.Contains("var canDeriveExplicitReadiness = hasMissingRoutingContractTools", script, StringComparison.Ordinal);
        Assert.Contains("if (canDeriveExplicitReadiness && explicitReadinessIssueCount > 0) {", script, StringComparison.Ordinal);
        Assert.Contains("titleLines.push(\"Autonomy readiness:\");", script, StringComparison.Ordinal);
        Assert.Contains("lines.push(\"autonomy readiness: \" + routingCatalog.autonomyReadinessHighlights[i]);", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures lightweight tool-catalog bootstrap data includes capability snapshot fallback state,
    /// so the policy panel can show readiness before the full hello/session policy arrives.
    /// </summary>
    [Fact]
    public void Load_IncludesCapabilitySnapshotPolicyFallbackSurface() {
        var html = UiShellAssets.Load();
        var renderingScriptPath = Path.Combine(UiDirectory, RenderingScriptFile);
        var renderingScript = File.ReadAllText(renderingScriptPath);
        var coreScriptPath = Path.Combine(UiDirectory, "Shell.10.core.js");
        var coreScript = File.ReadAllText(coreScriptPath);

        Assert.Contains("id=\"policyCapabilitySnapshot\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"policyPluginDetails\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"policyPlugins\"", html, StringComparison.Ordinal);
        Assert.Contains("state.options.toolCatalogRoutingCatalog = nextOptions.toolCatalogRoutingCatalog || null;", renderingScript, StringComparison.Ordinal);
        Assert.Contains("state.options.toolCatalogPlugins = Array.isArray(nextOptions.toolCatalogPlugins) ? nextOptions.toolCatalogPlugins : [];", renderingScript, StringComparison.Ordinal);
        Assert.Contains("state.options.toolCatalogCapabilitySnapshot = nextOptions.toolCatalogCapabilitySnapshot || null;", renderingScript, StringComparison.Ordinal);
        Assert.Contains("state.options.latestRoutingPromptExposure = nextOptions.latestRoutingPromptExposure || null;", renderingScript, StringComparison.Ordinal);
        Assert.Contains("Bootstrap preview", coreScript, StringComparison.Ordinal);
        Assert.Contains("appendOptionsKv(policyEl, \"Prompt exposure\", routingPromptExposure.strategy + \" (\" + routingPromptExposure.selectedToolCount + \"/\" + routingPromptExposure.totalToolCount + \")\");", coreScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the shell summary keeps descriptor-first bootstrap labels for persisted preview and live startup phases.
    /// </summary>
    [Fact]
    public void Load_IncludesDescriptorFirstStartupBootstrapSummaryLabels() {
        var scriptPath = Path.Combine(UiDirectory, "Shell.10.core.js");
        var script = File.ReadAllText(scriptPath);

        AssertContainsAll(
            script,
            "descriptorDiscoveryMs = readStartupBootstrapPhaseMs(phases, \"descriptor_discovery\");",
            "descriptorDiscoveryMs = toNonNegativeInt(value.packLoadMs);",
            "packActivationMs = readStartupBootstrapPhaseMs(phases, \"pack_activation\");",
            "packActivationMs = toNonNegativeInt(value.packRegisterMs);",
            "registryActivationFinalizeMs = readStartupBootstrapPhaseMs(phases, \"registry_activation_finalize\");",
            "registryActivationFinalizeMs = toNonNegativeInt(value.registryFinalizeMs);",
            "descriptorDiscoveryMs: descriptorDiscoveryMs,",
            "packActivationMs: packActivationMs,",
            "registryActivationFinalizeMs: registryActivationFinalizeMs,",
            "segments.push(\"descriptor-preview\");",
            "segments.push(\"descriptor-discovery \" + formatStartupBootstrapDuration(descriptorDiscoveryMs));",
            "segments.push(\"pack-activation \" + formatStartupBootstrapDuration(packActivationMs));",
            "segments.push(\"activation-finalize \" + formatStartupBootstrapDuration(activationFinalizeMs));");
        Assert.Contains("function readStartupBootstrapPhaseMs(phases, phaseId)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("function resolveStartupBootstrapPhaseDuration", script, StringComparison.Ordinal);
        Assert.DoesNotContain("case \"pack_load\":", script, StringComparison.Ordinal);
        Assert.DoesNotContain("case \"pack_register\":", script, StringComparison.Ordinal);
        Assert.DoesNotContain("case \"registry_finalize\":", script, StringComparison.Ordinal);
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
    /// Ensures transcript rendering split stays in the expected load slot between
    /// the core rendering helpers and the data table/runtime bindings that consume it.
    /// </summary>
    [Fact]
    public void Load_EmitsTranscriptRenderingChunkBetweenRenderingAndDataTableChunks() {
        var html = UiShellAssets.Load();
        var renderingIndex = html.IndexOf("/* IXCHAT_PART:Shell.18.core.tools.rendering.js */", StringComparison.Ordinal);
        var transcriptIndex = html.IndexOf("/* IXCHAT_PART:Shell.18a.transcript.rendering.js */", StringComparison.Ordinal);
        var dataTablesIndex = html.IndexOf("/* IXCHAT_PART:Shell.16.core.datatables.js */", StringComparison.Ordinal);

        Assert.True(renderingIndex >= 0, "Missing core rendering chunk marker.");
        Assert.True(transcriptIndex >= 0, "Missing transcript rendering chunk marker.");
        Assert.True(dataTablesIndex >= 0, "Missing data table chunk marker.");
        Assert.True(renderingIndex < transcriptIndex, "Transcript chunk must load after core rendering.");
        Assert.True(transcriptIndex < dataTablesIndex, "Transcript chunk must load before data table bindings.");
    }

    /// <summary>
    /// Ensures the manifest itself keeps transcript rendering adjacent to the core rendering split.
    /// </summary>
    [Fact]
    public void JavaScriptManifest_TracksTranscriptRenderingChunkImmediatelyAfterCoreRenderingChunk() {
        var manifest = UiShellAssets.JavaScriptManifest.ToArray();
        var renderingIndex = Array.IndexOf(manifest, "Shell.18.core.tools.rendering.js");
        var transcriptIndex = Array.IndexOf(manifest, "Shell.18a.transcript.rendering.js");

        Assert.True(renderingIndex >= 0, "Core rendering chunk must stay in the manifest.");
        Assert.Equal(renderingIndex + 1, transcriptIndex);
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
            "var mermaidContinuationKeywords = [",
            "function looksLikeMermaidContinuationAfterStandaloneEnd(remainder) {",
            "function findMermaidEdgeStatementStartIndex(text, startIndex) {",
            "function trySplitCollapsedMermaidEndLine(line) {",
            "if (!remainder || !looksLikeMermaidContinuationAfterStandaloneEnd(remainder)) {",
            "function trySplitCollapsedMermaidEdgeStatements(line) {",
            ".bubble .markdown-body pre > code.language-mermaid",
            ".bubble .markdown-body pre.language-mermaid",
            "var blocks = collectMermaidBlocks(root);");
        Assert.DoesNotContain("(?<!\\S)", script, StringComparison.Ordinal);
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
        var renderingScriptPath = Path.Combine(UiDirectory, RenderingScriptFile);
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
    /// Ensures the live chat stylesheet gives OfficeIMO native network visuals the same visible sizing contract
    /// as IX-owned network hosts so upgraded historical chats do not collapse to zero-height blocks.
    /// </summary>
    [Fact]
    public void TranscriptRendering_IncludesNativeOfficeImoNetworkStyles() {
        var cssPath = Path.Combine(UiDirectory, "Shell.20.chat.css");
        var css = File.ReadAllText(cssPath);

        Assert.Contains(".omd-network {", css, StringComparison.Ordinal);
        Assert.Contains(".omd-network .omd-network-canvas {", css, StringComparison.Ordinal);
        Assert.Contains(".omd-network .vis-network:focus {", css, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures runtime apply updates remain monotonic when options payloads arrive out of order.
    /// </summary>
    [Fact]
    public void Load_IncludesRuntimeApplyRequestOrderingGuardForOptionsSync() {
        var scriptPath = Path.Combine(UiDirectory, RenderingScriptFile);
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
        var renderingScriptPath = Path.Combine(UiDirectory, RenderingScriptFile);
        var transcriptRenderingScriptPath = Path.Combine(UiDirectory, TranscriptRenderingScriptFile);
        var script = File.ReadAllText(renderingScriptPath) + Environment.NewLine + File.ReadAllText(transcriptRenderingScriptPath);

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
    /// Ensures tool cards expose searchable execution-locality labels so mixed/local-only/remote-ready tools
    /// can be spotted without relying on pack-level summaries alone.
    /// </summary>
    [Fact]
    public void Load_IncludesToolExecutionLocalityBadgesAndFilterTerms() {
        var html = UiShellAssets.Load();
        var scriptPath = Path.Combine(UiDirectory, "Shell.15.core.tools.js");
        var script = File.ReadAllText(scriptPath);
        var bindingsPath = Path.Combine(UiDirectory, "Shell.20.bindings.js");
        var bindings = File.ReadAllText(bindingsPath);
        var renderingScriptPath = Path.Combine(UiDirectory, RenderingScriptFile);
        var renderingScript = File.ReadAllText(renderingScriptPath);
        var coreScriptPath = Path.Combine(UiDirectory, "Shell.10.core.js");
        var coreScript = File.ReadAllText(coreScriptPath);
        var cssPath = Path.Combine(UiDirectory, "Shell.30.options.css");
        var css = File.ReadAllText(cssPath);

        Assert.Contains("id=\"optToolLocalityFilters\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"optToolLocalityRemoteReady\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"optToolLocalityLocalOnly\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"optToolLocalityMixed\"", html, StringComparison.Ordinal);
        Assert.Contains("data-locality-filter=\"dual_scope\">Dual-scope</button>", html, StringComparison.Ordinal);
        Assert.Contains("toolLocalityFilter: \"all\"", coreScript, StringComparison.Ordinal);
        Assert.Contains("function normalizeToolExecutionScope(value)", script, StringComparison.Ordinal);
        Assert.Contains("function normalizeToolLocalityFilter(value)", script, StringComparison.Ordinal);
        Assert.Contains("function renderToolLocalityQuickFilters()", script, StringComparison.Ordinal);
        Assert.Contains("function toolMatchesLocalityFilter(tool, localityFilter)", script, StringComparison.Ordinal);
        Assert.Contains("var localityFilter = normalizeToolLocalityFilter(state.options.toolLocalityFilter);", script, StringComparison.Ordinal);
        Assert.Contains("tools = tools.filter(function(tool) { return toolMatchesLocalityFilter(tool, localityFilter); });", script, StringComparison.Ordinal);
        Assert.Contains("if (normalized === \"dual_scope\" || normalized === \"dual-scope\" || normalized === \"mixed\") {", script, StringComparison.Ordinal);
        Assert.Contains("return \"dual_scope\";", script, StringComparison.Ordinal);
        Assert.Contains("if (localityFilter === \"dual_scope\") {", script, StringComparison.Ordinal);
        Assert.Contains("return execution.scope === \"local_or_remote\";", script, StringComparison.Ordinal);
        Assert.Contains("function resolveToolExecutionBadgeModel(tool)", script, StringComparison.Ordinal);
        Assert.Contains("function summarizePackExecutionLocality(tools)", script, StringComparison.Ordinal);
        Assert.Contains("executionPill.className = \"options-pill options-pill-execution options-pill-execution-\" + execution.status;", script, StringComparison.Ordinal);
        Assert.Contains("executionDetail.textContent = execution.note;", script, StringComparison.Ordinal);
        Assert.Contains("executionBadge.className = \"options-pill options-pill-execution options-pill-execution-\" + executionSummary.status;", script, StringComparison.Ordinal);
        Assert.Contains("label = \"Mixed locality\";", script, StringComparison.Ordinal);
        Assert.Contains("label = \"Remote-ready\";", script, StringComparison.Ordinal);
        Assert.Contains("tool.executionContractId || \"\"", script, StringComparison.Ordinal);
        Assert.Contains("\"execution-aware declared execution-contract structured\"", script, StringComparison.Ordinal);
        Assert.Contains("\"remote remote-only remote only remote-ready remote ready remote-capable remote capable\"", script, StringComparison.Ordinal);
        Assert.Contains("\"local remote local-and-remote local or remote mixed dual-scope remote-ready remote ready remote-capable remote capable\"", script, StringComparison.Ordinal);
        Assert.Contains("normalizeBool(tool.supportsRemoteExecution) ? \"supports-remote-execution remote-capable remote-ready\" : \"\"", script, StringComparison.Ordinal);
        Assert.Contains("state.options.toolLocalityFilter = normalizeToolLocalityFilter(button.getAttribute(\"data-locality-filter\"));", bindings, StringComparison.Ordinal);
        Assert.Contains("renderToolLocalityQuickFilters();", renderingScript, StringComparison.Ordinal);
        Assert.Contains(".options-pill-execution", css, StringComparison.Ordinal);
        Assert.Contains(".options-pill-execution-local", css, StringComparison.Ordinal);
        Assert.Contains(".options-pill-execution-remote", css, StringComparison.Ordinal);
        Assert.Contains(".options-pill-execution-mixed", css, StringComparison.Ordinal);
        Assert.Contains(".options-tools-filter-pills", css, StringComparison.Ordinal);
        Assert.Contains(".options-tools-filter-pills .options-pill-action.active", css, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures runtime UI supports focused provider/model/usage panel views so long settings pages stay navigable.
    /// </summary>
    [Fact]
    public void Load_IncludesRuntimePanelViewSelectorAndVisibilityGuards() {
        var html = UiShellAssets.Load();
        var scriptPath = Path.Combine(UiDirectory, "Shell.15.core.tools.js");
        var script = File.ReadAllText(scriptPath);
        var renderingScriptPath = Path.Combine(UiDirectory, RenderingScriptFile);
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
