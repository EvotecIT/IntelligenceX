(function() {
  function byId(id) { return document.getElementById(id); }
  function post(type, extra) {
    var payload = Object.assign({ type: type }, extra || {});
    window.chrome.webview.postMessage(JSON.stringify(payload));
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
    connected: false,
    authenticated: false,
    loginInProgress: false,
    sending: false,
    cancelable: false,
    cancelRequested: false,
    windowMaximized: false,
    debugMode: false,
    expandedToolPacks: {},
    options: {
      timestampMode: "seconds",
      timestampFormat: "HH:mm:ss",
      export: {
        saveMode: "ask",
        defaultFormat: "xlsx",
        lastDirectory: ""
      },
      autonomy: {
        maxToolRounds: null,
        parallelTools: null,
        turnTimeoutSeconds: null,
        toolTimeoutSeconds: null,
        weightedToolRouting: null,
        maxCandidateTools: null
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
        models: [],
        favoriteModels: [],
        recentModels: [],
        isStale: false,
        warning: "",
        profileSaved: false,
        runtimeDetection: {
          hasRun: false,
          lmStudioAvailable: false,
          ollamaAvailable: false,
          detectedName: "",
          detectedBaseUrl: "",
          warning: ""
        }
      },
      debugToolsEnabled: false,
      toolFilter: "",
      policy: null,
      packs: [],
      tools: []
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
    if (document.body.classList.contains("data-view-open")) {
      if (eventTarget && eventTarget.closest) {
        var dataViewScrollTarget = eventTarget.closest(".dt-scroll-body, #dataViewBody, .data-view-panel .dt-layout-cell");
        if (dataViewScrollTarget) {
          return dataViewScrollTarget;
        }
      }

      if (dataViewBody) {
        return dataViewBody;
      }
    }

    if (document.body.classList.contains("options-open")) {
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

      if (optionsBody) {
        return optionsBody;
      }
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

    // If nested area cannot move further, route to panel-level scroller.
    if (document.body.classList.contains("data-view-open") && dataViewBody && target !== dataViewBody) {
      before = dataViewBody.scrollTop;
      dataViewBody.scrollTop += amount;
      if (dataViewBody.scrollTop !== before) {
        return true;
      }
    }

    if (document.body.classList.contains("options-open") && optionsBody && target !== optionsBody) {
      before = optionsBody.scrollTop;
      optionsBody.scrollTop += amount;
      if (optionsBody.scrollTop !== before) {
        return true;
      }
    }

    if (!document.body.classList.contains("data-view-open") && target !== transcript) {
      before = transcript.scrollTop;
      transcript.scrollTop += amount;
      return transcript.scrollTop !== before;
    }

    return false;
  }

  function openOptions() {
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

  function updateStatusVisual(text, tone) {
    var statusEl = byId("status");
    var value = text || "";
    var lower = value.toLowerCase();
    var normalizedTone = "";
    if (typeof tone === "string") {
      normalizedTone = tone.trim().toLowerCase();
    }

    statusEl.textContent = value;
    statusEl.classList.remove("ok", "warn", "bad");
    if (normalizedTone === "ok") {
      statusEl.classList.add("ok");
      return;
    }
    if (normalizedTone === "warn") {
      statusEl.classList.add("warn");
      return;
    }
    if (normalizedTone === "bad") {
      statusEl.classList.add("bad");
      return;
    }

    if (lower.indexOf("failed") >= 0 || lower.indexOf("error") >= 0 || lower.indexOf("limit") >= 0 || lower.indexOf("quota") >= 0 || lower.indexOf("unavailable") >= 0) {
      statusEl.classList.add("bad");
    } else if (lower.indexOf("connected") >= 0 || lower.indexOf("ready") >= 0) {
      statusEl.classList.add("ok");
    } else if (lower.indexOf("sign") >= 0 || lower.indexOf("wait") >= 0 || lower.indexOf("open") >= 0 || lower.indexOf("start") >= 0) {
      statusEl.classList.add("warn");
    }
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
    var switchRecommended = normalizeBool(state.usageLimitSwitchRecommended) || normalizeBool(state.queuedPromptPending);
    var debugToolsEnabled = normalizeBool(state.options.debugToolsEnabled);

    signIn.hidden = false;
    signIn.disabled = loginInProgress;
    if (authenticated) {
      signIn.textContent = loginInProgress ? "Signing In..." : "Sign In Again";
      signIn.setAttribute("data-cmd", "relogin");
    } else {
      signIn.textContent = loginInProgress
        ? "Signing In..."
        : (switchRecommended ? "Sign In (retry queued prompt)" : "Sign In");
      signIn.setAttribute("data-cmd", "login");
    }

    if (switchAccount) {
      switchAccount.hidden = false;
      switchAccount.disabled = loginInProgress;
      switchAccount.textContent = switchRecommended ? "Switch Account (Recommended)" : "Switch Account";
    }

    reconnect.textContent = normalizeBool(state.connected) ? "Reconnect runtime" : "Start runtime";
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
    var busy = sending || loginBusy;

    send.disabled = loginBusy || (sending && !cancelable) || (cancelable && cancelRequested);
    send.classList.toggle("cancel-mode", cancelable);

    if (cancelable) {
      send.setAttribute("aria-label", cancelRequested ? "Canceling turn" : "Stop turn");
      send.title = cancelRequested ? "Canceling..." : "Stop";
      send.innerHTML = "<svg width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2.5' stroke-linecap='round' stroke-linejoin='round'><rect x='7' y='7' width='10' height='10' rx='2'/></svg>";
    } else {
      send.setAttribute("aria-label", "Send");
      send.title = "Send";
      send.innerHTML = "<svg width='18' height='18' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2.5' stroke-linecap='round' stroke-linejoin='round'><line x1='12' y1='19' x2='12' y2='5'/><polyline points='5 12 12 5 19 12'/></svg>";
    }

    promptEl.setAttribute("aria-busy", busy ? "true" : "false");
    promptEl.placeholder = busy ? "IntelligenceX is working..." : "Ask IntelligenceX...";
  }

  function renderPolicy() {
    var policyEl = byId("policyInfo");
    var startupWarningsEl = byId("policyStartupWarnings");
    var pluginRootsEl = byId("policyPluginRoots");
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
      return;
    }

    var startupWarnings = toStringArray(p.startupWarnings);
    var pluginSearchPaths = toStringArray(p.pluginSearchPaths);
    var rows = [
      ["Read-only", p.readOnly ? "Yes" : "No"],
      ["Parallel tools", p.parallelTools ? "Yes" : "No"],
      ["Max tool rounds", p.maxToolRounds == null ? "Default" : String(p.maxToolRounds)],
      ["Turn timeout", p.turnTimeoutSeconds == null ? "Default" : (String(p.turnTimeoutSeconds) + "s")],
      ["Tool timeout", p.toolTimeoutSeconds == null ? "Default" : (String(p.toolTimeoutSeconds) + "s")],
      ["Plugin roots", pluginSearchPaths.length === 0 ? "None" : String(pluginSearchPaths.length)],
      ["Runtime notices", startupWarnings.length === 0 ? "None" : String(startupWarnings.length)]
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

    renderPolicyList(pluginRootsEl, pluginSearchPaths, "Plugin search roots");
    renderPolicyList(startupWarningsEl, startupWarnings, startupWarnings.length === 1 ? "Runtime notice" : "Runtime notices");
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
