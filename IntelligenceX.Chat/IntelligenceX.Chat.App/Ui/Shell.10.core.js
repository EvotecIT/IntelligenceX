(function() {
  function byId(id) { return document.getElementById(id); }
  function post(type, extra) {
    var payload = Object.assign({ type: type }, extra || {});
    window.chrome.webview.postMessage(JSON.stringify(payload));
  }
  // Keep bounds/default parity with ExportPreferencesContract.NormalizeDocxVisualMaxWidthPx (C# host).
  var exportDocxVisualMaxWidthContract = {
    minPx: 320,
    maxPx: 2000,
    defaultPx: 760
  };
  function normalizeDocxVisualMaxWidthPxContract(value) {
    var parsed = Number.parseInt(String(value == null ? "" : value).trim(), 10);
    if (!Number.isFinite(parsed)) {
      return exportDocxVisualMaxWidthContract.defaultPx;
    }
    if (parsed < exportDocxVisualMaxWidthContract.minPx) {
      return exportDocxVisualMaxWidthContract.minPx;
    }
    if (parsed > exportDocxVisualMaxWidthContract.maxPx) {
      return exportDocxVisualMaxWidthContract.maxPx;
    }
    return Math.floor(parsed);
  }

  var menu = byId("menu");
  var promptEl = byId("prompt");
  var dragBar = byId("dragBar");
  var workspace = byId("workspace");
  var chatSidebar = byId("chatSidebar");
  var transcript = byId("transcript");
  var chatSidebarList = byId("chatSidebarList");
  var sidebarResizeHandle = byId("sidebarResizeHandle");
  var optionsPanel = byId("optionsPanel");
  var optionsBody = optionsPanel.querySelector(".options-body");
  var dataViewBody = byId("dataViewBody");
  var visualViewBody = byId("visualViewBody");
  var optionsBackdrop = byId("optionsBackdrop");
  var openCustomSelect = null;
  var lastHostWheelAt = 0;
  var lastNativeWheelAt = 0;
  var sidebarResizeActive = false;

  var sidebarPrefs = {
    width: 228,
    collapsed: false,
    mode: "manual"
  };

  var state = {
    status: "Starting runtime...",
    statusTone: "warn",
    usageLimitSwitchRecommended: false,
    queuedPromptPending: false,
    queuedPromptCount: 0,
    queuedTurnCount: 0,
    connected: false,
    authenticated: false,
    accountId: "",
    loginInProgress: false,
    hasExplicitUnauthenticatedProbeSnapshot: false,
    sending: false,
    cancelable: false,
    cancelRequested: false,
    activityTimeline: [],
    routingPromptExposureHistory: [],
    statusTimeline: [],
    lastTurnMetrics: null,
    latencySummary: null,
    providerCircuit: null,
    windowMaximized: false,
    debugMode: false,
    expandedToolPacks: {},
    options: {
      timestampMode: "seconds",
      timestampFormat: "HH:mm:ss",
      export: {
        saveMode: "ask",
        defaultFormat: "xlsx",
        visualThemeMode: "preserve_ui_theme",
        docxVisualMaxWidthPx: exportDocxVisualMaxWidthContract.defaultPx,
        lastDirectory: ""
      },
      autonomy: {
        maxToolRounds: null,
        parallelTools: null,
        parallelToolMode: "auto",
        turnTimeoutSeconds: null,
        toolTimeoutSeconds: null,
        weightedToolRouting: null,
        maxCandidateTools: null,
        planExecuteReviewLoop: null,
        maxReviewPasses: null,
        modelHeartbeatSeconds: null,
        queueAutoDispatch: true,
        proactiveMode: false
      },
      memory: {
        enabled: true,
        count: 0,
        facts: []
      },
      runtimeScheduler: null,
      runtimeSchedulerScoped: null,
      runtimeSchedulerGlobal: null,
      memoryDebug: null,
      startupDiagnostics: null,
      activeProfileName: "default",
      profileNames: ["default"],
      activeConversationId: "",
      conversations: [],
      toolCatalogRoutingCatalog: null,
      toolCatalogPlugins: [],
      toolCatalogCapabilitySnapshot: null,
      latestRoutingPromptExposure: null,
      profileApplyMode: "session",
      profile: {
        userName: "",
        persona: "",
        theme: "default",
        onboardingCompleted: false
      },
      localModel: {
        transport: "native",
        baseUrl: "",
        modelsEndpoint: "",
        model: "gpt-5.4",
        openAIAuthMode: "bearer",
        openAIBasicUsername: "",
        openAIAccountId: "",
        activeNativeAccountSlot: 1,
        nativeAccountSlots: [],
        reasoningEffort: "",
        reasoningSummary: "",
        textVerbosity: "",
        temperature: null,
        models: [],
        favoriteModels: [],
        recentModels: [],
        isStale: false,
        warning: "",
        profileSaved: false,
        authenticatedAccountId: "",
        accountUsage: [],
        activeAccountUsage: null,
        runtimeDetection: {
          hasRun: false,
          lmStudioAvailable: false,
          ollamaAvailable: false,
          detectedName: "",
          detectedBaseUrl: "",
          warning: ""
        }
      },
      debug: {
        showTurnTrace: false,
        showDraftBubbles: false
      },
      debugToolsEnabled: false,
      toolFilter: "",
      toolLocalityFilter: "all",
      policy: null,
      packs: [],
      tools: [],
      toolsLoading: true,
      toolsCatalogPendingCount: 0
    }
  };

  function normalizeBool(value) {
    return value === true;
  }

  function toStringArray(value) {
    if (!Array.isArray(value)) {
      return [];
    }

    var list = [];
    for (var i = 0; i < value.length; i++) {
      if (typeof value[i] !== "string") {
        continue;
      }

      var normalized = value[i].trim();
      if (!normalized) {
        continue;
      }

      list.push(normalized);
    }
    return list;
  }

  function normalizeProfileApplyMode(value) {
    return value === "profile" ? "profile" : "session";
  }

  function debugToolsStorageKey() {
    var profile = (state.options.activeProfileName || "default").trim().toLowerCase();
    if (!profile) {
      profile = "default";
    }
    return "ixchat.debug.tools." + profile;
  }

  function loadDebugToolsEnabledForActiveProfile() {
    state.options.debugToolsEnabled = readStorage(debugToolsStorageKey()) === "1";
  }

  function setDebugToolsEnabled(enabled, persist) {
    state.options.debugToolsEnabled = enabled === true;
    if (persist !== false) {
      writeStorage(debugToolsStorageKey(), state.options.debugToolsEnabled ? "1" : "0");
    }
    updateMenuState();
  }

  function readStorage(key) {
    try {
      return window.localStorage ? window.localStorage.getItem(key) : null;
    } catch (_) {
      return null;
    }
  }

  function writeStorage(key, value) {
    try {
      if (!window.localStorage) {
        return;
      }
      window.localStorage.setItem(key, value);
    } catch (_) {
      // Ignore storage failures.
    }
  }

  function clampSidebarWidth(value) {
    var n = Number(value);
    if (!Number.isFinite(n)) {
      return 228;
    }
    return Math.max(170, Math.min(360, Math.round(n)));
  }

  function normalizeSidebarMode(value) {
    return value === "hover" ? "hover" : "manual";
  }

  function loadSidebarPrefs() {
    sidebarPrefs.width = clampSidebarWidth(readStorage("ixchat.sidebar.width"));
    sidebarPrefs.collapsed = readStorage("ixchat.sidebar.collapsed") === "1";
    sidebarPrefs.mode = normalizeSidebarMode(readStorage("ixchat.sidebar.mode"));
  }

  function persistSidebarPrefs() {
    writeStorage("ixchat.sidebar.width", String(sidebarPrefs.width));
    writeStorage("ixchat.sidebar.collapsed", sidebarPrefs.collapsed ? "1" : "0");
    writeStorage("ixchat.sidebar.mode", sidebarPrefs.mode);
  }

  function setSidebarWidth(width) {
    sidebarPrefs.width = clampSidebarWidth(width);
    if (!sidebarPrefs.collapsed) {
      document.documentElement.style.setProperty("--ix-sidebar-width", String(sidebarPrefs.width) + "px");
    }
    persistSidebarPrefs();
  }

  function setSidebarCollapsed(collapsed) {
    sidebarPrefs.collapsed = collapsed === true;
    document.body.classList.toggle("sidebar-collapsed", sidebarPrefs.collapsed);
    document.body.classList.remove("sidebar-hover-open");

    if (!sidebarPrefs.collapsed) {
      document.documentElement.style.setProperty("--ix-sidebar-width", String(sidebarPrefs.width) + "px");
    }

    var toggle = byId("btnSidebarToggle");
    if (toggle) {
      toggle.setAttribute("aria-label", sidebarPrefs.collapsed ? "Expand sidebar" : "Collapse sidebar");
      toggle.title = sidebarPrefs.collapsed ? "Expand sidebar" : "Collapse sidebar";
    }

    persistSidebarPrefs();
  }

  function setSidebarHoverMode(mode) {
    sidebarPrefs.mode = normalizeSidebarMode(mode);
    document.body.classList.toggle("sidebar-hover-mode", sidebarPrefs.mode === "hover");

    if (sidebarPrefs.mode !== "hover") {
      document.body.classList.remove("sidebar-hover-open");
    }

    persistSidebarPrefs();
  }

  function setSidebarHoverOpen(open) {
    if (!sidebarPrefs.collapsed || sidebarPrefs.mode !== "hover") {
      document.body.classList.remove("sidebar-hover-open");
      return;
    }

    document.body.classList.toggle("sidebar-hover-open", open === true);
  }

  function toggleSidebarCollapsed() {
    setSidebarCollapsed(!sidebarPrefs.collapsed);
  }

  function beginSidebarResize() {
    if (sidebarPrefs.collapsed) {
      return;
    }

    sidebarResizeActive = true;
    document.body.classList.add("sidebar-resizing");
  }

  function updateSidebarResize(clientX) {
    if (!sidebarResizeActive || !workspace || !chatSidebar) {
      return;
    }

    var bounds = workspace.getBoundingClientRect();
    var nextWidth = clientX - bounds.left;
    setSidebarWidth(nextWidth);
  }

  function endSidebarResize() {
    if (!sidebarResizeActive) {
      return;
    }

    sidebarResizeActive = false;
    document.body.classList.remove("sidebar-resizing");
  }

  function autoResizePrompt() {
    promptEl.style.height = "auto";
    var next = Math.max(44, Math.min(promptEl.scrollHeight, 180));
    promptEl.style.height = next + "px";
  }

  state.options.profileApplyMode = normalizeProfileApplyMode(readStorage("ixchat.profile.applyMode"));

  var IX_MODAL_MODE_NONE = "none";
  var IX_MODAL_MODE_OPTIONS = "options";
  var IX_MODAL_MODE_DATA_VIEW = "dataView";
  var IX_MODAL_MODE_VISUAL_VIEW = "visualView";

  function getActiveModalMode() {
    if (document.body.classList.contains("visual-view-open")) {
      return IX_MODAL_MODE_VISUAL_VIEW;
    }
    if (document.body.classList.contains("data-view-open")) {
      return IX_MODAL_MODE_DATA_VIEW;
    }
    if (document.body.classList.contains("options-open")) {
      return IX_MODAL_MODE_OPTIONS;
    }
    return IX_MODAL_MODE_NONE;
  }

  function getModalPrimaryScrollTarget(mode) {
    if (mode === IX_MODAL_MODE_VISUAL_VIEW) {
      return visualViewBody || null;
    }
    if (mode === IX_MODAL_MODE_DATA_VIEW) {
      return dataViewBody || null;
    }
    if (mode === IX_MODAL_MODE_OPTIONS) {
      return optionsBody || null;
    }
    return null;
  }

  function resolveModalWheelScrollTarget(mode, eventTarget) {
    if (mode === IX_MODAL_MODE_VISUAL_VIEW) {
      if (eventTarget && eventTarget.closest) {
        var visualViewScrollTarget = eventTarget.closest("#visualViewBody, .visual-view-panel");
        if (visualViewScrollTarget) {
          return visualViewScrollTarget;
        }
      }

      return getModalPrimaryScrollTarget(mode);
    }

    if (mode === IX_MODAL_MODE_DATA_VIEW) {
      if (eventTarget && eventTarget.closest) {
        var dataViewScrollTarget = eventTarget.closest(".dt-scroll-body, #dataViewBody, .data-view-panel .dt-layout-cell");
        if (dataViewScrollTarget) {
          return dataViewScrollTarget;
        }
      }

      return getModalPrimaryScrollTarget(mode);
    }

    if (mode === IX_MODAL_MODE_OPTIONS) {
      var openSelectMenu = openCustomSelect ? openCustomSelect.querySelector(".ix-select-menu") : null;
      if (openSelectMenu && eventTarget && openSelectMenu.contains(eventTarget)) {
        return openSelectMenu;
      }

      if (eventTarget && eventTarget.closest) {
        var optionScrollTarget = eventTarget.closest(".options-body, .options-tools, .options-conversations, .ix-select-menu");
        if (optionScrollTarget) {
          return optionScrollTarget;
        }
      }

      return getModalPrimaryScrollTarget(mode);
    }

    return null;
  }

  function resolveWheelZoneName(mode) {
    if (mode === IX_MODAL_MODE_VISUAL_VIEW) {
      return "visualView";
    }
    if (mode === IX_MODAL_MODE_DATA_VIEW) {
      return "dataView";
    }
    if (mode === IX_MODAL_MODE_OPTIONS) {
      return "options";
    }
    return "transcript";
  }

  function applyPagedScrollKey(target, key, minStepPx) {
    if (!target) {
      return false;
    }

    var minimum = Number(minStepPx);
    if (!Number.isFinite(minimum) || minimum <= 0) {
      minimum = 120;
    }

    var step = Math.max(minimum, Math.floor(target.clientHeight * 0.9));
    switch (key) {
      case "PageDown":
        target.scrollTop += step;
        return true;
      case "PageUp":
        target.scrollTop -= step;
        return true;
      case "Home":
        target.scrollTop = 0;
        return true;
      case "End":
        target.scrollTop = target.scrollHeight;
        return true;
      default:
        return false;
    }
  }

  function isEditableElement(el) {
    if (!el || !el.tagName) {
      return false;
    }

    var tag = el.tagName.toLowerCase();
    if (tag === "textarea") {
      return true;
    }

    if (tag === "input") {
      var type = (el.type || "text").toLowerCase();
      return type !== "button" && type !== "checkbox" && type !== "radio" && type !== "submit";
    }

    if (el.isContentEditable) {
      return true;
    }

    return false;
  }

  function resolveWheelScrollTarget(eventTarget) {
    var mode = getActiveModalMode();
    var modalTarget = resolveModalWheelScrollTarget(mode, eventTarget || null);
    if (modalTarget) {
      return modalTarget;
    }

    return transcript;
  }

  function applyWheelDelta(deltaY, eventTarget) {
    var amount = Number(deltaY);
    if (!Number.isFinite(amount) || amount === 0) {
      return false;
    }

    var target = resolveWheelScrollTarget(eventTarget || null);
    if (!target) {
      return false;
    }

    var before = target.scrollTop;
    target.scrollTop += amount;
    if (target.scrollTop !== before) {
      return true;
    }

    var mode = getActiveModalMode();
    var modalTarget = getModalPrimaryScrollTarget(mode);
    if (mode !== IX_MODAL_MODE_NONE && modalTarget && target !== modalTarget) {
      before = modalTarget.scrollTop;
      modalTarget.scrollTop += amount;
      if (modalTarget.scrollTop !== before) {
        return true;
      }
      return false;
    }

    if (mode === IX_MODAL_MODE_NONE && target !== transcript) {
      before = transcript.scrollTop;
      transcript.scrollTop += amount;
      return transcript.scrollTop !== before;
    }

    return false;
  }

  function openOptions() {
    if (document.body.classList.contains("visual-view-open") && window.ixCloseVisualView) {
      window.ixCloseVisualView();
    }
    if (document.body.classList.contains("data-view-open") && window.ixCloseDataView) {
      window.ixCloseDataView();
    }
    document.body.classList.add("options-open");
    optionsPanel.setAttribute("aria-hidden", "false");
    menu.classList.remove("open");
    restoreOptionsTab();
    post("options_refresh");
  }

  function closeOptions() {
    closeOpenCustomSelect();
    document.body.classList.remove("options-open");
    optionsPanel.setAttribute("aria-hidden", "true");
  }

  function switchOptionsTab(tabId) {
    closeOpenCustomSelect();
    var tabs = optionsPanel.querySelectorAll(".options-tab");
    var contents = optionsPanel.querySelectorAll(".options-tab-content");
    for (var i = 0; i < tabs.length; i++) {
      tabs[i].classList.toggle("active", tabs[i].dataset.tab === tabId);
    }
    for (var j = 0; j < contents.length; j++) {
      contents[j].classList.toggle("active", contents[j].dataset.tab === tabId);
    }
    if (tabId === "tools") {
      var hasVisibleToolState = state.options
        && ((Array.isArray(state.options.tools) && state.options.tools.length > 0)
          || (Array.isArray(state.options.packs) && state.options.packs.length > 0));
      if (state.connected && !hasVisibleToolState) {
        state.options.toolsLoading = true;
        if (typeof renderTools === "function") {
          renderTools();
        }
      }
    }
    writeStorage("ixchat.options.tab", tabId);
  }

  function restoreOptionsTab() {
    var saved = readStorage("ixchat.options.tab");
    if (saved) {
      var exists = optionsPanel.querySelector('.options-tab[data-tab="' + saved + '"]');
      if (exists) {
        switchOptionsTab(saved);
        return;
      }
    }
    switchOptionsTab("profile");
  }

  function resolveStatusRuntimeSummary() {
    var local = (state.options && state.options.localModel) || {};
    var transport = String(local.transport || "native").trim().toLowerCase();
    var baseUrl = String(local.baseUrl || "").trim().toLowerCase();
    var model = String(local.model || "").trim();

    var runtimeLabel = "ChatGPT";
    if (transport === "copilot-cli") {
      runtimeLabel = "Copilot";
    } else if (transport === "compatible-http") {
      if (baseUrl.indexOf("127.0.0.1:1234") >= 0 || baseUrl.indexOf("localhost:1234") >= 0) {
        runtimeLabel = "LM Studio";
      } else if (baseUrl.indexOf("127.0.0.1:11434") >= 0 || baseUrl.indexOf("localhost:11434") >= 0) {
        runtimeLabel = "Ollama";
      } else if (baseUrl.indexOf("copilot") >= 0) {
        runtimeLabel = "Copilot HTTP";
      } else {
        runtimeLabel = "Compatible HTTP";
      }
    }

    return runtimeLabel + " | " + (model || "(auto)");
  }

  function shouldRenderHeaderStatusChip(value) {
    var normalized = String(value || "").trim();
    if (!normalized) {
      return false;
    }

    var lower = normalized.toLowerCase();
    if (lower.length > 72) {
      return false;
    }

    // Centralized gate: keep the top header chip scoped to compact runtime/session states.
    // Longer operational updates belong in activity/system messages, not in the title-bar chip.
    if (lower.indexOf("queued") >= 0
      || lower.indexOf("queue ") >= 0
      || lower.indexOf("prompt ") >= 0
      || lower.indexOf("turn ") >= 0
      || lower.indexOf("retry") >= 0
      || lower.indexOf("switch account") >= 0
      || lower.indexOf("usage limit") >= 0
      || lower.indexOf("account limit") >= 0
      || lower.indexOf("post-login verification") >= 0
      || lower.indexOf("paused") >= 0
      || lower.indexOf("remaining") >= 0
      || lower.indexOf("applying") >= 0
      || lower.indexOf("export") >= 0) {
      return false;
    }

    return lower.indexOf("ready") === 0
      || lower.indexOf("connected") === 0
      || lower.indexOf("starting runtime") === 0
      || lower.indexOf("sign in to continue") === 0
      || lower.indexOf("waiting for sign-in") === 0
      || lower.indexOf("finish sign-in in browser") === 0
      || lower.indexOf("opening sign-in") === 0
      || lower.indexOf("runtime unavailable") === 0
      || lower.indexOf("sign in failed") === 0
      || lower.indexOf("canceling") === 0
      || lower.indexOf("previous request still running") === 0
      || lower.indexOf("debug mode on") === 0;
  }

  function parseStartupStatusContext(value) {
    var normalized = String(value || "").trim();
    if (!normalized) {
      return null;
    }

    var match = normalized.match(/\(\s*phase\s+([a-z0-9_]+)(?:\s*,\s*cause\s+([a-z0-9_]+))?\s*\)/i);
    if (!match) {
      return null;
    }

    var phase = String(match[1] || "").trim().toLowerCase();
    if (!phase) {
      return null;
    }

    var cause = String(match[2] || "").trim().toLowerCase();
    return {
      phase: phase,
      cause: cause
    };
  }

  function stripStartupStatusContextSuffix(value) {
    var normalized = String(value || "").trim();
    if (!normalized) {
      return "";
    }

    return normalized
      .replace(/\s*\(\s*phase\s+[a-z0-9_]+(?:\s*,\s*cause\s+[a-z0-9_]+)?\s*\)\s*$/i, "")
      .trim();
  }

  function isStartupSignInWaitStatusText(value) {
    var lower = String(value || "").toLowerCase();
    return lower.indexOf("waiting for sign-in") >= 0
      || lower.indexOf("finish sign-in") >= 0
      || lower.indexOf("sign in to finish loading tool packs") >= 0
      || lower.indexOf("sign in to continue loading tool packs") >= 0;
  }

  function isStartupAuthVerificationStatusText(value) {
    var lower = String(value || "").toLowerCase();
    return lower.indexOf("verifying sign-in state before loading tool packs") >= 0;
  }

  function isStartupAuthGateActiveFromDiagnostics() {
    var startupDiagnostics = state.options && state.options.startupDiagnostics;
    if (!startupDiagnostics || typeof startupDiagnostics !== "object") {
      return false;
    }

    var authGate = startupDiagnostics.authGate;
    if (!authGate || typeof authGate !== "object") {
      return false;
    }

    return authGate.active === true;
  }

  function isStartupSignInWaitStillRelevant(rawStatus, connected, requiresInteractiveSignIn, authenticated, loginInProgress, authGateActive) {
    if (!isStartupSignInWaitStatusText(rawStatus)) {
      return false;
    }

    if (loginInProgress || authGateActive) {
      return true;
    }

    return requiresInteractiveSignIn
      && connected
      && !authenticated
      && normalizeBool(state.hasExplicitUnauthenticatedProbeSnapshot);
  }

  function isStartupAuthVerificationStillRelevant(rawStatus, connected, requiresInteractiveSignIn, authenticated, loginInProgress, authGateActive) {
    if (!isStartupAuthVerificationStatusText(rawStatus)) {
      return false;
    }

    if (loginInProgress || authGateActive) {
      return true;
    }

    return requiresInteractiveSignIn
      && connected
      && !authenticated
      && !normalizeBool(state.hasExplicitUnauthenticatedProbeSnapshot);
  }

  function isStartupDiagnosticsPhaseResultKnown(value) {
    var normalized = String(value || "").trim().toLowerCase();
    return normalized === "success" || normalized === "failed";
  }

  function shouldSuppressStartupMetadataContextFromDiagnostics(context) {
    if (!context || context.phase !== "startup_metadata_sync") {
      return false;
    }

    var startupDiagnostics = state.options && state.options.startupDiagnostics;
    if (!startupDiagnostics || typeof startupDiagnostics !== "object") {
      return false;
    }

    var metadataSync = startupDiagnostics.metadataSync;
    if (!metadataSync || typeof metadataSync !== "object") {
      return false;
    }

    if (metadataSync.inProgress === true || metadataSync.queued === true) {
      return false;
    }

    var failureRecovery = metadataSync.failureRecovery;
    if (failureRecovery && typeof failureRecovery === "object") {
      if (failureRecovery.rerunRequested === true) {
        return false;
      }

      var limitReachedCount = Number(failureRecovery.limitReachedCount);
      if (Number.isFinite(limitReachedCount) && limitReachedCount > 0) {
        return true;
      }
    }

    var metadataResult = String(metadataSync.result || "").trim().toLowerCase();
    if (metadataResult === "failed" || metadataResult === "success") {
      return true;
    }

    var helloDiag = startupDiagnostics.hello;
    var listToolsDiag = startupDiagnostics.listTools;
    var helloResult = helloDiag && typeof helloDiag === "object"
      ? String(helloDiag.result || "").trim().toLowerCase()
      : "";
    var listToolsResult = listToolsDiag && typeof listToolsDiag === "object"
      ? String(listToolsDiag.result || "").trim().toLowerCase()
      : "";

    return isStartupDiagnosticsPhaseResultKnown(helloResult)
      || isStartupDiagnosticsPhaseResultKnown(listToolsResult);
  }

  function requiresInteractiveSignInForStartup() {
    var localModel = state.options && state.options.localModel;
    if (!localModel || typeof localModel !== "object") {
      return false;
    }

    var transport = String(localModel.transport || "").trim().toLowerCase();
    return transport === "native";
  }

  function extractStartupToolPackProgress(value) {
    var match = String(value || "").match(/tool packs?\s+(\d+)\s*\/\s*(\d+)/i);
    if (!match) {
      return "";
    }

    return match[1] + "/" + match[2];
  }

  function startupPhaseStateLabel(value) {
    if (value === "active") {
      return "active";
    }
    if (value === "done") {
      return "done";
    }
    if (value === "skipped") {
      return "optional";
    }
    return "pending";
  }

  var STARTUP_HEADER_STAGE_TOTAL = 4;
  var STARTUP_HEADER_WARN_AFTER_MS = 15000;
  var STARTUP_HEADER_TIMEOUT_AFTER_MS = 45000;
  var STARTUP_HEADER_METADATA_BACKGROUND_AFTER_MS = 6000;
  var STARTUP_PROGRESS_STAGE_ORDER = ["startup_connect", "startup_auth_wait", "startup_metadata_sync", "startup_ready"];
  var STARTUP_PROGRESS_STAGE_WEIGHTS = {
    startup_connect: 25,
    startup_auth_wait: 20,
    startup_metadata_sync: 45,
    startup_ready: 10
  };
  var startupHeaderPhaseTracker = {
    activeKey: "",
    startedAtMs: 0,
    cycleStartedAtMs: 0
  };

  function resetStartupHeaderPhaseTracker() {
    startupHeaderPhaseTracker.activeKey = "";
    startupHeaderPhaseTracker.startedAtMs = 0;
    startupHeaderPhaseTracker.cycleStartedAtMs = 0;
  }

  function resolveStartupStageIndex(key) {
    if (key === "startup_connect") {
      return 1;
    }
    if (key === "startup_auth_wait") {
      return 2;
    }
    if (key === "startup_metadata_sync") {
      return 3;
    }
    return 4;
  }

  function resolveStartupStageLabel(key) {
    if (key === "startup_connect") {
      return "Runtime connect";
    }
    if (key === "startup_auth_wait") {
      return "Authentication gate";
    }
    if (key === "startup_metadata_sync") {
      return "Tool packs + metadata";
    }
    return "Working state";
  }

  function normalizeStartupPhaseKey(value) {
    var key = String(value || "").trim().toLowerCase();
    if (key === "startup_connect" || key === "startup_auth_wait" || key === "startup_metadata_sync" || key === "startup_ready") {
      return key;
    }
    return "startup_ready";
  }

  function formatStartupHeaderElapsed(ms) {
    var value = Number(ms);
    if (!Number.isFinite(value) || value <= 0) {
      return "0s";
    }

    if (value < 1000) {
      return Math.max(1, Math.floor(value)) + "ms";
    }

    return Math.max(1, Math.floor(value / 1000)) + "s";
  }

  function clampStartupProgressRatio(value) {
    var normalized = Number(value);
    if (!Number.isFinite(normalized)) {
      return 0;
    }
    if (normalized <= 0) {
      return 0;
    }
    if (normalized >= 1) {
      return 1;
    }
    return normalized;
  }

  function resolveStartupStageWeight(key) {
    var normalizedKey = normalizeStartupPhaseKey(key);
    var weight = Number(STARTUP_PROGRESS_STAGE_WEIGHTS[normalizedKey]);
    if (!Number.isFinite(weight) || weight <= 0) {
      return 0;
    }
    return weight;
  }

  function resolveStartupRowState(rows, key) {
    var normalizedKey = normalizeStartupPhaseKey(key);
    var normalizedRows = Array.isArray(rows) ? rows : [];
    for (var i = 0; i < normalizedRows.length; i++) {
      var row = normalizedRows[i] || {};
      if (normalizeStartupPhaseKey(row.key) !== normalizedKey) {
        continue;
      }

      var rowState = String(row.state || "pending").trim().toLowerCase();
      if (rowState === "active" || rowState === "done" || rowState === "skipped") {
        return rowState;
      }

      return "pending";
    }

    return "pending";
  }

  function resolveStartupStageActiveFraction(activeKey, rawStatus, elapsedMs, loginInProgress) {
    var normalizedKey = normalizeStartupPhaseKey(activeKey);
    var lower = String(rawStatus || "").toLowerCase();
    var elapsed = Number(elapsedMs);
    if (!Number.isFinite(elapsed) || elapsed < 0) {
      elapsed = 0;
    }

    if (normalizedKey === "startup_connect") {
      var connectElapsedRatio = clampStartupProgressRatio(elapsed / 25000);
      var connectAttemptRatio = 0;
      var attemptMatch = String(rawStatus || "").match(/attempt\s+(\d+)\s*\/\s*(\d+)/i);
      if (attemptMatch) {
        var attemptCurrent = Number(attemptMatch[1]);
        var attemptTotal = Number(attemptMatch[2]);
        if (Number.isFinite(attemptCurrent) && Number.isFinite(attemptTotal) && attemptTotal > 0) {
          connectAttemptRatio = clampStartupProgressRatio(attemptCurrent / attemptTotal);
        }
      }

      return clampStartupProgressRatio(Math.max(0.12 + connectElapsedRatio * 0.78, connectAttemptRatio * 0.85));
    }

    if (normalizedKey === "startup_auth_wait") {
      var authElapsedRatio = clampStartupProgressRatio(elapsed / 30000);
      var authBaseRatio = loginInProgress ? 0.25 : 0.35;
      if (lower.indexOf("verifying sign-in state") >= 0) {
        authBaseRatio = Math.max(authBaseRatio, 0.45);
      }
      return clampStartupProgressRatio(authBaseRatio + authElapsedRatio * 0.45);
    }

    if (normalizedKey === "startup_metadata_sync") {
      var metadataElapsedRatio = clampStartupProgressRatio(elapsed / 45000);
      var metadataPhaseRatio = 0.2;
      if (lower.indexOf("syncing session policy") >= 0 || lower.indexOf("session policy synced") >= 0) {
        metadataPhaseRatio = 0.3;
      }
      if (lower.indexOf("tool catalog") >= 0) {
        metadataPhaseRatio = 0.62;
      }
      if (lower.indexOf("authentication refresh") >= 0 || lower.indexOf("authentication refreshed") >= 0) {
        metadataPhaseRatio = 0.86;
      }

      var metadataPackRatio = 0;
      var packProgress = extractStartupToolPackProgress(rawStatus);
      if (packProgress) {
        var packParts = packProgress.split("/");
        if (packParts.length === 2) {
          var packCurrent = Number(packParts[0]);
          var packTotal = Number(packParts[1]);
          if (Number.isFinite(packCurrent) && Number.isFinite(packTotal) && packTotal > 0) {
            metadataPackRatio = clampStartupProgressRatio(packCurrent / packTotal);
          }
        }
      }

      return clampStartupProgressRatio(Math.max(metadataPhaseRatio, metadataPackRatio * 0.95, 0.18 + metadataElapsedRatio * 0.75));
    }

    if (normalizedKey === "startup_ready") {
      var readyElapsedRatio = clampStartupProgressRatio(elapsed / 12000);
      return clampStartupProgressRatio(0.55 + readyElapsedRatio * 0.4);
    }

    return clampStartupProgressRatio(elapsed / 20000);
  }

  function buildStartupProgressSnapshot(rows, activeKey, rawStatus, elapsedMs, loginInProgress) {
    var normalizedRows = Array.isArray(rows) ? rows : [];
    var normalizedKey = normalizeStartupPhaseKey(activeKey);
    var totalWeight = 0;
    var completedWeight = 0;
    var activeStagePercent = 0;

    for (var i = 0; i < STARTUP_PROGRESS_STAGE_ORDER.length; i++) {
      var stageKey = STARTUP_PROGRESS_STAGE_ORDER[i];
      var stageWeight = resolveStartupStageWeight(stageKey);
      if (stageWeight <= 0) {
        continue;
      }

      totalWeight += stageWeight;
      var stageState = resolveStartupRowState(normalizedRows, stageKey);
      var stageRatio = 0;
      if (stageState === "done" || stageState === "skipped") {
        stageRatio = 1;
      } else if (stageState === "active" || stageKey === normalizedKey) {
        stageRatio = resolveStartupStageActiveFraction(stageKey, rawStatus, elapsedMs, loginInProgress);
      }

      stageRatio = clampStartupProgressRatio(stageRatio);
      completedWeight += stageWeight * stageRatio;
      if (stageKey === normalizedKey) {
        activeStagePercent = Math.round(stageRatio * 100);
      }
    }

    if (totalWeight <= 0) {
      return {
        progressPercent: 0,
        stagePercent: 0
      };
    }

    var progressPercent = Math.round((completedWeight / totalWeight) * 100);
    progressPercent = Math.max(0, Math.min(100, progressPercent));
    if (normalizedKey !== "startup_ready") {
      progressPercent = Math.min(progressPercent, 99);
    } else {
      progressPercent = Math.max(progressPercent, 90);
    }
    if (normalizedKey === "startup_connect" && progressPercent < 1) {
      progressPercent = 1;
    }

    return {
      progressPercent: progressPercent,
      stagePercent: Math.max(0, Math.min(100, activeStagePercent))
    };
  }

  function isStartupHeaderCandidate(rawStatus) {
    var normalized = String(rawStatus || "").trim();
    var connected = normalizeBool(state.connected);
    var requiresInteractiveSignIn = requiresInteractiveSignInForStartup();
    var authenticated = normalizeBool(state.authenticated);
    var loginInProgress = normalizeBool(state.loginInProgress);
    var authGateActive = isStartupAuthGateActiveFromDiagnostics();
    var signInWaitRelevant = isStartupSignInWaitStillRelevant(
      normalized,
      connected,
      requiresInteractiveSignIn,
      authenticated,
      loginInProgress,
      authGateActive);
    if (!normalized) {
      return !connected;
    }

    var toolsLoading = normalizeBool(state.options && state.options.toolsLoading);
    var startupContext = parseStartupStatusContext(normalized);
    if (startupContext && !isStaleStartupStatusContextCandidate(
      normalized,
      startupContext,
      connected,
      toolsLoading,
      loginInProgress)) {
      return true;
    }

    if (toolsLoading) {
      return true;
    }

    if (!connected) {
      return true;
    }

    var lower = normalized.toLowerCase();
    return lower.indexOf("starting runtime") === 0
      || lower.indexOf("runtime connected. loading tool packs") === 0
      || lower.indexOf("runtime connected. sign in to finish loading tool packs") === 0
      || signInWaitRelevant;
  }

  function isStaleStartupStatusContextCandidate(rawStatus, context, connected, toolsLoading, loginInProgress) {
    if (shouldSuppressStartupMetadataContextFromDiagnostics(context)) {
      return true;
    }

    if (!context || !connected || toolsLoading || loginInProgress) {
      return false;
    }

    if (context.phase !== "startup_connect" && context.phase !== "startup_metadata_sync") {
      return false;
    }

    var requiresInteractiveSignIn = requiresInteractiveSignInForStartup();
    var authenticated = normalizeBool(state.authenticated);
    var authGateActive = isStartupAuthGateActiveFromDiagnostics();
    var signInWaitRelevant = isStartupSignInWaitStillRelevant(
      rawStatus,
      connected,
      requiresInteractiveSignIn,
      authenticated,
      loginInProgress,
      authGateActive);
    var authVerificationRelevant = isStartupAuthVerificationStillRelevant(
      rawStatus,
      connected,
      requiresInteractiveSignIn,
      authenticated,
      loginInProgress,
      authGateActive);
    if (isStartupSignInWaitStatusText(rawStatus) || isStartupAuthVerificationStatusText(rawStatus)) {
      return !signInWaitRelevant && !authVerificationRelevant;
    }

    return true;
  }

  function resolveHeaderStartupProgressStatus(rawStatus) {
    if (!isStartupHeaderCandidate(rawStatus)) {
      resetStartupHeaderPhaseTracker();
      return null;
    }

    var startupModel = buildStartupPhaseTimelineModel();
    var rows = Array.isArray(startupModel.rows) ? startupModel.rows : [];
    if (rows.length === 0) {
      resetStartupHeaderPhaseTracker();
      return null;
    }

    var readyRow = null;
    for (var i = 0; i < rows.length; i++) {
      if (rows[i] && rows[i].key === "startup_ready") {
        readyRow = rows[i];
        break;
      }
    }
    var readyDone = readyRow && readyRow.state === "done";
    var metadataRowActive = false;
    for (var m = 0; m < rows.length; m++) {
      if (rows[m] && rows[m].key === "startup_metadata_sync" && rows[m].state === "active") {
        metadataRowActive = true;
        break;
      }
    }
    var suppressMetadataFromDiagnostics = shouldSuppressStartupMetadataContextFromDiagnostics({ phase: "startup_metadata_sync" });
    if (readyDone) {
      resetStartupHeaderPhaseTracker();
      if (metadataRowActive || (normalizeBool(state.options && state.options.toolsLoading) && !suppressMetadataFromDiagnostics)) {
        return {
          text: "Ready",
          tone: "ok",
          startupSummary: startupModel.summary,
          startupStageLabel: "Working state",
          startupElapsedLabel: "",
          progressPercent: -1,
          progressStagePercent: -1,
          progressStageLabel: ""
        };
      }

      return null;
    }

    var activeRow = null;
    for (var a = 0; a < rows.length; a++) {
      if (rows[a] && rows[a].state === "active") {
        activeRow = rows[a];
        break;
      }
    }
    if (!activeRow) {
      for (var p = 0; p < rows.length; p++) {
        if (rows[p] && rows[p].state === "pending") {
          activeRow = rows[p];
          break;
        }
      }
    }
    if (!activeRow) {
      activeRow = rows[rows.length - 1];
    }

    var activeKey = normalizeStartupPhaseKey(activeRow ? activeRow.key : "");
    var stageIndex = resolveStartupStageIndex(activeKey);
    var stageLabel = resolveStartupStageLabel(activeKey);
    var now = Date.now();
    if (startupHeaderPhaseTracker.cycleStartedAtMs <= 0) {
      startupHeaderPhaseTracker.cycleStartedAtMs = now;
    }
    if (startupHeaderPhaseTracker.activeKey !== activeKey || startupHeaderPhaseTracker.startedAtMs <= 0) {
      startupHeaderPhaseTracker.activeKey = activeKey;
      startupHeaderPhaseTracker.startedAtMs = now;
    }
    var elapsedMs = Math.max(0, now - startupHeaderPhaseTracker.startedAtMs);
    var elapsedLabel = formatStartupHeaderElapsed(elapsedMs);
    var progressSnapshot = buildStartupProgressSnapshot(rows, activeKey, rawStatus, elapsedMs, normalizeBool(state.loginInProgress));
    var progressPercent = Math.max(0, Math.min(100, Math.round(progressSnapshot.progressPercent || 0)));
    var lowerRawStatus = String(rawStatus || "").toLowerCase();
    var text = "Starting runtime";
    var tone = "warn";

    if (activeKey === "startup_connect") {
      text = "Starting runtime";
      if (lowerRawStatus.indexOf("retry") >= 0) {
        text = "Starting runtime";
      }
    } else if (activeKey === "startup_auth_wait") {
      if (isStartupAuthVerificationStatusText(rawStatus)) {
        text = "Checking sign-in";
      } else {
        text = "Sign in required";
      }
    } else if (activeKey === "startup_metadata_sync") {
      text = "Loading tools";
    } else if (activeKey === "startup_ready") {
      text = "Finalizing";
    }

    if (activeKey === "startup_metadata_sync"
      && normalizeBool(state.connected)
      && !normalizeBool(state.loginInProgress)
      && elapsedMs >= STARTUP_HEADER_METADATA_BACKGROUND_AFTER_MS) {
      return {
        text: "Ready",
        tone: "ok",
        startupSummary: startupModel.summary,
        startupStageLabel: "Working state",
        startupElapsedLabel: elapsedLabel,
        progressPercent: -1,
        progressStagePercent: -1,
        progressStageLabel: ""
      };
    }

    if (activeKey !== "startup_auth_wait") {
      if (elapsedMs >= STARTUP_HEADER_TIMEOUT_AFTER_MS) {
        tone = "bad";
        text = "Startup delayed";
      }
    }

    return {
      text: text,
      tone: tone,
      startupSummary: startupModel.summary,
      startupStageLabel: stageLabel,
      startupElapsedLabel: elapsedLabel,
      progressPercent: progressPercent,
      progressStagePercent: Math.max(0, Math.min(100, Math.round(progressSnapshot.stagePercent || 0))),
      progressStageLabel: "Stage " + stageIndex + "/" + STARTUP_HEADER_STAGE_TOTAL
    };
  }

  function buildStartupPhaseTimelineModel() {
    var rawStatus = String(state.status || "").trim();
    var statusEntries = Array.isArray(state.statusTimeline) ? state.statusTimeline : [];
    var connected = normalizeBool(state.connected);
    var authenticated = normalizeBool(state.authenticated);
    var requiresInteractiveSignIn = requiresInteractiveSignInForStartup();
    var toolsLoading = normalizeBool(state.options && state.options.toolsLoading);
    var loginInProgress = normalizeBool(state.loginInProgress);
    var hasExplicitUnauthenticatedProbeSnapshot = normalizeBool(state.hasExplicitUnauthenticatedProbeSnapshot);
    var authGateActive = isStartupAuthGateActiveFromDiagnostics();
    var signInWaitRelevant = isStartupSignInWaitStillRelevant(
      rawStatus,
      connected,
      requiresInteractiveSignIn,
      authenticated,
      loginInProgress,
      authGateActive);
    var context = parseStartupStatusContext(rawStatus);
    if (isStaleStartupStatusContextCandidate(rawStatus, context, connected, toolsLoading, loginInProgress)) {
      context = null;
    }
    var authVerificationPending = isStartupAuthVerificationStillRelevant(
      rawStatus,
      connected,
      requiresInteractiveSignIn,
      authenticated,
      loginInProgress,
      authGateActive);
    var suppressMetadataFromDiagnostics = shouldSuppressStartupMetadataContextFromDiagnostics({ phase: "startup_metadata_sync" });
    var effectiveToolsLoading = toolsLoading && !suppressMetadataFromDiagnostics;
    var waitingForSignIn = signInWaitRelevant
      || (requiresInteractiveSignIn && authGateActive)
      || (context && context.phase === "startup_auth_wait" && !authVerificationPending)
      || (requiresInteractiveSignIn
        && connected
        && !authenticated
        && hasExplicitUnauthenticatedProbeSnapshot
        && String(rawStatus).toLowerCase().indexOf("sign in to continue") === 0);

    var activePhase = context ? context.phase : "";
    if (activePhase === "startup_metadata_sync" && waitingForSignIn) {
      activePhase = "startup_auth_wait";
    }

    var seenConnect = false;
    var seenAuth = false;
    var seenMetadata = false;
    for (var i = 0; i < statusEntries.length; i++) {
      var parsed = parseStartupStatusContext(statusEntries[i]);
      if (!parsed) {
        continue;
      }

      if (parsed.phase === "startup_connect") {
        seenConnect = true;
      } else if (parsed.phase === "startup_auth_wait") {
        seenAuth = true;
      } else if (parsed.phase === "startup_metadata_sync") {
        seenMetadata = true;
      }
    }

    var metadataActive = activePhase === "startup_metadata_sync"
      || (effectiveToolsLoading && connected && !waitingForSignIn);
    var connectDone = connected || seenConnect || activePhase === "startup_auth_wait" || metadataActive;
    var authDone = authenticated || !requiresInteractiveSignIn || (connected && !hasExplicitUnauthenticatedProbeSnapshot && !authGateActive);
    var metadataDone = !metadataActive && !effectiveToolsLoading && (seenMetadata || connectDone);
    var metadataBackgroundFallback = false;
    if (metadataActive
      && connected
      && !waitingForSignIn
      && !loginInProgress
      && startupHeaderPhaseTracker.activeKey === "startup_metadata_sync"
      && startupHeaderPhaseTracker.startedAtMs > 0
      && Date.now() - startupHeaderPhaseTracker.startedAtMs >= STARTUP_HEADER_METADATA_BACKGROUND_AFTER_MS) {
      metadataBackgroundFallback = true;
      metadataActive = false;
      metadataDone = true;
    }
    var runtimeReady = connected
      && !waitingForSignIn
      && !loginInProgress
      && (authDone || !requiresInteractiveSignIn || !authGateActive);
    var readyDone = runtimeReady;
    var statusDetail = stripStartupStatusContextSuffix(rawStatus);
    var packProgress = extractStartupToolPackProgress(rawStatus);

    var rows = [];
    rows.push({
      key: "startup_connect",
      label: "Runtime connect",
      state: activePhase === "startup_connect" ? "active" : (connectDone ? "done" : "pending"),
      detail: activePhase === "startup_connect"
        ? (statusDetail || "Connecting to local runtime service.")
        : (connectDone ? "Runtime connection established." : "Waiting for runtime process and pipe.")
    });

    var authState = "pending";
    var authDetail = "Will activate only when runtime requests authentication.";
    if (waitingForSignIn || activePhase === "startup_auth_wait") {
      authState = "active";
      authDetail = statusDetail || (authVerificationPending
        ? "Verifying sign-in state before startup sync continues."
        : "Sign in is required before startup sync can continue.");
    } else if (authDone) {
      authState = "done";
      authDetail = "Authentication complete.";
    } else if (connected && !authenticated && !requiresInteractiveSignIn) {
      authState = "skipped";
      authDetail = "No active sign-in gate for the current runtime mode.";
    } else if (connected && !authenticated && !waitingForSignIn && !loginInProgress && !authGateActive) {
      authState = "skipped";
      authDetail = "No active sign-in challenge; authentication checks continue in background.";
    }
    rows.push({
      key: "startup_auth_wait",
      label: "Authentication gate",
      state: authState,
      detail: authDetail
    });

    var metadataDetail = "Waiting for startup metadata phase.";
    if (!connectDone) {
      metadataDetail = "Runs after runtime connection succeeds.";
    } else if (metadataDone) {
      metadataDetail = metadataBackgroundFallback
        ? "Tool pack metadata is still syncing in background."
        : "Tool pack metadata is synchronized.";
    } else if (metadataActive) {
      metadataDetail = statusDetail || "Loading tool packs and runtime metadata.";
    }
    if (packProgress) {
      metadataDetail += " (" + packProgress + ")";
    }
    rows.push({
      key: "startup_metadata_sync",
      label: "Tool packs + metadata",
      state: metadataActive ? "active" : (metadataDone ? "done" : "pending"),
      detail: metadataDetail
    });

    var readyState = "pending";
    var readyDetail = "Waiting for final startup checks.";
    if (readyDone) {
      readyState = "done";
      readyDetail = metadataBackgroundFallback || metadataActive || toolsLoading
        || effectiveToolsLoading
        ? "Runtime is ready. Tool pack metadata is still syncing in background."
        : (statusDetail || "Runtime is ready.");
    } else if (!connectDone) {
      readyDetail = "Waiting for runtime connection.";
    } else if (waitingForSignIn) {
      readyDetail = "Waiting for sign-in to complete.";
    } else if (authVerificationPending) {
      readyDetail = "Verifying sign-in state before startup metadata sync.";
    } else if (metadataActive || effectiveToolsLoading) {
      readyDetail = "Waiting for tool pack metadata sync.";
    }
    rows.push({
      key: "startup_ready",
      label: "Working state",
      state: readyState,
      detail: readyDetail
    });

    var summaryActiveKey = "startup_ready";
    for (var a = 0; a < rows.length; a++) {
      if (rows[a] && rows[a].state === "active") {
        summaryActiveKey = normalizeStartupPhaseKey(rows[a].key);
        break;
      }
    }
    if (summaryActiveKey === "startup_ready") {
      for (var p = 0; p < rows.length; p++) {
        if (rows[p] && rows[p].state === "pending") {
          summaryActiveKey = normalizeStartupPhaseKey(rows[p].key);
          break;
        }
      }
    }
    var summaryProgress = buildStartupProgressSnapshot(rows, summaryActiveKey, rawStatus, 0, loginInProgress);
    var summaryProgressPercent = Math.max(0, Math.min(100, Math.round(summaryProgress.progressPercent || 0)));

    var summary = [];
    summary.push("Overall progress: " + summaryProgressPercent + "%.");
    if (readyDone) {
      summary.push("Startup is ready.");
    } else {
      for (var r = 0; r < rows.length; r++) {
        if (rows[r].state === "active") {
          summary.push("Current phase: " + rows[r].label.toLowerCase() + ".");
          break;
        }
      }
      if (summary.length === 0) {
        summary.push("Startup is still pending.");
      }
    }
    summary.push("Phase states: "
      + rows.map(function(row) { return row.label + " " + startupPhaseStateLabel(row.state); }).join(", ")
      + ".");

    return {
      rows: rows,
      summary: summary.join(" "),
      progressPercent: summaryProgressPercent,
      activeKey: summaryActiveKey
    };
  }

  function resolveHeaderStatusChipFromStructuredStartupContext() {
    var context = parseStartupStatusContext(state.status);
    if (!context) {
      return null;
    }

    var rawStatus = String(state.status || "");
    if (isStaleStartupStatusContextCandidate(
      rawStatus,
      context,
      normalizeBool(state.connected),
      normalizeBool(state.options && state.options.toolsLoading),
      normalizeBool(state.loginInProgress))) {
      return null;
    }

    var connected = normalizeBool(state.connected);
    var requiresInteractiveSignIn = requiresInteractiveSignInForStartup();
    var authenticated = normalizeBool(state.authenticated);
    var loginInProgress = normalizeBool(state.loginInProgress);
    var hasExplicitUnauthenticatedProbeSnapshot = normalizeBool(state.hasExplicitUnauthenticatedProbeSnapshot);
    var authGateActive = isStartupAuthGateActiveFromDiagnostics();
    var signInWaitRelevant = isStartupSignInWaitStillRelevant(
      rawStatus,
      connected,
      requiresInteractiveSignIn,
      authenticated,
      loginInProgress,
      authGateActive);
    var authVerificationPending = isStartupAuthVerificationStillRelevant(
      rawStatus,
      connected,
      requiresInteractiveSignIn,
      authenticated,
      loginInProgress,
      authGateActive);

    if (context.phase === "startup_auth_wait") {
      if (authVerificationPending) {
        return { text: "Checking sign-in", tone: "warn" };
      }

      if (!authGateActive && !loginInProgress && !signInWaitRelevant) {
        return null;
      }

      if (!hasExplicitUnauthenticatedProbeSnapshot && !loginInProgress && !authGateActive) {
        return { text: "Checking sign-in", tone: "warn" };
      }

      return { text: "Sign in required", tone: "warn" };
    }

    if (context.phase === "startup_metadata_sync") {
      // Some startup paths still emit metadata-sync phase while authentication is pending.
      // Show the explicit sign-in action instead of a misleading generic loading state.
      if (signInWaitRelevant) {
        return { text: "Sign in required", tone: "warn" };
      }

      if (context.cause === "metadata_retry") {
        return { text: "Loading tools", tone: "warn" };
      }

      return { text: "Loading tools", tone: "warn" };
    }

    if (context.phase === "startup_connect") {
      return { text: "Starting runtime", tone: "warn" };
    }

    return null;
  }

  function resolveHeaderStatusChipFallbackStatus() {
    var startupProgressStatus = resolveHeaderStartupProgressStatus(String(state.status || ""));
    if (startupProgressStatus) {
      return startupProgressStatus;
    }

    var structuredStartupStatus = resolveHeaderStatusChipFromStructuredStartupContext();
    if (structuredStartupStatus) {
      return structuredStartupStatus;
    }

    if (normalizeBool(state.connected)) {
      var rawStatus = String(state.status || "").toLowerCase();
      if (rawStatus.indexOf("tool metadata sync is degraded") >= 0) {
        return { text: "Ready", tone: "warn" };
      }
      if (rawStatus.indexOf("retrying tool metadata sync in background") >= 0) {
        return { text: "Ready", tone: "warn" };
      }

      if (normalizeBool(state.authenticated)) {
        return { text: "Ready", tone: "ok" };
      }
      if (normalizeBool(state.hasExplicitUnauthenticatedProbeSnapshot) || normalizeBool(state.loginInProgress)) {
        return { text: "Sign in required", tone: "warn" };
      }
      return { text: "Ready", tone: "ok" };
    }

    return { text: "Starting runtime", tone: "warn" };
  }

  function buildRoutingStatusChipModel() {
    var policy = (state.options && state.options.policy) || null;
    var routingCatalog = normalizeRoutingCatalog(policy && policy.routingCatalog
      ? policy.routingCatalog
      : (state.options ? state.options.toolCatalogRoutingCatalog : null));
    if (!routingCatalog) {
      return {
        visible: false,
        text: "Routing catalog unavailable",
        tone: "warn",
        title: "Routing catalog diagnostics are not available for this session."
      };
    }

    var issueCount = computeRoutingCatalogIssueCount(routingCatalog);
    var explicitReadinessIssueCount = computeExplicitRoutingReadinessIssueCount(routingCatalog);

    var headerText = routingCatalog.isHealthy
      ? (routingCatalog.isExplicitRoutingReady
        ? "Routing healthy"
        : "Routing healthy (explicit pending)")
      : ("Routing issues: " + String(issueCount));

    var titleLines = [];
    titleLines.push("Routing catalog: " + (routingCatalog.isHealthy ? "healthy" : "degraded"));
    titleLines.push("Explicit-ready: " + (routingCatalog.isExplicitRoutingReady
      ? "yes"
      : ("no (" + explicitReadinessIssueCount + " blocker" + (explicitReadinessIssueCount === 1 ? "" : "s") + ")")));
    titleLines.push("Routing-aware tools: " + routingCatalog.routingAwareTools + "/" + routingCatalog.totalTools);
    titleLines.push("Routing source: explicit " + routingCatalog.explicitRoutingTools + ", inferred " + routingCatalog.inferredRoutingTools);
    titleLines.push("Contract-aware tools: setup " + routingCatalog.setupAwareTools + ", handoff " + routingCatalog.handoffAwareTools + ", recovery " + routingCatalog.recoveryAwareTools);
    titleLines.push("Autonomy surface: remote-capable " + routingCatalog.remoteCapableTools + ", cross-pack handoffs " + routingCatalog.crossPackHandoffTools);
    if (routingCatalog.autonomyReadinessHighlights.length > 0) {
      titleLines.push("Autonomy readiness:");
      for (var j = 0; j < routingCatalog.autonomyReadinessHighlights.length; j++) {
        titleLines.push("- " + routingCatalog.autonomyReadinessHighlights[j]);
      }
    }
    titleLines.push("Domain-family tools: " + routingCatalog.domainFamilyTools);
    if (routingCatalog.missingRoutingContractTools > 0) {
      titleLines.push("Missing contracts: " + routingCatalog.missingRoutingContractTools);
    }
    if (routingCatalog.missingPackIdTools > 0) {
      titleLines.push("Missing pack id: " + routingCatalog.missingPackIdTools);
    }
    if (routingCatalog.missingRoleTools > 0) {
      titleLines.push("Missing role: " + routingCatalog.missingRoleTools);
    }
    if (routingCatalog.expectedDomainFamilyMissingTools > 0) {
      titleLines.push("Expected family missing: " + routingCatalog.expectedDomainFamilyMissingTools);
    }
    if (routingCatalog.domainFamilyMissingActionTools > 0) {
      titleLines.push("Family missing action: " + routingCatalog.domainFamilyMissingActionTools);
    }
    if (routingCatalog.actionWithoutFamilyTools > 0) {
      titleLines.push("Action without family: " + routingCatalog.actionWithoutFamilyTools);
    }
    if (routingCatalog.familyActionConflictFamilies > 0) {
      titleLines.push("Family action conflicts: " + routingCatalog.familyActionConflictFamilies);
    }
    if (routingCatalog.familyActions.length > 0) {
      titleLines.push("Families:");
      for (var i = 0; i < routingCatalog.familyActions.length; i++) {
        var familyAction = routingCatalog.familyActions[i];
        titleLines.push("- " + familyAction.family + " -> " + familyAction.actionId + " (" + familyAction.toolCount + ")");
      }
    }

    return {
      visible: true,
      text: headerText,
      tone: !routingCatalog.isHealthy ? "bad" : (routingCatalog.isExplicitRoutingReady ? "ok" : "warn"),
      title: titleLines.join("\n")
    };
  }

  function updateRoutingStatusVisual() {
    var routingEl = byId("routingStatus");
    if (!routingEl) {
      return;
    }

    var model = buildRoutingStatusChipModel();
    routingEl.classList.remove("ok", "warn", "bad");
    if (!model.visible) {
      routingEl.hidden = true;
      routingEl.textContent = "";
      routingEl.title = "";
      return;
    }

    routingEl.hidden = false;
    routingEl.textContent = model.text;
    routingEl.title = model.title || "";
    if (model.tone === "ok") {
      routingEl.classList.add("ok");
    } else if (model.tone === "bad") {
      routingEl.classList.add("bad");
    } else {
      routingEl.classList.add("warn");
    }
  }

  var STATUS_TIMELINE_MAX_ENTRIES = 16;

  function normalizeStatusTimelineEntry(value) {
    var normalized = String(value || "").trim();
    if (!normalized) {
      return "";
    }
    normalized = normalized.replace(/\s+/g, " ");
    if (normalized.length > 96) {
      normalized = normalized.slice(0, 93).trimEnd() + "...";
    }
    return normalized;
  }

  function appendStatusTimelineEntry(value) {
    var entry = normalizeStatusTimelineEntry(value);
    if (!entry) {
      return;
    }

    if (!Array.isArray(state.statusTimeline)) {
      state.statusTimeline = [];
    }

    if (state.statusTimeline.length > 0 && state.statusTimeline[state.statusTimeline.length - 1] === entry) {
      return;
    }

    state.statusTimeline.push(entry);
    while (state.statusTimeline.length > STATUS_TIMELINE_MAX_ENTRIES) {
      state.statusTimeline.shift();
    }
  }

  function buildStatusChipTitle(displayValue, rawValue, startupHeaderStatus, runtimeSummary) {
    var lines = [];
    var normalizedDisplay = normalizeStatusTimelineEntry(displayValue);
    var normalizedRaw = normalizeStatusTimelineEntry(rawValue);
    if (normalizedDisplay) {
      lines.push("Status: " + normalizedDisplay);
    }
    if (normalizedRaw && normalizedRaw !== normalizedDisplay) {
      lines.push("Detail: " + normalizedRaw);
    }
    var normalizedRuntimeSummary = normalizeStatusTimelineEntry(runtimeSummary);
    if (normalizedRuntimeSummary) {
      lines.push("Runtime: " + normalizedRuntimeSummary);
    }
    if (Array.isArray(state.statusTimeline) && state.statusTimeline.length > 0) {
      lines.push("Runtime lifecycle: " + state.statusTimeline.join(" > "));
    }
    if (startupHeaderStatus && typeof startupHeaderStatus === "object") {
      var startupSummary = String(startupHeaderStatus.startupSummary || "").trim();
      if (startupSummary) {
        lines.push("Startup: " + startupSummary);
      }
      var startupStageLabel = String(startupHeaderStatus.startupStageLabel || "").trim();
      var startupElapsedLabel = String(startupHeaderStatus.startupElapsedLabel || "").trim();
      if (startupStageLabel && startupElapsedLabel) {
        lines.push("Active stage: " + startupStageLabel + " (" + startupElapsedLabel + ")");
      }
      var startupProgressPercent = Number(startupHeaderStatus.progressPercent);
      if (Number.isFinite(startupProgressPercent) && startupProgressPercent >= 0) {
        var roundedProgressPercent = Math.max(0, Math.min(100, Math.round(startupProgressPercent)));
        var progressStageLabel = String(startupHeaderStatus.progressStageLabel || "").trim();
        var progressStagePercent = Number(startupHeaderStatus.progressStagePercent);
        if (progressStageLabel && Number.isFinite(progressStagePercent) && progressStagePercent >= 0) {
          lines.push("Progress: " + roundedProgressPercent + "% (" + progressStageLabel + " " + Math.max(0, Math.min(100, Math.round(progressStagePercent))) + "%)");
        } else {
          lines.push("Progress: " + roundedProgressPercent + "%");
        }
      }
    }
    appendStatusChipBackgroundDetailLines(lines, normalizedRaw, startupHeaderStatus);
    return lines.join("\n");
  }

  function appendStatusChipBackgroundDetailLines(lines, normalizedRaw, startupHeaderStatus) {
    if (!Array.isArray(lines)) {
      return;
    }

    var raw = String(normalizedRaw || "").trim().toLowerCase();
    var startupSummary = startupHeaderStatus && typeof startupHeaderStatus === "object"
      ? String(startupHeaderStatus.startupSummary || "").trim().toLowerCase()
      : "";

    if (raw.indexOf("tool metadata sync is degraded") >= 0) {
      lines.push("Background: tool metadata sync is degraded.");
      return;
    }

    if (raw.indexOf("retrying tool metadata sync in background") >= 0) {
      lines.push("Background: retrying tool metadata sync in background.");
      return;
    }

    if (raw.indexOf("loading tool packs in background") >= 0
      || startupSummary.indexOf("syncing in background") >= 0) {
      lines.push("Background: tool pack metadata is still syncing.");
    }
  }

  function applyStatusChipStartupProgress(statusEl, startupHeaderStatus) {
    if (!statusEl) {
      return;
    }

    var percent = Number(startupHeaderStatus && startupHeaderStatus.progressPercent);
    if (!Number.isFinite(percent) || percent < 0) {
      statusEl.classList.remove("status-chip-progress");
      statusEl.style.removeProperty("--ix-startup-progress");
      statusEl.removeAttribute("data-startup-progress");
      return;
    }

    var clampedPercent = Math.max(0, Math.min(100, Math.round(percent)));
    statusEl.classList.add("status-chip-progress");
    statusEl.style.setProperty("--ix-startup-progress", clampedPercent + "%");
    statusEl.setAttribute("data-startup-progress", String(clampedPercent));
  }

  function updateStatusVisual(text, tone) {
    var statusEl = byId("status");
    var rawValue = String(text || "").trim();
    var value = rawValue;
    var lowerRawValue = rawValue.toLowerCase();
    var startupContext = parseStartupStatusContext(rawValue);
    var normalizedTone = "";
    if (typeof tone === "string") {
      normalizedTone = tone.trim().toLowerCase();
    }
    if (normalizeBool(state.connected)
      && !normalizeBool(state.authenticated)
      && normalizeBool(state.options && state.options.toolsLoading)
      && (isStartupSignInWaitStatusText(rawValue)
        || (startupContext && startupContext.phase === "startup_auth_wait"))
      && (isStartupAuthGateActiveFromDiagnostics() || normalizeBool(state.loginInProgress))
      && lowerRawValue.indexOf("sign in to continue") === 0
      && lowerRawValue.indexOf("loading tool packs") < 0) {
      value = "Sign in to continue loading tool packs";
      if (!normalizedTone) {
        normalizedTone = "warn";
      }
    }
    var startupHeaderStatus = resolveHeaderStartupProgressStatus(rawValue);
    if (startupHeaderStatus) {
      value = startupHeaderStatus.text;
      normalizedTone = startupHeaderStatus.tone;
    }
    if (!shouldRenderHeaderStatusChip(value)) {
      var fallbackStatus = resolveHeaderStatusChipFallbackStatus();
      value = fallbackStatus.text;
      normalizedTone = fallbackStatus.tone;
      startupHeaderStatus = fallbackStatus;
    }
    appendStatusTimelineEntry(rawValue || value);
    var displayValue = value;
    var runtimeSummary = "";
    if (normalizedTone === "ok" || normalizedTone.length === 0) {
      var lowerForSummary = value.toLowerCase();
      if (lowerForSummary.indexOf("ready") >= 0 || lowerForSummary.indexOf("connected") >= 0) {
        runtimeSummary = resolveStatusRuntimeSummary();
      }
    }

    statusEl.textContent = displayValue;
    statusEl.title = buildStatusChipTitle(displayValue, rawValue, startupHeaderStatus, runtimeSummary);
    applyStatusChipStartupProgress(statusEl, startupHeaderStatus);
    var lower = displayValue.toLowerCase();
    statusEl.classList.remove("ok", "warn", "bad");
    if (normalizedTone === "ok") {
      statusEl.classList.add("ok");
    } else if (normalizedTone === "warn") {
      statusEl.classList.add("warn");
    } else if (normalizedTone === "bad") {
      statusEl.classList.add("bad");
    } else if (lower.indexOf("failed") >= 0 || lower.indexOf("error") >= 0 || lower.indexOf("limit") >= 0 || lower.indexOf("quota") >= 0 || lower.indexOf("unavailable") >= 0) {
      statusEl.classList.add("bad");
    } else if (lower.indexOf("connected") >= 0 || lower.indexOf("ready") >= 0) {
      statusEl.classList.add("ok");
    } else if (lower.indexOf("sign") >= 0 || lower.indexOf("wait") >= 0 || lower.indexOf("open") >= 0 || lower.indexOf("start") >= 0) {
      statusEl.classList.add("warn");
    } else {
      statusEl.classList.add("warn");
    }

    updateRoutingStatusVisual();
  }

  function updateWindowControlsState() {
    var maximizeButton = byId("btnWinMax");
    if (!maximizeButton) {
      return;
    }

    var isMaximized = normalizeBool(state.windowMaximized);
    maximizeButton.setAttribute("aria-label", isMaximized ? "Restore" : "Maximize");
    maximizeButton.title = isMaximized ? "Restore" : "Maximize";

    if (isMaximized) {
      maximizeButton.innerHTML = "<svg width='12' height='12' viewBox='0 0 12 12' fill='none'><rect x='1.8' y='3.2' width='6.8' height='6.8' rx='1' stroke='currentColor' stroke-width='1.1'/><path d='M4 2h6v6' stroke='currentColor' stroke-width='1.1' stroke-linecap='round' stroke-linejoin='round'/></svg>";
      return;
    }

    maximizeButton.innerHTML = "<svg width='12' height='12' viewBox='0 0 12 12' fill='none'><rect x='2' y='2' width='8' height='8' rx='1' fill='none' stroke='currentColor' stroke-width='1.2'/></svg>";
  }

  function updateMenuState() {
    var signIn = byId("menuSignIn");
    var switchAccount = byId("menuSwitchAccount");
    var reconnect = byId("menuReconnect");
    var debug = byId("menuToggleDebug");
    var transcriptForensics = byId("menuExportTranscriptForensics");
    var wheelDiagnostics = byId("menuWheelDiagnostics");
    var debugDivider = byId("menuDebugDivider");
    var authenticated = normalizeBool(state.authenticated);
    var loginInProgress = normalizeBool(state.loginInProgress);
    var queuedPromptCount = Number(state.queuedPromptCount);
    if (!Number.isFinite(queuedPromptCount) || queuedPromptCount < 0) {
      queuedPromptCount = 0;
    } else {
      queuedPromptCount = Math.floor(queuedPromptCount);
    }
    var hasQueuedPrompts = queuedPromptCount > 0 || normalizeBool(state.queuedPromptPending);
    var switchRecommended = normalizeBool(state.usageLimitSwitchRecommended) || hasQueuedPrompts;
    var debugToolsEnabled = normalizeBool(state.options.debugToolsEnabled);
    var transport = (state.options && state.options.localModel && state.options.localModel.transport) || "native";
    var isNativeTransport = String(transport).trim().toLowerCase() === "native";

    signIn.hidden = !isNativeTransport;
    signIn.disabled = loginInProgress;
    if (authenticated) {
      signIn.textContent = loginInProgress ? "Signing In..." : "Refresh Account";
      signIn.setAttribute("data-cmd", "relogin");
    } else {
      signIn.textContent = loginInProgress
        ? "Signing In..."
        : (hasQueuedPrompts
          ? (queuedPromptCount > 0 ? ("Sign In (" + queuedPromptCount + " queued)") : "Sign In (retry queued prompt)")
          : "Sign In");
      signIn.setAttribute("data-cmd", "login");
    }

    if (switchAccount) {
      switchAccount.hidden = !isNativeTransport;
      switchAccount.disabled = loginInProgress;
      switchAccount.textContent = switchRecommended
        ? (queuedPromptCount > 0 ? ("Switch Account (" + queuedPromptCount + " queued)") : "Switch Account (Recommended)")
        : "Switch Account";
    }

    reconnect.textContent = normalizeBool(state.connected) ? "Reconnect session" : "Start runtime";
    if (debug) {
      debug.hidden = !debugToolsEnabled;
      debug.textContent = normalizeBool(state.debugMode) ? "Disable Debug" : "Enable Debug";
    }
    if (transcriptForensics) {
      transcriptForensics.hidden = !debugToolsEnabled;
    }
    if (wheelDiagnostics) {
      wheelDiagnostics.hidden = !debugToolsEnabled;
    }
    if (debugDivider) {
      debugDivider.hidden = !debugToolsEnabled;
    }
  }

  function updateComposerState() {
    var send = byId("btnSend");
    var sending = normalizeBool(state.sending);
    var loginBusy = normalizeBool(state.loginInProgress);
    var cancelable = normalizeBool(state.cancelable);
    var cancelRequested = normalizeBool(state.cancelRequested);
    var promptText = (promptEl.value || "").trim();
    var hasPromptText = promptText.length > 0;
    var queueingWhileRunning = cancelable && hasPromptText;
    var queueingForLogin = loginBusy && hasPromptText;
    var queuedPromptCount = Number(state.queuedPromptCount);
    if (!Number.isFinite(queuedPromptCount) || queuedPromptCount < 0) {
      queuedPromptCount = 0;
    } else {
      queuedPromptCount = Math.floor(queuedPromptCount);
    }
    var busy = sending || loginBusy;

    send.disabled = (loginBusy && !hasPromptText) || (sending && !cancelable) || (cancelable && cancelRequested && !hasPromptText);
    send.classList.toggle("cancel-mode", cancelable && !hasPromptText);

    if (cancelable && !hasPromptText) {
      send.setAttribute("aria-label", cancelRequested ? "Canceling turn" : "Stop turn");
      send.title = cancelRequested ? "Canceling..." : "Stop";
      send.innerHTML = "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2.5' stroke-linecap='round' stroke-linejoin='round'><rect x='7' y='7' width='10' height='10' rx='2'/></svg>";
    } else {
      send.setAttribute("aria-label", "Send");
      send.title = queueingForLogin ? "Queue for sign-in" : (queueingWhileRunning ? "Queue next turn" : "Send");
      send.innerHTML = "<svg width='18' height='18' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2.5' stroke-linecap='round' stroke-linejoin='round'><line x1='12' y1='19' x2='12' y2='5'/><polyline points='5 12 12 5 19 12'/></svg>";
    }

    promptEl.setAttribute("aria-busy", busy ? "true" : "false");
    if (!busy) {
      promptEl.placeholder = "Ask IntelligenceX...";
      return;
    }

    if (state.queuedTurnCount > 0) {
      promptEl.placeholder = "Working... " + state.queuedTurnCount + " queued";
      return;
    }

    if (loginBusy && queuedPromptCount > 0) {
      promptEl.placeholder = "Waiting for sign-in... " + queuedPromptCount + " queued";
      return;
    }

    promptEl.placeholder = cancelable
      ? "Working... type to queue the next turn"
      : "IntelligenceX is working...";
  }

  function renderPolicy() {
    var policyEl = byId("policyInfo");
    var pluginDetailsEl = byId("policyPluginDetails");
    var startupWarningsEl = byId("policyStartupWarnings");
    var pluginsEl = byId("policyPlugins");
    var pluginRootsEl = byId("policyPluginRoots");
    var routingCatalogEl = byId("policyRoutingCatalog");
    var capabilitySnapshotEl = byId("policyCapabilitySnapshot");
    policyEl.innerHTML = "";

    var p = state.options.policy;
    var fallbackRoutingCatalog = normalizeRoutingCatalog(state.options ? state.options.toolCatalogRoutingCatalog : null);
    var fallbackCapabilitySnapshot = normalizeCapabilitySnapshot(state.options ? state.options.toolCatalogCapabilitySnapshot : null);
    var routingPromptExposure = normalizeRoutingPromptExposure(state.options ? state.options.latestRoutingPromptExposure : null);
    var fallbackPlugins = normalizePlugins(state.options ? state.options.toolCatalogPlugins : null);
    if (fallbackPlugins.length === 0) {
      fallbackPlugins = resolveCapabilitySnapshotPlugins(fallbackCapabilitySnapshot);
    }
    if (!p) {
      var usingToolCatalogPreview = !!fallbackRoutingCatalog || !!fallbackCapabilitySnapshot || fallbackPlugins.length > 0;
      policyEl.innerHTML = usingToolCatalogPreview
        ? "<span class='options-k'>Policy</span><span class='options-v'>Not available</span><span class='options-k'>Bootstrap preview</span><span class='options-v'>Using tool catalog</span>"
        : "<span class='options-k'>Policy</span><span class='options-v'>Not available</span>";
      if (routingPromptExposure) {
        appendOptionsKv(policyEl, "Prompt exposure", routingPromptExposure.strategy + " (" + routingPromptExposure.selectedToolCount + "/" + routingPromptExposure.totalToolCount + ")");
      }
      if (startupWarningsEl) {
        startupWarningsEl.hidden = true;
        startupWarningsEl.innerHTML = "";
      }
      renderPluginPolicyDetails(pluginDetailsEl, fallbackPlugins);
      if (pluginsEl) {
        renderPolicyList(pluginsEl, formatPluginPolicyLines(fallbackPlugins), "Plugin sources");
      }
      if (pluginRootsEl) {
        pluginRootsEl.hidden = true;
        pluginRootsEl.innerHTML = "";
      }
      if (routingCatalogEl) {
        renderRoutingCatalogPolicy(routingCatalogEl, fallbackRoutingCatalog, routingPromptExposure);
      }
      if (capabilitySnapshotEl) {
        renderCapabilitySnapshotPolicy(capabilitySnapshotEl, fallbackCapabilitySnapshot);
      }
      return;
    }

    var startupWarnings = toStringArray(p.startupWarnings);
    var startupBootstrap = normalizeStartupBootstrap(p.startupBootstrap);
    var pluginSearchPaths = toStringArray(p.pluginSearchPaths);
    var runtimePolicy = normalizeRuntimePolicy(p.runtimePolicy);
    var routingCatalog = normalizeRoutingCatalog(p && p.routingCatalog ? p.routingCatalog : fallbackRoutingCatalog);
    var capabilitySnapshot = normalizeCapabilitySnapshot(p && p.capabilitySnapshot ? p.capabilitySnapshot : fallbackCapabilitySnapshot);
    var plugins = normalizePlugins(p && p.plugins ? p.plugins : null);
    if (plugins.length === 0) {
      plugins = resolveCapabilitySnapshotPlugins(capabilitySnapshot);
    }
    if (plugins.length === 0) {
      plugins = fallbackPlugins;
    }
    var runtimeNotices = routingCatalog
      ? filterOutRoutingCatalogWarnings(startupWarnings)
      : startupWarnings;
    var routingIssueCount = routingCatalog ? computeRoutingCatalogIssueCount(routingCatalog) : 0;
    var explicitReadinessIssueCount = routingCatalog ? computeExplicitRoutingReadinessIssueCount(routingCatalog) : 0;
    var capabilityAutonomy = capabilitySnapshot && capabilitySnapshot.autonomy ? capabilitySnapshot.autonomy : null;
    var rows = [
      ["Read-only", p.readOnly ? "Yes" : "No"],
      ["Parallel tools", p.parallelTools ? "Yes" : "No"],
      ["Mutating parallel", p.allowMutatingParallelToolCalls ? "Yes" : "No"],
      ["Max tool rounds", p.maxToolRounds == null ? "Default" : String(p.maxToolRounds)],
      ["Turn timeout", p.turnTimeoutSeconds == null ? "Default" : (String(p.turnTimeoutSeconds) + "s")],
      ["Tool timeout", p.toolTimeoutSeconds == null ? "Default" : (String(p.toolTimeoutSeconds) + "s")],
      ["Runtime policy", !runtimePolicy ? "Not available" : "Available"],
      ["Explicit routing required", !runtimePolicy ? "N/A" : (runtimePolicy.requireExplicitRoutingMetadata ? "Yes" : "No")],
      ["Routing catalog", !routingCatalog ? "Not available" : (routingCatalog.isHealthy ? "Healthy" : ("Degraded (" + routingIssueCount + ")"))],
      ["Explicit routing ready", !routingCatalog
        ? "N/A"
        : (routingCatalog.isExplicitRoutingReady ? "Yes" : ("No (" + explicitReadinessIssueCount + ")"))],
      ["Capability snapshot", !capabilitySnapshot ? "Not available" : "Available"],
      ["Tooling available", !capabilitySnapshot ? "N/A" : (capabilitySnapshot.toolingAvailable ? "Yes" : "No")],
      ["Registered tools", !capabilitySnapshot ? "N/A" : String(capabilitySnapshot.registeredTools)],
      ["Enabled packs", !capabilitySnapshot ? "N/A" : String(capabilitySnapshot.enabledPackCount)],
      ["Plugin sources", plugins.length === 0 ? "None" : String(plugins.length)],
      ["Remote reachability", !capabilitySnapshot ? "N/A" : capabilitySnapshot.remoteReachabilityMode],
      ["Autonomy surface", !capabilityAutonomy
        ? "N/A"
        : ("remote-capable " + capabilityAutonomy.remoteCapableToolCount + ", cross-pack " + capabilityAutonomy.crossPackHandoffToolCount)],
      ["Routing families", !routingCatalog ? "N/A" : String(routingCatalog.familyActions.length)],
      ["Prompt exposure", !routingPromptExposure ? "Not available" : (routingPromptExposure.strategy + " (" + routingPromptExposure.selectedToolCount + "/" + routingPromptExposure.totalToolCount + ")")],
      ["Plugin roots", pluginSearchPaths.length === 0 ? "None" : String(pluginSearchPaths.length)],
      ["Runtime notices", runtimeNotices.length === 0 ? "None" : String(runtimeNotices.length)],
      ["Tool bootstrap", !startupBootstrap ? "N/A" : formatStartupBootstrapSummary(startupBootstrap)]
    ];

    for (var i = 0; i < rows.length; i++) {
      var k = document.createElement("span");
      k.className = "options-k";
      k.textContent = rows[i][0];
      var v = document.createElement("span");
      v.className = "options-v";
      v.textContent = rows[i][1];
      policyEl.appendChild(k);
      policyEl.appendChild(v);
    }

    renderPluginPolicyDetails(pluginDetailsEl, plugins);
    renderRoutingCatalogPolicy(routingCatalogEl, routingCatalog, routingPromptExposure);
    renderCapabilitySnapshotPolicy(capabilitySnapshotEl, capabilitySnapshot);
    renderPolicyList(pluginsEl, formatPluginPolicyLines(plugins), "Plugin sources");
    renderPolicyList(pluginRootsEl, pluginSearchPaths, "Plugin search roots");
    renderPolicyList(startupWarningsEl, runtimeNotices, runtimeNotices.length === 1 ? "Runtime notice" : "Runtime notices");
  }

  function toNonNegativeInt(value) {
    var number = Number(value);
    if (!Number.isFinite(number) || number < 0) {
      return 0;
    }
    return Math.floor(number);
  }

  function normalizeOptionalPath(value) {
    if (typeof value !== "string") {
      return "";
    }

    var normalized = value.trim();
    return normalized;
  }

  function normalizeModeToken(value, fallback) {
    var normalized = typeof value === "string" ? value.trim().toLowerCase() : "";
    return normalized || fallback;
  }

  function normalizeRuntimePolicy(value) {
    if (!value || typeof value !== "object") {
      return null;
    }

    return {
      writeGovernanceMode: normalizeModeToken(value.writeGovernanceMode, "enforced"),
      requireWriteGovernanceRuntime: value.requireWriteGovernanceRuntime === true,
      writeGovernanceRuntimeConfigured: value.writeGovernanceRuntimeConfigured === true,
      requireWriteAuditSinkForWriteOperations: value.requireWriteAuditSinkForWriteOperations === true,
      writeAuditSinkMode: normalizeModeToken(value.writeAuditSinkMode, "none"),
      writeAuditSinkConfigured: value.writeAuditSinkConfigured === true,
      writeAuditSinkPath: normalizeOptionalPath(value.writeAuditSinkPath),
      authenticationRuntimePreset: normalizeModeToken(value.authenticationRuntimePreset, "default"),
      requireExplicitRoutingMetadata: value.requireExplicitRoutingMetadata === true,
      requireAuthenticationRuntime: value.requireAuthenticationRuntime === true,
      authenticationRuntimeConfigured: value.authenticationRuntimeConfigured === true,
      requireSuccessfulSmtpProbeForSend: value.requireSuccessfulSmtpProbeForSend === true,
      smtpProbeMaxAgeSeconds: toNonNegativeInt(value.smtpProbeMaxAgeSeconds),
      runAsProfilePath: normalizeOptionalPath(value.runAsProfilePath),
      authenticationProfilePath: normalizeOptionalPath(value.authenticationProfilePath)
    };
  }

  function normalizeStartupBootstrap(value) {
    if (!value || typeof value !== "object") {
      return null;
    }

    var phases = Array.isArray(value.phases) ? value.phases.map(function (phase) {
      return {
        id: typeof phase?.id === "string" ? phase.id.trim() : "",
        label: typeof phase?.label === "string" ? phase.label.trim() : "",
        durationMs: toNonNegativeInt(phase?.durationMs),
        order: toNonNegativeInt(phase?.order)
      };
    }) : [];
    var descriptorDiscoveryMs = toNonNegativeInt(value.descriptorDiscoveryMs);
    if (descriptorDiscoveryMs <= 0) {
      descriptorDiscoveryMs = readStartupBootstrapPhaseMs(phases, "descriptor_discovery");
    }
    if (descriptorDiscoveryMs <= 0) {
      descriptorDiscoveryMs = toNonNegativeInt(value.packLoadMs);
    }
    var packActivationMs = toNonNegativeInt(value.packActivationMs);
    if (packActivationMs <= 0) {
      packActivationMs = readStartupBootstrapPhaseMs(phases, "pack_activation");
    }
    if (packActivationMs <= 0) {
      packActivationMs = toNonNegativeInt(value.packRegisterMs);
    }
    var registryActivationFinalizeMs = toNonNegativeInt(value.registryActivationFinalizeMs);
    if (registryActivationFinalizeMs <= 0) {
      registryActivationFinalizeMs = readStartupBootstrapPhaseMs(phases, "registry_activation_finalize");
    }
    if (registryActivationFinalizeMs <= 0) {
      registryActivationFinalizeMs = toNonNegativeInt(value.registryFinalizeMs);
    }

    return {
      totalMs: toNonNegativeInt(value.totalMs),
      runtimePolicyMs: toNonNegativeInt(value.runtimePolicyMs),
      bootstrapOptionsMs: toNonNegativeInt(value.bootstrapOptionsMs),
      packLoadMs: toNonNegativeInt(value.packLoadMs),
      packRegisterMs: toNonNegativeInt(value.packRegisterMs),
      registryFinalizeMs: toNonNegativeInt(value.registryFinalizeMs),
      registryMs: toNonNegativeInt(value.registryMs),
      descriptorDiscoveryMs: descriptorDiscoveryMs,
      packActivationMs: packActivationMs,
      registryActivationFinalizeMs: registryActivationFinalizeMs,
      tools: toNonNegativeInt(value.tools),
      packsLoaded: toNonNegativeInt(value.packsLoaded),
      packsDisabled: toNonNegativeInt(value.packsDisabled),
      pluginRoots: toNonNegativeInt(value.pluginRoots),
      slowPackCount: toNonNegativeInt(value.slowPackCount),
      slowPackTopCount: toNonNegativeInt(value.slowPackTopCount),
      packProgressProcessed: toNonNegativeInt(value.packProgressProcessed),
      packProgressTotal: toNonNegativeInt(value.packProgressTotal),
      phases: phases,
      slowPluginCount: toNonNegativeInt(value.slowPluginCount),
      slowPluginTopCount: toNonNegativeInt(value.slowPluginTopCount),
      pluginProgressProcessed: toNonNegativeInt(value.pluginProgressProcessed),
      pluginProgressTotal: toNonNegativeInt(value.pluginProgressTotal),
      slowestPhaseId: typeof value.slowestPhaseId === "string" ? value.slowestPhaseId.trim() : "",
      slowestPhaseLabel: typeof value.slowestPhaseLabel === "string" ? value.slowestPhaseLabel.trim() : "",
      slowestPhaseMs: toNonNegativeInt(value.slowestPhaseMs)
    };
  }

  function readStartupBootstrapPhaseMs(phases, phaseId) {
    if (!Array.isArray(phases) || !phaseId) {
      return 0;
    }

    for (var i = 0; i < phases.length; i += 1) {
      var phase = phases[i];
      if (phase && phase.id === phaseId) {
        return toNonNegativeInt(phase.durationMs);
      }
    }

    return 0;
  }

  function hasStartupBootstrapPhaseId(telemetry, phaseId) {
    if (!telemetry || !phaseId || !Array.isArray(telemetry.phases)) {
      return false;
    }

    for (var i = 0; i < telemetry.phases.length; i += 1) {
      var phase = telemetry.phases[i];
      if (phase && typeof phase.id === "string" && phase.id.trim().toLowerCase() === phaseId.toLowerCase()) {
        return true;
      }
    }

    return false;
  }

  function formatStartupBootstrapDuration(ms) {
    if (!Number.isFinite(ms) || ms <= 0) {
      return "0ms";
    }

    if (ms >= 1000) {
      return (ms / 1000).toFixed(1) + "s";
    }

    return String(ms) + "ms";
  }

  function formatStartupBootstrapSummary(telemetry) {
    if (!telemetry) {
      return "N/A";
    }

    var segments = [];
    var descriptorDiscoveryMs = toNonNegativeInt(telemetry.descriptorDiscoveryMs);
    var packActivationMs = toNonNegativeInt(telemetry.packActivationMs);
    var activationFinalizeMs = toNonNegativeInt(telemetry.registryActivationFinalizeMs);
    segments.push("total " + formatStartupBootstrapDuration(telemetry.totalMs));
    if (hasStartupBootstrapPhaseId(telemetry, "descriptor_cache_hit")) {
      segments.push("descriptor-preview");
    } else {
      segments.push("descriptor-discovery " + formatStartupBootstrapDuration(descriptorDiscoveryMs));
      segments.push("pack-activation " + formatStartupBootstrapDuration(packActivationMs));
      segments.push("activation-finalize " + formatStartupBootstrapDuration(activationFinalizeMs));
    }
    segments.push("registry " + formatStartupBootstrapDuration(telemetry.registryMs));
    segments.push("tools " + String(telemetry.tools));
    segments.push("enabled-packs " + String(telemetry.packsLoaded) + "/" + String(Math.max(telemetry.packsLoaded, telemetry.packsDisabled + telemetry.packsLoaded)));
    if (telemetry.packProgressTotal > 0 || telemetry.packProgressProcessed > 0) {
      segments.push("pack-steps " + String(telemetry.packProgressProcessed) + "/" + String(Math.max(telemetry.packProgressTotal, telemetry.packProgressProcessed)));
    }
    if (telemetry.slowPackCount > 0) {
      segments.push("slow-packs " + String(telemetry.slowPackCount));
    }
    if (telemetry.pluginProgressTotal > 0 || telemetry.pluginProgressProcessed > 0) {
      segments.push("plugins " + String(telemetry.pluginProgressProcessed) + "/" + String(Math.max(telemetry.pluginProgressTotal, telemetry.pluginProgressProcessed)));
    }
    if (telemetry.slowPluginCount > 0) {
      segments.push("slow " + String(telemetry.slowPluginCount));
    }

    return segments.join("; ");
  }

  function computeRoutingCatalogIssueCount(routingCatalog) {
    if (!routingCatalog) {
      return 0;
    }

    return routingCatalog.missingRoutingContractTools
      + routingCatalog.missingPackIdTools
      + routingCatalog.missingRoleTools
      + routingCatalog.expectedDomainFamilyMissingTools
      + routingCatalog.domainFamilyMissingActionTools
      + routingCatalog.actionWithoutFamilyTools
      + routingCatalog.familyActionConflictFamilies;
  }

  function computeExplicitRoutingReadinessIssueCount(routingCatalog) {
    if (!routingCatalog) {
      return 0;
    }

    return routingCatalog.missingRoutingContractTools
      + routingCatalog.missingPackIdTools
      + routingCatalog.missingRoleTools
      + routingCatalog.inferredRoutingTools;
  }

  function normalizeRoutingFamilyActions(value) {
    var familyActionsRaw = Array.isArray(value) ? value : [];
    var familyActions = [];
    for (var i = 0; i < familyActionsRaw.length; i++) {
      var item = familyActionsRaw[i];
      if (!item || typeof item !== "object") {
        continue;
      }

      var family = typeof item.family === "string" ? item.family.trim() : "";
      var actionId = typeof item.actionId === "string" ? item.actionId.trim() : "";
      if (!family || !actionId) {
        continue;
      }

      familyActions.push({
        family: family,
        actionId: actionId,
        toolCount: toNonNegativeInt(item.toolCount)
      });
    }

    return familyActions;
  }

  function normalizeRoutingCatalog(value) {
    if (!value || typeof value !== "object") {
      return null;
    }

    var hasOwn = Object.prototype.hasOwnProperty;
    var hasIsHealthy = hasOwn.call(value, "isHealthy");
    var hasIsExplicitRoutingReady = hasOwn.call(value, "isExplicitRoutingReady");
    var hasMissingRoutingContractTools = hasOwn.call(value, "missingRoutingContractTools");
    var hasMissingPackIdTools = hasOwn.call(value, "missingPackIdTools");
    var hasMissingRoleTools = hasOwn.call(value, "missingRoleTools");
    var hasInferredRoutingTools = hasOwn.call(value, "inferredRoutingTools");
    var hasExpectedDomainFamilyMissingTools = hasOwn.call(value, "expectedDomainFamilyMissingTools");
    var hasDomainFamilyMissingActionTools = hasOwn.call(value, "domainFamilyMissingActionTools");
    var hasActionWithoutFamilyTools = hasOwn.call(value, "actionWithoutFamilyTools");
    var hasFamilyActionConflictFamilies = hasOwn.call(value, "familyActionConflictFamilies");

    var autonomyReadinessHighlightsRaw = Array.isArray(value.autonomyReadinessHighlights) ? value.autonomyReadinessHighlights : [];
    var familyActions = normalizeRoutingFamilyActions(value.familyActions);
    var autonomyReadinessHighlights = [];
    for (var j = 0; j < autonomyReadinessHighlightsRaw.length; j++) {
      if (typeof autonomyReadinessHighlightsRaw[j] !== "string") {
        continue;
      }

      var readinessHighlight = autonomyReadinessHighlightsRaw[j].trim();
      if (!readinessHighlight) {
        continue;
      }

      autonomyReadinessHighlights.push(readinessHighlight);
    }

    var routingCatalog = {
      totalTools: toNonNegativeInt(value.totalTools),
      routingAwareTools: toNonNegativeInt(value.routingAwareTools),
      explicitRoutingTools: toNonNegativeInt(value.explicitRoutingTools),
      inferredRoutingTools: toNonNegativeInt(value.inferredRoutingTools),
      missingRoutingContractTools: toNonNegativeInt(value.missingRoutingContractTools),
      missingPackIdTools: toNonNegativeInt(value.missingPackIdTools),
      missingRoleTools: toNonNegativeInt(value.missingRoleTools),
      setupAwareTools: toNonNegativeInt(value.setupAwareTools),
      handoffAwareTools: toNonNegativeInt(value.handoffAwareTools),
      recoveryAwareTools: toNonNegativeInt(value.recoveryAwareTools),
      remoteCapableTools: toNonNegativeInt(value.remoteCapableTools),
      crossPackHandoffTools: toNonNegativeInt(value.crossPackHandoffTools),
      domainFamilyTools: toNonNegativeInt(value.domainFamilyTools),
      expectedDomainFamilyMissingTools: toNonNegativeInt(value.expectedDomainFamilyMissingTools),
      domainFamilyMissingActionTools: toNonNegativeInt(value.domainFamilyMissingActionTools),
      actionWithoutFamilyTools: toNonNegativeInt(value.actionWithoutFamilyTools),
      familyActionConflictFamilies: toNonNegativeInt(value.familyActionConflictFamilies),
      // Keep version-skew payloads neutral by default; derive degraded states only when required counters are present.
      isHealthy: hasIsHealthy ? value.isHealthy === true : true,
      isExplicitRoutingReady: hasIsExplicitRoutingReady ? value.isExplicitRoutingReady === true : true,
      familyActions: familyActions,
      autonomyReadinessHighlights: autonomyReadinessHighlights
    };

    var canDeriveHealth = hasMissingRoutingContractTools
      && hasMissingPackIdTools
      && hasMissingRoleTools
      && hasExpectedDomainFamilyMissingTools
      && hasDomainFamilyMissingActionTools
      && hasActionWithoutFamilyTools
      && hasFamilyActionConflictFamilies;
    var canDeriveExplicitReadiness = hasMissingRoutingContractTools
      && hasMissingPackIdTools
      && hasMissingRoleTools
      && hasInferredRoutingTools;
    var issueCount = computeRoutingCatalogIssueCount(routingCatalog);
    var explicitReadinessIssueCount = computeExplicitRoutingReadinessIssueCount(routingCatalog);
    if (canDeriveHealth && issueCount > 0) {
      routingCatalog.isHealthy = false;
    }
    if (canDeriveExplicitReadiness && explicitReadinessIssueCount > 0) {
      routingCatalog.isExplicitRoutingReady = false;
    }

    return routingCatalog;
  }

  function normalizeCapabilitySnapshot(value) {
    if (!value || typeof value !== "object") {
      return null;
    }

    var autonomyRaw = value.autonomy && typeof value.autonomy === "object"
      ? value.autonomy
      : null;
    var toolingSnapshotRaw = value.toolingSnapshot && typeof value.toolingSnapshot === "object"
      ? value.toolingSnapshot
      : null;

    return {
      registeredTools: toNonNegativeInt(value.registeredTools),
      enabledPackCount: toNonNegativeInt(value.enabledPackCount),
      pluginCount: toNonNegativeInt(value.pluginCount),
      enabledPluginCount: toNonNegativeInt(value.enabledPluginCount),
      toolingAvailable: value.toolingAvailable === true,
      allowedRootCount: toNonNegativeInt(value.allowedRootCount),
      enabledPackIds: toStringArray(value.enabledPackIds),
      enabledPluginIds: toStringArray(value.enabledPluginIds),
      routingFamilies: toStringArray(value.routingFamilies),
      familyActions: normalizeRoutingFamilyActions(value.familyActions),
      skills: toStringArray(value.skills),
      healthyTools: toStringArray(value.healthyTools),
      remoteReachabilityMode: normalizeModeToken(value.remoteReachabilityMode, "unknown"),
      autonomy: !autonomyRaw ? null : {
        remoteCapableToolCount: toNonNegativeInt(autonomyRaw.remoteCapableToolCount),
        setupAwareToolCount: toNonNegativeInt(autonomyRaw.setupAwareToolCount),
        handoffAwareToolCount: toNonNegativeInt(autonomyRaw.handoffAwareToolCount),
        recoveryAwareToolCount: toNonNegativeInt(autonomyRaw.recoveryAwareToolCount),
        crossPackHandoffToolCount: toNonNegativeInt(autonomyRaw.crossPackHandoffToolCount),
        remoteCapablePackIds: toStringArray(autonomyRaw.remoteCapablePackIds),
        crossPackReadyPackIds: toStringArray(autonomyRaw.crossPackReadyPackIds),
        crossPackTargetPackIds: toStringArray(autonomyRaw.crossPackTargetPackIds)
      },
      toolingSnapshot: !toolingSnapshotRaw ? null : {
        source: normalizeModeToken(toolingSnapshotRaw.source, ""),
        packs: Array.isArray(toolingSnapshotRaw.packs) ? toolingSnapshotRaw.packs : [],
        plugins: normalizePlugins(toolingSnapshotRaw.plugins)
      },
      parityAttentionCount: toNonNegativeInt(value.parityAttentionCount),
      parityMissingCapabilityCount: toNonNegativeInt(value.parityMissingCapabilityCount)
    };
  }

  function normalizeRoutingPromptExposure(value) {
    if (!value || typeof value !== "object") {
      return null;
    }

    var strategy = typeof value.strategy === "string" ? value.strategy.trim() : "";
    if (!strategy) {
      return null;
    }

    var selectedToolCount = toNonNegativeInt(value.selectedToolCount);
    var totalToolCount = toNonNegativeInt(value.totalToolCount);
    if (selectedToolCount > totalToolCount) {
      selectedToolCount = totalToolCount;
    }

    return {
      requestId: typeof value.requestId === "string" ? value.requestId.trim() : "",
      threadId: typeof value.threadId === "string" ? value.threadId.trim() : "",
      strategy: strategy,
      selectedToolCount: selectedToolCount,
      totalToolCount: totalToolCount,
      reordered: value.reordered === true,
      topToolNames: toStringArray(value.topToolNames)
    };
  }

  function resolveCapabilitySnapshotPlugins(capabilitySnapshot) {
    if (!capabilitySnapshot || !capabilitySnapshot.toolingSnapshot) {
      return [];
    }

    return normalizePlugins(capabilitySnapshot.toolingSnapshot.plugins);
  }

  function normalizePluginSourceKind(value) {
    var normalized = String(value || "").trim().toLowerCase();
    if (normalized === "builtin" || normalized === "closed_source" || normalized === "open_source") {
      return normalized;
    }
    return "open_source";
  }

  function normalizePluginOrigin(value) {
    if (typeof value !== "string") {
      return "";
    }

    return value.trim();
  }

  function normalizePluginInfo(value) {
    if (!value || typeof value !== "object") {
      return null;
    }

    var id = typeof value.id === "string" ? value.id.trim() : "";
    var name = typeof value.name === "string" ? value.name.trim() : "";
    if (!id && !name) {
      return null;
    }

    return {
      id: id,
      name: name,
      version: typeof value.version === "string" ? value.version.trim() : "",
      origin: normalizePluginOrigin(value.origin),
      sourceKind: normalizePluginSourceKind(value.sourceKind),
      defaultEnabled: normalizeBool(value.defaultEnabled),
      enabled: normalizeBool(value.enabled),
      disabledReason: typeof value.disabledReason === "string" ? value.disabledReason.trim() : "",
      isDangerous: normalizeBool(value.isDangerous),
      packIds: toStringArray(value.packIds),
      rootPath: normalizeOptionalPath(value.rootPath),
      skillIds: toStringArray(value.skillIds)
    };
  }

  function normalizePlugins(value) {
    if (!Array.isArray(value) || value.length === 0) {
      return [];
    }

    var plugins = [];
    for (var i = 0; i < value.length; i++) {
      var plugin = normalizePluginInfo(value[i]);
      if (!plugin) {
        continue;
      }

      plugins.push(plugin);
    }

    plugins.sort(function(a, b) {
      var byName = String(a.name || a.id || "").localeCompare(String(b.name || b.id || ""), undefined, { sensitivity: "accent" });
      if (byName !== 0) {
        return byName;
      }

      return String(a.id || "").localeCompare(String(b.id || ""), undefined, { sensitivity: "accent" });
    });
    return plugins;
  }

  function buildPluginPolicyLabel(plugin) {
    if (!plugin) {
      return "";
    }

    var pluginId = String(plugin.id || "").trim();
    var pluginName = String(plugin.name || "").trim();
    if (!pluginName) {
      return pluginId;
    }

    return pluginId && pluginName.toLowerCase() !== pluginId.toLowerCase()
      ? (pluginName + " [" + pluginId + "]")
      : pluginName;
  }

  function describePluginOrigin(origin, sourceKind) {
    var normalizedOrigin = normalizePluginOrigin(origin).toLowerCase();
    if (normalizedOrigin === "folder" || normalizedOrigin === "plugin_folder") {
      return "plugin folder";
    }
    if (normalizedOrigin === "builtin") {
      return "built-in runtime";
    }
    if (normalizedOrigin) {
      return normalizedOrigin.replace(/_/g, " ");
    }

    if (normalizePluginSourceKind(sourceKind) === "builtin") {
      return "built-in runtime";
    }
    if (normalizePluginSourceKind(sourceKind) === "closed_source") {
      return "private plugin source";
    }
    return "registered plugin source";
  }

  function formatPluginPolicyLines(plugins) {
    if (!Array.isArray(plugins) || plugins.length === 0) {
      return [];
    }

    var lines = [];
    for (var i = 0; i < plugins.length; i++) {
      var plugin = plugins[i];
      var details = [];
      details.push(plugin.enabled ? "enabled" : "disabled");
      details.push(plugin.defaultEnabled ? "default enabled" : "default disabled");
      details.push("origin " + describePluginOrigin(plugin.origin, plugin.sourceKind));
      details.push("source " + packSourceLabel(plugin.sourceKind));
      if (plugin.version) {
        details.push("version " + plugin.version);
      }
      if (plugin.isDangerous) {
        details.push("dangerous");
      }
      if (plugin.packIds.length > 0) {
        details.push("packs " + plugin.packIds.join("/"));
      }
      if (plugin.skillIds.length > 0) {
        details.push("skills " + plugin.skillIds.join("/"));
      }
      if (plugin.rootPath) {
        details.push("root " + plugin.rootPath);
      }
      if (!plugin.enabled && plugin.disabledReason) {
        details.push("reason " + plugin.disabledReason);
      }

      lines.push(buildPluginPolicyLabel(plugin) + ": " + details.join(", "));
    }

    return lines;
  }

  function appendOptionsKv(host, label, value) {
    if (!host) {
      return;
    }

    var k = document.createElement("span");
    k.className = "options-k";
    k.textContent = label;
    var v = document.createElement("span");
    v.className = "options-v";
    v.textContent = value;
    host.appendChild(k);
    host.appendChild(v);
  }

  function renderPluginPolicyDetails(host, plugins) {
    if (!host) {
      return;
    }

    host.innerHTML = "";
    if (!Array.isArray(plugins) || plugins.length === 0) {
      host.hidden = true;
      return;
    }

    host.hidden = false;
    for (var i = 0; i < plugins.length; i++) {
      var plugin = plugins[i];
      var label = buildPluginPolicyLabel(plugin);
      if (!label) {
        continue;
      }

      var provenanceParts = [
        "origin " + describePluginOrigin(plugin.origin, plugin.sourceKind),
        "source " + packSourceLabel(plugin.sourceKind),
        plugin.defaultEnabled ? "default enabled" : "default disabled"
      ];
      appendOptionsKv(host, label + " provenance", provenanceParts.join(" | "));

      var runtimeParts = [plugin.enabled ? "enabled" : "disabled"];
      if (plugin.isDangerous) {
        runtimeParts.push("dangerous");
      }
      if (!plugin.enabled && plugin.disabledReason) {
        runtimeParts.push("reason " + plugin.disabledReason);
      }
      appendOptionsKv(host, label + " runtime", runtimeParts.join(" | "));

      if (plugin.packIds.length > 0) {
        appendOptionsKv(host, label + " packs", plugin.packIds.join(", "));
      }
      if (plugin.skillIds.length > 0) {
        appendOptionsKv(host, label + " skills", plugin.skillIds.join(", "));
      }

      var identityParts = [];
      if (plugin.version) {
        identityParts.push("version " + plugin.version);
      }
      if (plugin.rootPath) {
        identityParts.push("root " + plugin.rootPath);
      }
      if (identityParts.length > 0) {
        appendOptionsKv(host, label + " identity", identityParts.join(" | "));
      }
    }

    if (host.childElementCount === 0) {
      host.hidden = true;
    }
  }

  function renderRoutingCatalogPolicy(host, routingCatalog, routingPromptExposure) {
    if (!host) {
      return;
    }

    host.innerHTML = "";
    host.classList.remove("options-policy-list-warn");

    if (!routingCatalog && !routingPromptExposure) {
      host.hidden = true;
      return;
    }

    var lines = [];
    if (!routingCatalog) {
      lines.push("catalog: unavailable");
    } else {
      lines.push("health: " + (routingCatalog.isHealthy ? "healthy" : "degraded"));
      lines.push("explicit-ready: " + (routingCatalog.isExplicitRoutingReady ? "yes" : "no"));
      lines.push("routing-aware: " + routingCatalog.routingAwareTools + "/" + routingCatalog.totalTools);
      lines.push("routing source: explicit " + routingCatalog.explicitRoutingTools + ", inferred " + routingCatalog.inferredRoutingTools);
      lines.push("contract-aware: setup " + routingCatalog.setupAwareTools + ", handoff " + routingCatalog.handoffAwareTools + ", recovery " + routingCatalog.recoveryAwareTools);
      lines.push("autonomy surface: remote-capable " + routingCatalog.remoteCapableTools + ", cross-pack handoffs " + routingCatalog.crossPackHandoffTools);
      for (var i = 0; i < routingCatalog.autonomyReadinessHighlights.length; i++) {
        lines.push("autonomy readiness: " + routingCatalog.autonomyReadinessHighlights[i]);
      }
      lines.push("domain-family tools: " + routingCatalog.domainFamilyTools);
      if (routingCatalog.missingRoutingContractTools > 0) {
        lines.push("missing contracts: " + routingCatalog.missingRoutingContractTools);
      }
      if (routingCatalog.missingPackIdTools > 0) {
        lines.push("missing pack id: " + routingCatalog.missingPackIdTools);
      }
      if (routingCatalog.missingRoleTools > 0) {
        lines.push("missing role: " + routingCatalog.missingRoleTools);
      }
      if (routingCatalog.expectedDomainFamilyMissingTools > 0) {
        lines.push("expected family missing: " + routingCatalog.expectedDomainFamilyMissingTools);
      }
      if (routingCatalog.domainFamilyMissingActionTools > 0) {
        lines.push("family missing action: " + routingCatalog.domainFamilyMissingActionTools);
      }
      if (routingCatalog.actionWithoutFamilyTools > 0) {
        lines.push("action without family: " + routingCatalog.actionWithoutFamilyTools);
      }
      if (routingCatalog.familyActionConflictFamilies > 0) {
        lines.push("family action conflicts: " + routingCatalog.familyActionConflictFamilies);
      }

      for (var i = 0; i < routingCatalog.familyActions.length; i++) {
        var familyAction = routingCatalog.familyActions[i];
        lines.push("family " + familyAction.family + " -> " + familyAction.actionId + " (" + familyAction.toolCount + ")");
      }
    }

    if (routingPromptExposure) {
      lines.push("latest prompt exposure: " + routingPromptExposure.strategy + " (" + routingPromptExposure.selectedToolCount + "/" + routingPromptExposure.totalToolCount + ")");
      if (routingPromptExposure.requestId || routingPromptExposure.threadId) {
        lines.push("prompt source: " + (routingPromptExposure.requestId || "n/a") + " @ " + (routingPromptExposure.threadId || "n/a"));
      }
      lines.push("prompt reordered: " + (routingPromptExposure.reordered ? "yes" : "no"));
      if (routingPromptExposure.topToolNames.length > 0) {
        lines.push("prompt top tools: " + routingPromptExposure.topToolNames.join(", "));
      }
    }

    if (routingCatalog && !routingCatalog.isHealthy) {
      host.classList.add("options-policy-list-warn");
    }

    renderPolicyList(host, lines, "Routing catalog");
  }

  function renderCapabilitySnapshotPolicy(host, capabilitySnapshot) {
    if (!host) {
      return;
    }

    host.innerHTML = "";
    host.classList.remove("options-policy-list-warn");

    if (!capabilitySnapshot) {
      host.hidden = true;
      return;
    }

    var lines = [];
    lines.push("tooling available: " + (capabilitySnapshot.toolingAvailable ? "yes" : "no"));
    lines.push("registered tools: " + capabilitySnapshot.registeredTools);
    lines.push("enabled packs: " + capabilitySnapshot.enabledPackCount);
    lines.push("plugins: enabled " + capabilitySnapshot.enabledPluginCount + "/" + capabilitySnapshot.pluginCount);
    lines.push("allowed roots: " + capabilitySnapshot.allowedRootCount);
    lines.push("remote reachability: " + capabilitySnapshot.remoteReachabilityMode);
    lines.push("routing families: " + capabilitySnapshot.routingFamilies.length);
    if (capabilitySnapshot.skills.length > 0) {
      lines.push("skills: " + capabilitySnapshot.skills.join(", "));
    }
    if (capabilitySnapshot.enabledPackIds.length > 0) {
      lines.push("enabled pack ids: " + capabilitySnapshot.enabledPackIds.join(", "));
    }
    if (capabilitySnapshot.healthyTools.length > 0) {
      lines.push("healthy tools: " + capabilitySnapshot.healthyTools.length);
    }
    if (capabilitySnapshot.toolingSnapshot) {
      var toolingSource = capabilitySnapshot.toolingSnapshot.source || "registered_export";
      lines.push("tooling snapshot: " + toolingSource + ", packs " + capabilitySnapshot.toolingSnapshot.packs.length + ", plugins " + capabilitySnapshot.toolingSnapshot.plugins.length);
    }

    var autonomy = capabilitySnapshot.autonomy;
    if (autonomy) {
      lines.push("autonomy surface: remote-capable " + autonomy.remoteCapableToolCount + ", setup-aware " + autonomy.setupAwareToolCount + ", handoff-aware " + autonomy.handoffAwareToolCount + ", recovery-aware " + autonomy.recoveryAwareToolCount);
      lines.push("cross-pack handoffs: " + autonomy.crossPackHandoffToolCount);
      if (autonomy.remoteCapablePackIds.length > 0) {
        lines.push("remote-capable packs: " + autonomy.remoteCapablePackIds.join(", "));
      }
      if (autonomy.crossPackReadyPackIds.length > 0) {
        lines.push("cross-pack ready packs: " + autonomy.crossPackReadyPackIds.join(", "));
      }
      if (autonomy.crossPackTargetPackIds.length > 0) {
        lines.push("cross-pack target packs: " + autonomy.crossPackTargetPackIds.join(", "));
      }
    }

    if (capabilitySnapshot.parityAttentionCount > 0) {
      lines.push("parity attention: " + capabilitySnapshot.parityAttentionCount);
    }
    if (capabilitySnapshot.parityMissingCapabilityCount > 0) {
      lines.push("parity missing capability: " + capabilitySnapshot.parityMissingCapabilityCount);
    }

    for (var i = 0; i < capabilitySnapshot.familyActions.length; i++) {
      var familyAction = capabilitySnapshot.familyActions[i];
      lines.push("family " + familyAction.family + " -> " + familyAction.actionId + " (" + familyAction.toolCount + ")");
    }

    renderPolicyList(host, lines, "Capability snapshot");
  }

  function filterOutRoutingCatalogWarnings(values) {
    if (!Array.isArray(values) || values.length === 0) {
      return [];
    }

    var filtered = [];
    for (var i = 0; i < values.length; i++) {
      var value = values[i];
      if (typeof value !== "string") {
        continue;
      }

      var normalized = value.trim();
      if (!normalized) {
        continue;
      }

      if (normalized.toLowerCase().indexOf("[routing catalog]") === 0) {
        continue;
      }

      filtered.push(normalized);
    }

    return filtered;
  }

  function renderPolicyList(host, values, title) {
    if (!host) {
      return;
    }

    host.innerHTML = "";
    if (!values || values.length === 0) {
      host.hidden = true;
      return;
    }

    host.hidden = false;

    var heading = document.createElement("div");
    heading.className = "options-policy-list-title";
    heading.textContent = title;
    host.appendChild(heading);

    var list = document.createElement("ul");
    list.className = "options-policy-list";
    for (var i = 0; i < values.length; i++) {
      var item = document.createElement("li");
      item.textContent = values[i];
      list.appendChild(item);
    }
    host.appendChild(list);
  }
