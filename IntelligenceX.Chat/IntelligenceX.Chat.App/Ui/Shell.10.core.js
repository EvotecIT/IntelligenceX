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
    status: "Starting...",
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
        toolTimeoutSeconds: null
      },
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

  function updateStatusVisual(text) {
    var statusEl = byId("status");
    var value = text || "";
    var lower = value.toLowerCase();

    statusEl.textContent = value;
    statusEl.classList.remove("ok", "warn", "bad");
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
    var debugToolsEnabled = normalizeBool(state.options.debugToolsEnabled);

    signIn.hidden = false;
    signIn.disabled = loginInProgress;
    if (authenticated) {
      signIn.textContent = loginInProgress ? "Signing In..." : "Sign In Again";
      signIn.setAttribute("data-cmd", "relogin");
    } else {
      signIn.textContent = loginInProgress ? "Signing In..." : "Sign In";
      signIn.setAttribute("data-cmd", "login");
    }

    if (switchAccount) {
      switchAccount.hidden = !authenticated;
      switchAccount.disabled = loginInProgress;
    }

    reconnect.textContent = normalizeBool(state.connected) ? "Refresh session" : "Retry now";
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
    policyEl.innerHTML = "";

    var p = state.options.policy;
    if (!p) {
      policyEl.innerHTML = "<span class='options-k'>Policy</span><span class='options-v'>Not available</span>";
      return;
    }

    var rows = [
      ["Read-only", p.readOnly ? "Yes" : "No"],
      ["Parallel tools", p.parallelTools ? "Yes" : "No"],
      ["Max tool rounds", p.maxToolRounds == null ? "Default" : String(p.maxToolRounds)],
      ["Turn timeout", p.turnTimeoutSeconds == null ? "Default" : (String(p.turnTimeoutSeconds) + "s")],
      ["Tool timeout", p.toolTimeoutSeconds == null ? "Default" : (String(p.toolTimeoutSeconds) + "s")]
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
  }

  function conversationTitle(item) {
    var title = (item && item.title ? String(item.title) : "").trim();
    return title || "New Chat";
  }

  function conversationMeta(item) {
    var count = item && typeof item.messageCount === "number" ? item.messageCount : 0;
    var updated = item && item.updatedLocal ? String(item.updatedLocal) : "";
    var base = String(count) + (count === 1 ? " message" : " messages");
    return updated ? (base + " · " + updated) : base;
  }

  function renderSidebarConversations() {
    var host = chatSidebarList;
    if (!host) {
      return;
    }

    host.innerHTML = "";
    var items = state.options.conversations || [];
    if (items.length === 0) {
      var empty = document.createElement("div");
      empty.className = "chat-sidebar-empty";
      empty.textContent = "No chats yet";
      host.appendChild(empty);
      return;
    }

    for (var i = 0; i < items.length; i++) {
      var chat = items[i];
      var id = chat && chat.id ? String(chat.id) : "";
      if (!id) {
        continue;
      }

      var button = document.createElement("button");
      button.type = "button";
      button.className = "chat-sidebar-item";
      button.dataset.conversationId = id;
      if (id === state.options.activeConversationId) {
        button.classList.add("active");
      }

      var body = document.createElement("div");
      body.className = "chat-sidebar-item-body";

      var title = document.createElement("span");
      title.className = "chat-sidebar-item-title";
      title.textContent = conversationTitle(chat);
      body.appendChild(title);

      var meta = document.createElement("span");
      meta.className = "chat-sidebar-item-meta";
      meta.textContent = conversationMeta(chat);
      body.appendChild(meta);

      button.appendChild(body);

      var deleteBtn = document.createElement("span");
      deleteBtn.className = "chat-sidebar-item-delete";
      deleteBtn.setAttribute("role", "button");
      deleteBtn.setAttribute("aria-label", "Delete conversation");
      deleteBtn.title = "Delete";
      deleteBtn.dataset.conversationId = id;
      deleteBtn.dataset.conversationTitle = conversationTitle(chat);
      deleteBtn.innerHTML = "<svg width='10' height='10' viewBox='0 0 16 16' fill='none'><path d='M4 4l8 8M12 4l-8 8' stroke='currentColor' stroke-width='1.8' stroke-linecap='round'/></svg>";
      button.appendChild(deleteBtn);

      host.appendChild(button);
    }
  }

  function renderOptionsConversations() {
    var host = byId("optConversations");
    if (!host) {
      return;
    }

    host.innerHTML = "";
    var items = state.options.conversations || [];
    if (items.length === 0) {
      host.innerHTML = "<div class='options-item'><div class='options-item-title'>No chats yet</div></div>";
      return;
    }

    for (var i = 0; i < items.length; i++) {
      var chat = items[i];
      var id = chat && chat.id ? String(chat.id) : "";
      if (!id) {
        continue;
      }

      var row = document.createElement("div");
      row.className = "options-conversation-item";
      if (id === state.options.activeConversationId) {
        row.classList.add("active");
      }

      var main = document.createElement("div");
      main.className = "options-conversation-main";

      var title = document.createElement("div");
      title.className = "options-conversation-title";
      title.textContent = conversationTitle(chat);
      main.appendChild(title);

      var meta = document.createElement("div");
      meta.className = "options-conversation-meta";
      meta.textContent = conversationMeta(chat);
      main.appendChild(meta);

      if (chat.preview) {
        var preview = document.createElement("div");
        preview.className = "options-conversation-preview";
        preview.textContent = String(chat.preview);
        main.appendChild(preview);
      }

      row.appendChild(main);

      if (id !== state.options.activeConversationId) {
        var actions = document.createElement("div");
        actions.className = "options-conversation-actions";

        var openButton = document.createElement("button");
        openButton.type = "button";
        openButton.className = "options-btn options-btn-sm options-conversation-switch";
        openButton.dataset.action = "switch";
        openButton.dataset.conversationId = id;
        openButton.textContent = "Open";
        actions.appendChild(openButton);

        var renameButton = document.createElement("button");
        renameButton.type = "button";
        renameButton.className = "options-btn options-btn-sm options-btn-ghost options-conversation-switch";
        renameButton.dataset.action = "rename";
        renameButton.dataset.conversationId = id;
        renameButton.dataset.currentTitle = conversationTitle(chat);
        renameButton.textContent = "Rename";
        actions.appendChild(renameButton);

        var deleteButton = document.createElement("button");
        deleteButton.type = "button";
        deleteButton.className = "options-btn options-btn-sm options-btn-danger options-conversation-switch";
        deleteButton.dataset.action = "delete";
        deleteButton.dataset.conversationId = id;
        deleteButton.dataset.currentTitle = conversationTitle(chat);
        deleteButton.textContent = "Delete";
        actions.appendChild(deleteButton);

        row.appendChild(actions);
      } else {
        var activeActions = document.createElement("div");
        activeActions.className = "options-conversation-actions";

        var activePill = document.createElement("span");
        activePill.className = "options-pill";
        activePill.textContent = "Active";
        activeActions.appendChild(activePill);

        var renameActiveButton = document.createElement("button");
        renameActiveButton.type = "button";
        renameActiveButton.className = "options-btn options-btn-sm options-btn-ghost options-conversation-switch";
        renameActiveButton.dataset.action = "rename";
        renameActiveButton.dataset.conversationId = id;
        renameActiveButton.dataset.currentTitle = conversationTitle(chat);
        renameActiveButton.textContent = "Rename";
        activeActions.appendChild(renameActiveButton);

        var deleteActiveButton = document.createElement("button");
        deleteActiveButton.type = "button";
        deleteActiveButton.className = "options-btn options-btn-sm options-btn-danger options-conversation-switch";
        deleteActiveButton.dataset.action = "delete";
        deleteActiveButton.dataset.conversationId = id;
        deleteActiveButton.dataset.currentTitle = conversationTitle(chat);
        deleteActiveButton.textContent = "Delete";
        activeActions.appendChild(deleteActiveButton);

        row.appendChild(activeActions);
      }

      host.appendChild(row);
    }
  }

  function positionSelectMenu(wrap) {
    var button = wrap.querySelector(".ix-select-btn");
    var menu = wrap.querySelector(".ix-select-menu");
    if (!button || !menu) {
      return;
    }

    var rect = button.getBoundingClientRect();
    menu.style.position = "fixed";
    menu.style.left = rect.left + "px";
    menu.style.top = (rect.bottom + 4) + "px";
    menu.style.width = rect.width + "px";
    menu.style.right = "auto";
  }

  function clearSelectMenuPosition(wrap) {
    var menu = wrap ? wrap.querySelector(".ix-select-menu") : null;
    if (!menu) {
      return;
    }
    menu.style.position = "";
    menu.style.left = "";
    menu.style.top = "";
    menu.style.width = "";
    menu.style.right = "";
  }

  function closeOpenCustomSelect() {
    if (!openCustomSelect) {
      return;
    }
    clearSelectMenuPosition(openCustomSelect);
    openCustomSelect.classList.remove("open");
    openCustomSelect = null;
  }

  function syncCustomSelect(select) {
    if (!select || !select._ixWrap || !select._ixButton || !select._ixMenu) {
      return;
    }

    var button = select._ixButton;
    var menu = select._ixMenu;
    menu.innerHTML = "";

    var selectedText = "";
    for (var i = 0; i < select.options.length; i++) {
      var option = select.options[i];
      if (option.selected) {
        selectedText = option.textContent || option.value || "";
      }

      var item = document.createElement("button");
      item.type = "button";
      item.className = "ix-select-item";
      if (option.selected) {
        item.classList.add("active");
      }
      item.dataset.value = option.value;
      item.textContent = option.textContent || option.value || "";
      menu.appendChild(item);
    }

    if (!selectedText && select.options.length > 0) {
      selectedText = select.options[0].textContent || select.options[0].value || "";
    }

    button.querySelector(".ix-select-label").textContent = selectedText || "";
  }

  function ensureCustomSelect(selectId) {
    var select = byId(selectId);
    if (!select) {
      return;
    }

    if (!select._ixWrap) {
      var parent = select.parentNode;
      var wrap = document.createElement("div");
      wrap.className = "ix-select";
      parent.insertBefore(wrap, select);
      wrap.appendChild(select);

      select.classList.add("options-select-native");
      select.tabIndex = -1;
      select.setAttribute("aria-hidden", "true");

      var button = document.createElement("button");
      button.type = "button";
      button.className = "ix-select-btn";
      button.innerHTML = "<span class='ix-select-label'></span><span class='ix-select-caret' aria-hidden='true'></span>";
      wrap.appendChild(button);

      var menu = document.createElement("div");
      menu.className = "ix-select-menu";
      wrap.appendChild(menu);

      select._ixWrap = wrap;
      select._ixButton = button;
      select._ixMenu = menu;

      button.addEventListener("click", function(e) {
        e.preventDefault();
        e.stopPropagation();
        if (openCustomSelect && openCustomSelect !== wrap) {
          closeOpenCustomSelect();
        }
        var willOpen = !wrap.classList.contains("open");
        wrap.classList.toggle("open", willOpen);
        if (willOpen) {
          positionSelectMenu(wrap);
          openCustomSelect = wrap;
        } else {
          clearSelectMenuPosition(wrap);
          openCustomSelect = null;
        }
      });

      button.addEventListener("keydown", function(e) {
        var key = e.key;
        var count = select.options.length;

        if (key === "Enter" || key === " ") {
          e.preventDefault();
          button.click();
          return;
        }

        if (key === "Escape") {
          closeOpenCustomSelect();
          return;
        }

        if (count === 0) {
          return;
        }

        var idx = select.selectedIndex >= 0 ? select.selectedIndex : 0;
        if (key === "ArrowDown") {
          idx = Math.min(count - 1, idx + 1);
        } else if (key === "ArrowUp") {
          idx = Math.max(0, idx - 1);
        } else if (key === "Home") {
          idx = 0;
        } else if (key === "End") {
          idx = count - 1;
        } else {
          return;
        }

        e.preventDefault();
        if (select.selectedIndex !== idx) {
          select.selectedIndex = idx;
          select.dispatchEvent(new Event("change", { bubbles: true }));
        } else {
          syncCustomSelect(select);
        }
      });

      menu.addEventListener("click", function(e) {
        var optionButton = e.target.closest(".ix-select-item");
        if (!optionButton) {
          return;
        }

        var value = optionButton.dataset.value || "";
        if (select.value !== value) {
          select.value = value;
          select.dispatchEvent(new Event("change", { bubbles: true }));
        }

        closeOpenCustomSelect();
        button.focus();
      });

      select.addEventListener("change", function() {
        syncCustomSelect(select);
      });
    }

    syncCustomSelect(select);
  }

  function normalizePackId(value) {
    return (value || "").toLowerCase().replace(/[\s_]/g, "-");
  }

  function findPackById(packId) {
    var packs = state.options.packs || [];
    var normalized = normalizePackId(packId);
    for (var i = 0; i < packs.length; i++) {
      if (normalizePackId(packs[i].id) === normalized) {
        return packs[i];
      }
    }
    return null;
  }

  function mapCategoryToPackId(category) {
    var normalized = normalizePackId(category);
    if (normalized === "active-directory") return "ad";
    if (normalized === "event-log") return "eventlog";
    if (normalized === "file-system") return "fs";
    if (normalized === "system") return "system";
    if (normalized === "email") return "email";
    if (normalized === "testimox") return "testimox";
    if (normalized === "reviewer-setup") return "reviewer-setup";
    return "";
  }

  function inferPackIdFromTool(tool) {
    if (!tool) {
      return "other";
    }

    if (tool.packId) {
      var explicit = normalizePackId(tool.packId);
      if (explicit) {
        return explicit;
      }
    }

    var fromCategory = mapCategoryToPackId(tool.category);
    if (fromCategory) {
      return fromCategory;
    }

    var tags = tool.tags || [];
    for (var i = 0; i < tags.length; i++) {
      var mapped = mapCategoryToPackId(tags[i]);
      if (mapped) {
        return mapped;
      }
      var normalized = normalizePackId(tags[i]);
      if (normalized === "ad" || normalized === "eventlog" || normalized === "system" || normalized === "fs" || normalized === "email" || normalized === "testimox" || normalized === "reviewer-setup") {
        return normalized;
      }
    }

    var name = (tool.name || "").toLowerCase();
    if (name.indexOf("ad_") === 0) return "ad";
    if (name.indexOf("eventlog_") === 0) return "eventlog";
    if (name.indexOf("system_") === 0 || name.indexOf("wsl_") === 0) return "system";
    if (name.indexOf("fs_") === 0) return "fs";
    if (name.indexOf("email_") === 0) return "email";
    if (name.indexOf("testimox_") === 0) return "testimox";
    if (name.indexOf("reviewer_setup_") === 0) return "reviewer-setup";

    return "other";
  }

  function packDisplayName(packId) {
    var tools = state.options.tools || [];
    for (var i = 0; i < tools.length; i++) {
      var tool = tools[i];
      if (!tool || !tool.packId || !tool.packName) {
        continue;
      }
      if (normalizePackId(tool.packId) === normalizePackId(packId)) {
        return tool.packName;
      }
    }

    var pack = findPackById(packId);
    if (pack && pack.name) {
      return pack.name;
    }

    if (packId === "ad") return "Active Directory";
    if (packId === "eventlog") return "Event Log";
    if (packId === "system") return "System";
    if (packId === "fs") return "File System";
    if (packId === "email") return "Email";
    if (packId === "testimox") return "TestimoX";
    if (packId === "reviewer-setup") return "Reviewer Setup";
    return "Other";
  }

  function ensureAccordionState(packId) {
    if (!Object.prototype.hasOwnProperty.call(state.expandedToolPacks, packId)) {
      state.expandedToolPacks[packId] = false;
    }
    return state.expandedToolPacks[packId] === true;
  }

