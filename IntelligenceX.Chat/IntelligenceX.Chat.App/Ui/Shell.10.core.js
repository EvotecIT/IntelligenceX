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
    sending: false,
    cancelable: false,
    cancelRequested: false,
    activityTimeline: [],
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
      memoryDebug: null,
      activeProfileName: "default",
      profileNames: ["default"],
      activeConversationId: "",
      conversations: [],
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
        model: "gpt-5.3-codex",
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
      policy: null,
      packs: [],
      tools: [],
      toolsLoading: true
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

  function resolveHeaderStatusChipFromStructuredStartupContext() {
    var context = parseStartupStatusContext(state.status);
    if (!context) {
      return null;
    }

    if (context.phase === "startup_auth_wait") {
      if (context.cause === "auth_wait") {
        return { text: "Sign in to continue loading tool packs", tone: "warn" };
      }

      return { text: "Sign in to continue", tone: "warn" };
    }

    if (context.phase === "startup_metadata_sync") {
      if (context.cause === "metadata_retry") {
        return { text: "Loading tool packs (retrying metadata sync)", tone: "warn" };
      }

      return { text: "Loading tool packs", tone: "warn" };
    }

    if (context.phase === "startup_connect") {
      if (context.cause === "pipe_retry") {
        return { text: "Starting runtime (retrying connection)", tone: "warn" };
      }

      return { text: "Starting runtime", tone: "warn" };
    }

    return null;
  }

  function resolveHeaderStatusChipFallbackStatus() {
    var structuredStartupStatus = resolveHeaderStatusChipFromStructuredStartupContext();
    if (structuredStartupStatus) {
      return structuredStartupStatus;
    }

    if (normalizeBool(state.connected)) {
      if (normalizeBool(state.authenticated)) {
        return { text: "Ready", tone: "ok" };
      }
      return { text: "Sign in to continue", tone: "warn" };
    }

    return { text: "Starting runtime...", tone: "warn" };
  }

  function buildRoutingStatusChipModel() {
    var policy = (state.options && state.options.policy) || null;
    var routingCatalog = normalizeRoutingCatalog(policy ? policy.routingCatalog : null);
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

  function buildStatusChipTitle(displayValue, rawValue) {
    var lines = [];
    var normalizedDisplay = normalizeStatusTimelineEntry(displayValue);
    var normalizedRaw = normalizeStatusTimelineEntry(rawValue);
    if (normalizedDisplay) {
      lines.push("Status: " + normalizedDisplay);
    }
    if (normalizedRaw && normalizedRaw !== normalizedDisplay) {
      lines.push("Detail: " + normalizedRaw);
    }
    if (Array.isArray(state.statusTimeline) && state.statusTimeline.length > 0) {
      lines.push("Lifecycle: " + state.statusTimeline.join(" > "));
    }
    return lines.join("\n");
  }

  function updateStatusVisual(text, tone) {
    var statusEl = byId("status");
    var rawValue = String(text || "").trim();
    var value = rawValue;
    var lowerRawValue = rawValue.toLowerCase();
    var normalizedTone = "";
    if (typeof tone === "string") {
      normalizedTone = tone.trim().toLowerCase();
    }
    if (normalizeBool(state.connected)
      && !normalizeBool(state.authenticated)
      && normalizeBool(state.options && state.options.toolsLoading)
      && lowerRawValue.indexOf("sign in to continue") === 0
      && lowerRawValue.indexOf("loading tool packs") < 0) {
      value = "Sign in to continue loading tool packs";
      if (!normalizedTone) {
        normalizedTone = "warn";
      }
    }
    if (!shouldRenderHeaderStatusChip(value)) {
      var fallbackStatus = resolveHeaderStatusChipFallbackStatus();
      value = fallbackStatus.text;
      normalizedTone = fallbackStatus.tone;
    }
    appendStatusTimelineEntry(rawValue || value);
    var shouldAppendRuntime = value.indexOf("|") < 0;
    var displayValue = value;

    if (shouldAppendRuntime && (normalizedTone === "ok" || normalizedTone.length === 0)) {
      var lowerForAppend = value.toLowerCase();
      if (lowerForAppend.indexOf("ready") >= 0 || lowerForAppend.indexOf("connected") >= 0) {
        displayValue = value + " - " + resolveStatusRuntimeSummary();
      }
    }

    statusEl.textContent = displayValue;
    statusEl.title = buildStatusChipTitle(displayValue, rawValue);
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
      signIn.textContent = loginInProgress ? "Signing In..." : "Sign In Again";
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
    var startupWarningsEl = byId("policyStartupWarnings");
    var pluginRootsEl = byId("policyPluginRoots");
    var routingCatalogEl = byId("policyRoutingCatalog");
    policyEl.innerHTML = "";

    var p = state.options.policy;
    if (!p) {
      policyEl.innerHTML = "<span class='options-k'>Policy</span><span class='options-v'>Not available</span>";
      if (startupWarningsEl) {
        startupWarningsEl.hidden = true;
        startupWarningsEl.innerHTML = "";
      }
      if (pluginRootsEl) {
        pluginRootsEl.hidden = true;
        pluginRootsEl.innerHTML = "";
      }
      if (routingCatalogEl) {
        routingCatalogEl.hidden = true;
        routingCatalogEl.innerHTML = "";
        routingCatalogEl.classList.remove("options-policy-list-warn");
      }
      return;
    }

    var startupWarnings = toStringArray(p.startupWarnings);
    var startupBootstrap = normalizeStartupBootstrap(p.startupBootstrap);
    var pluginSearchPaths = toStringArray(p.pluginSearchPaths);
    var runtimePolicy = normalizeRuntimePolicy(p.runtimePolicy);
    var routingCatalog = normalizeRoutingCatalog(p.routingCatalog);
    var runtimeNotices = routingCatalog
      ? filterOutRoutingCatalogWarnings(startupWarnings)
      : startupWarnings;
    var routingIssueCount = routingCatalog ? computeRoutingCatalogIssueCount(routingCatalog) : 0;
    var explicitReadinessIssueCount = routingCatalog ? computeExplicitRoutingReadinessIssueCount(routingCatalog) : 0;
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
      ["Routing families", !routingCatalog ? "N/A" : String(routingCatalog.familyActions.length)],
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

    renderRoutingCatalogPolicy(routingCatalogEl, routingCatalog);
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

    return {
      totalMs: toNonNegativeInt(value.totalMs),
      runtimePolicyMs: toNonNegativeInt(value.runtimePolicyMs),
      bootstrapOptionsMs: toNonNegativeInt(value.bootstrapOptionsMs),
      packLoadMs: toNonNegativeInt(value.packLoadMs),
      registryMs: toNonNegativeInt(value.registryMs),
      tools: toNonNegativeInt(value.tools),
      packsLoaded: toNonNegativeInt(value.packsLoaded),
      packsDisabled: toNonNegativeInt(value.packsDisabled),
      pluginRoots: toNonNegativeInt(value.pluginRoots),
      slowPackCount: toNonNegativeInt(value.slowPackCount),
      slowPackTopCount: toNonNegativeInt(value.slowPackTopCount),
      packProgressProcessed: toNonNegativeInt(value.packProgressProcessed),
      packProgressTotal: toNonNegativeInt(value.packProgressTotal),
      slowPluginCount: toNonNegativeInt(value.slowPluginCount),
      slowPluginTopCount: toNonNegativeInt(value.slowPluginTopCount),
      pluginProgressProcessed: toNonNegativeInt(value.pluginProgressProcessed),
      pluginProgressTotal: toNonNegativeInt(value.pluginProgressTotal)
    };
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
    segments.push("total " + formatStartupBootstrapDuration(telemetry.totalMs));
    segments.push("pack-load " + formatStartupBootstrapDuration(telemetry.packLoadMs));
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

    var familyActionsRaw = Array.isArray(value.familyActions) ? value.familyActions : [];
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
      domainFamilyTools: toNonNegativeInt(value.domainFamilyTools),
      expectedDomainFamilyMissingTools: toNonNegativeInt(value.expectedDomainFamilyMissingTools),
      domainFamilyMissingActionTools: toNonNegativeInt(value.domainFamilyMissingActionTools),
      actionWithoutFamilyTools: toNonNegativeInt(value.actionWithoutFamilyTools),
      familyActionConflictFamilies: toNonNegativeInt(value.familyActionConflictFamilies),
      // Keep version-skew payloads neutral by default; derive degraded states only when required counters are present.
      isHealthy: hasIsHealthy ? value.isHealthy === true : true,
      isExplicitRoutingReady: hasIsExplicitRoutingReady ? value.isExplicitRoutingReady === true : true,
      familyActions: familyActions
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

  function renderRoutingCatalogPolicy(host, routingCatalog) {
    if (!host) {
      return;
    }

    host.innerHTML = "";
    host.classList.remove("options-policy-list-warn");

    if (!routingCatalog) {
      host.hidden = true;
      return;
    }

    var lines = [];
    lines.push("health: " + (routingCatalog.isHealthy ? "healthy" : "degraded"));
    lines.push("explicit-ready: " + (routingCatalog.isExplicitRoutingReady ? "yes" : "no"));
    lines.push("routing-aware: " + routingCatalog.routingAwareTools + "/" + routingCatalog.totalTools);
    lines.push("routing source: explicit " + routingCatalog.explicitRoutingTools + ", inferred " + routingCatalog.inferredRoutingTools);
    lines.push("contract-aware: setup " + routingCatalog.setupAwareTools + ", handoff " + routingCatalog.handoffAwareTools + ", recovery " + routingCatalog.recoveryAwareTools);
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

    if (!routingCatalog.isHealthy) {
      host.classList.add("options-policy-list-warn");
    }

    renderPolicyList(host, lines, "Routing catalog");
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
