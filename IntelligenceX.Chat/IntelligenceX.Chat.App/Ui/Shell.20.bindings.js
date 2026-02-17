  function isNoDragTarget(target) {
    if (!target || !target.closest) {
      return false;
    }
    return !!target.closest("[data-no-drag],button,input,textarea,a,select");
  }

  var nativeTitlebarEnabled = false;
  var titlebarMetricsScheduled = false;

  window.ixSetNativeTitlebarEnabled = function(enabled) {
    nativeTitlebarEnabled = !!enabled;
  };

  function getRectForHost(el) {
    if (!el || !el.getBoundingClientRect) {
      return null;
    }

    var rect = el.getBoundingClientRect();
    var width = Math.round(rect.width);
    var height = Math.round(rect.height);
    if (width <= 0 || height <= 0) {
      return null;
    }

    return {
      x: Math.round(rect.left),
      y: Math.round(rect.top),
      width: width,
      height: height
    };
  }

  function postTitlebarMetricsNow() {
    if (!dragBar) {
      return;
    }

    var titleBarRect = getRectForHost(dragBar);
    if (!titleBarRect) {
      return;
    }

    var noDragRects = [];
    var noDragKeys = {};
    var noDragTargets = dragBar.querySelectorAll("[data-no-drag],button,input,textarea,a,select");
    for (var i = 0; i < noDragTargets.length; i++) {
      var rect = getRectForHost(noDragTargets[i]);
      if (!rect) {
        continue;
      }

      var key = rect.x + ":" + rect.y + ":" + rect.width + ":" + rect.height;
      if (noDragKeys[key]) {
        continue;
      }

      noDragKeys[key] = true;
      noDragRects.push(rect);
    }

    post("window_titlebar_metrics", {
      titleBarRect: titleBarRect,
      noDragRects: noDragRects
    });
  }

  function scheduleTitlebarMetricsPost() {
    if (titlebarMetricsScheduled) {
      return;
    }

    titlebarMetricsScheduled = true;
    requestAnimationFrame(function() {
      titlebarMetricsScheduled = false;
      postTitlebarMetricsNow();
    });
  }

  window.ixPostTitlebarMetrics = function() {
    scheduleTitlebarMetricsPost();
  };

  if (dragBar) {
    scheduleTitlebarMetricsPost();

    window.addEventListener("resize", function() {
      scheduleTitlebarMetricsPost();
    });

    if (typeof ResizeObserver === "function") {
      var titlebarResizeObserver = new ResizeObserver(function() {
        scheduleTitlebarMetricsPost();
      });
      titlebarResizeObserver.observe(dragBar);

      var observedNoDragTargets = dragBar.querySelectorAll("[data-no-drag],button,input,textarea,a,select");
      for (var j = 0; j < observedNoDragTargets.length; j++) {
        titlebarResizeObserver.observe(observedNoDragTargets[j]);
      }
    }

    dragBar.addEventListener("pointerdown", function(e) {
      if (e.button !== 0 || isNoDragTarget(e.target) || nativeTitlebarEnabled) {
        return;
      }

      e.preventDefault();
      post("window_drag");
    });

    dragBar.addEventListener("dblclick", function(e) {
      if (e.button !== 0 || isNoDragTarget(e.target) || nativeTitlebarEnabled) {
        return;
      }
      e.preventDefault();
      post("window_maximize");
    });
  }

  byId("btnWinMin").addEventListener("click", function() { post("window_minimize"); });
  byId("btnWinMax").addEventListener("click", function() { post("window_maximize"); });
  byId("btnWinClose").addEventListener("click", function() { post("window_close"); });

  byId("btnMenu").addEventListener("click", function(e) {
    e.stopPropagation();
    menu.classList.toggle("open");
  });

  byId("menuOptions").addEventListener("click", function() {
    openOptions();
  });

  function createWheelDiagnosticsState() {
    return {
      startedUtc: new Date().toISOString(),
      counters: {
        hostForwarded: 0,
        hostSkippedAfterNative: 0,
        hostPointerWheel: 0,
        hostGlobalWheel: 0,
        nativeWheel: 0,
        nativeLegacyWheel: 0,
        duplicates: 0,
        applied: 0,
        notApplied: 0,
        skippedEditable: 0,
        noZone: 0,
        fallbackTranscript: 0,
        scriptError: 0
      },
      scriptErrors: [],
      lastEvents: []
    };
  }

  var wheelDiag = createWheelDiagnosticsState();

  function recordWheelDiag(kind, payload) {
    if (!kind) {
      return;
    }

    if (kind === "host_forward") {
      wheelDiag.counters.hostForwarded++;
    } else if (kind === "host_skip_after_native") {
      wheelDiag.counters.hostSkippedAfterNative++;
    } else if (kind === "host_pointer_wheel") {
      wheelDiag.counters.hostPointerWheel++;
    } else if (kind === "host_global_wheel") {
      wheelDiag.counters.hostGlobalWheel++;
    } else if (kind === "script_error") {
      wheelDiag.counters.scriptError++;
    }

    var eventItem = {
      t: new Date().toISOString(),
      kind: String(kind)
    };
    if (payload && typeof payload === "object") {
      for (var key in payload) {
        if (Object.prototype.hasOwnProperty.call(payload, key)) {
          eventItem[key] = payload[key];
        }
      }
    }

    wheelDiag.lastEvents.push(eventItem);
    if (wheelDiag.lastEvents.length > 32) {
      wheelDiag.lastEvents.shift();
    }
  }

  function buildWheelDiagnosticsText() {
    var lines = [];
    lines.push("IntelligenceX Wheel Diagnostics");
    lines.push("started_utc: " + wheelDiag.startedUtc);
    lines.push("now_utc: " + new Date().toISOString());
    lines.push("");
    lines.push("counters:");
    var counters = wheelDiag.counters;
    lines.push("  hostForwarded: " + counters.hostForwarded);
    lines.push("  hostSkippedAfterNative: " + counters.hostSkippedAfterNative);
    lines.push("  hostPointerWheel: " + counters.hostPointerWheel);
    lines.push("  hostGlobalWheel: " + counters.hostGlobalWheel);
    lines.push("  nativeWheel: " + counters.nativeWheel);
    lines.push("  nativeLegacyWheel: " + counters.nativeLegacyWheel);
    lines.push("  duplicates: " + counters.duplicates);
    lines.push("  applied: " + counters.applied);
    lines.push("  notApplied: " + counters.notApplied);
    lines.push("  skippedEditable: " + counters.skippedEditable);
    lines.push("  noZone: " + counters.noZone);
    lines.push("  fallbackTranscript: " + counters.fallbackTranscript);
    lines.push("  scriptError: " + counters.scriptError);
    lines.push("");
    lines.push("script_errors:");
    for (var s = 0; s < wheelDiag.scriptErrors.length; s++) {
      lines.push("  - " + JSON.stringify(wheelDiag.scriptErrors[s]));
    }
    lines.push("");
    lines.push("last_events:");
    for (var i = 0; i < wheelDiag.lastEvents.length; i++) {
      lines.push("  - " + JSON.stringify(wheelDiag.lastEvents[i]));
    }
    return lines.join("\n");
  }

  window.ixWheelDiagRecord = function(kind, payload) {
    recordWheelDiag(kind, payload);
  };

  window.ixGetWheelDiagnostics = function() {
    return buildWheelDiagnosticsText();
  };

  window.addEventListener("error", function(ev) {
    var payload = {
      message: ev && ev.message ? String(ev.message) : "",
      source: ev && ev.filename ? String(ev.filename) : "",
      line: ev && typeof ev.lineno === "number" ? ev.lineno : 0,
      col: ev && typeof ev.colno === "number" ? ev.colno : 0
    };
    wheelDiag.scriptErrors.push(payload);
    if (wheelDiag.scriptErrors.length > 10) {
      wheelDiag.scriptErrors.shift();
    }
    recordWheelDiag("script_error", payload);
  });

  window.addEventListener("unhandledrejection", function(ev) {
    var reason = ev && typeof ev.reason !== "undefined" ? String(ev.reason) : "";
    var payload = { message: "unhandledrejection", reason: reason };
    wheelDiag.scriptErrors.push(payload);
    if (wheelDiag.scriptErrors.length > 10) {
      wheelDiag.scriptErrors.shift();
    }
    recordWheelDiag("script_error", payload);
  });

  var menuWheelDiagnostics = byId("menuWheelDiagnostics");
  function copyWheelDiagnosticsToClipboard() {
    post("omd_copy", { text: buildWheelDiagnosticsText() });
  }

  function buildMemoryDiagnosticsText() {
    var lines = [];
    lines.push("IntelligenceX Memory Diagnostics");
    var snap = state.options.memoryDebug || null;
    if (!snap) {
      lines.push("status: unavailable");
      return lines.join("\n");
    }

    lines.push("updated_local: " + (snap.updatedLocal || ""));
    lines.push("");
    lines.push("stats:");
    lines.push("  facts: " + (snap.availableFacts || 0));
    lines.push("  candidates: " + (snap.candidateFacts || 0));
    lines.push("  selected: " + (snap.selectedFacts || 0));
    lines.push("  user_tokens: " + (snap.userTokenCount || 0));
    lines.push("  top_score: " + (typeof snap.topScore === "number" ? snap.topScore.toFixed(3) : "0.000"));
    lines.push("  top_similarity: " + (typeof snap.topSemanticSimilarity === "number" ? snap.topSemanticSimilarity.toFixed(3) : "0.000"));
    lines.push("  avg_selected_similarity: " + (typeof snap.averageSelectedSimilarity === "number" ? snap.averageSelectedSimilarity.toFixed(3) : "0.000"));
    lines.push("  avg_selected_relevance: " + (typeof snap.averageSelectedRelevance === "number" ? snap.averageSelectedRelevance.toFixed(3) : "0.000"));
    lines.push("  cache_entries: " + (snap.cacheEntries || 0));
    return lines.join("\n");
  }

  function copyMemoryDiagnosticsToClipboard() {
    post("omd_copy", { text: buildMemoryDiagnosticsText() });
  }

  if (menuWheelDiagnostics) {
    menuWheelDiagnostics.addEventListener("click", function() {
      copyWheelDiagnosticsToClipboard();
      menu.classList.remove("open");
    });
  }

  byId("btnSidebarNewChat").addEventListener("click", function() {
    post("new_conversation");
  });

  byId("btnSidebarToggle").addEventListener("click", function() {
    toggleSidebarCollapsed();
  });

  chatSidebar.addEventListener("mouseenter", function() {
    setSidebarHoverOpen(true);
  });

  chatSidebar.addEventListener("mouseleave", function() {
    setSidebarHoverOpen(false);
  });

  var pendingDeleteTimer = 0;

  function clearPendingDelete() {
    if (pendingDeleteTimer) {
      clearTimeout(pendingDeleteTimer);
      pendingDeleteTimer = 0;
    }
    var armed = chatSidebarList.querySelectorAll(".chat-sidebar-item-delete.armed");
    for (var i = 0; i < armed.length; i++) {
      armed[i].classList.remove("armed");
    }
  }

  byId("chatSidebarList").addEventListener("click", function(e) {
    var deleteBtn = e.target.closest(".chat-sidebar-item-delete");
    if (deleteBtn) {
      e.stopPropagation();
      var delId = deleteBtn.dataset.conversationId || "";
      if (!delId) {
        return;
      }

      if (deleteBtn.classList.contains("armed")) {
        clearPendingDelete();
        post("delete_conversation", { id: delId });
        return;
      }

      clearPendingDelete();
      deleteBtn.classList.add("armed");
      pendingDeleteTimer = setTimeout(clearPendingDelete, 3000);
      return;
    }

    clearPendingDelete();

    var button = e.target.closest(".chat-sidebar-item");
    if (!button) {
      return;
    }
    var id = button.dataset.conversationId || "";
    if (!id) {
      return;
    }
    post("switch_conversation", { id: id });
  });

  sidebarResizeHandle.addEventListener("pointerdown", function(e) {
    if (e.button !== 0) {
      return;
    }

    beginSidebarResize();
    updateSidebarResize(e.clientX);
    sidebarResizeHandle.setPointerCapture(e.pointerId);
    e.preventDefault();
  });

  sidebarResizeHandle.addEventListener("pointermove", function(e) {
    if (!sidebarResizeActive) {
      return;
    }

    updateSidebarResize(e.clientX);
    e.preventDefault();
  });

  sidebarResizeHandle.addEventListener("pointerup", function(e) {
    endSidebarResize();
    if (sidebarResizeHandle.hasPointerCapture(e.pointerId)) {
      sidebarResizeHandle.releasePointerCapture(e.pointerId);
    }
  });

  sidebarResizeHandle.addEventListener("pointercancel", function(e) {
    endSidebarResize();
    if (sidebarResizeHandle.hasPointerCapture(e.pointerId)) {
      sidebarResizeHandle.releasePointerCapture(e.pointerId);
    }
  });

  byId("btnOptionsClose").addEventListener("click", closeOptions);
  optionsBackdrop.addEventListener("click", closeOptions);

  optionsBody.addEventListener("scroll", function() {
    if (openCustomSelect) {
      closeOpenCustomSelect();
    }
  });

  var optionsTabs = byId("optionsTabs");
  if (optionsTabs) {
    optionsTabs.addEventListener("click", function(e) {
      var tab = e.target.closest(".options-tab");
      if (!tab || !tab.dataset.tab) {
        return;
      }
      switchOptionsTab(tab.dataset.tab);
    });
  }

  var debugProfileBadge = byId("optDebugProfileBadge");
  if (debugProfileBadge) {
    function openProfileFromDebugBadge() {
      switchOptionsTab("profile");
      var select = byId("optProfileSelect");
      if (!select) {
        return;
      }

      ensureCustomSelect("optProfileSelect");
      var button = select._ixButton || null;
      var wrap = select._ixWrap || null;
      if (button) {
        button.focus();
        if (!wrap || !wrap.classList.contains("open")) {
          button.click();
        }
        return;
      }

      select.focus();
    }

    debugProfileBadge.addEventListener("click", function() {
      openProfileFromDebugBadge();
    });

    debugProfileBadge.addEventListener("keydown", function(e) {
      if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        openProfileFromDebugBadge();
      }
    });
  }

  byId("optTimeMode").addEventListener("change", function(e) {
    post("set_time_mode", { value: e.target.value });
  });

  byId("optExportSaveMode").addEventListener("change", function(e) {
    post("set_export_save_mode", { value: e.target.value || "ask" });
  });

  byId("optExportDefaultFormat").addEventListener("change", function(e) {
    post("set_export_default_format", { value: e.target.value || "xlsx" });
  });

  byId("btnClearExportLastDirectory").addEventListener("click", function() {
    post("clear_export_last_directory");
  });

  function postAutonomySettings() {
    post("set_autonomy", {
      maxToolRounds: (byId("optAutonomyMaxRounds").value || "").trim(),
      parallelMode: (byId("optAutonomyParallel").value || "auto").trim(),
      planExecuteReviewLoop: (byId("optAutonomyPlanReview").value || "default").trim(),
      maxReviewPasses: (byId("optAutonomyMaxReviewPasses").value || "").trim(),
      modelHeartbeatSeconds: (byId("optAutonomyModelHeartbeat").value || "").trim(),
      turnTimeoutSeconds: (byId("optAutonomyTurnTimeout").value || "").trim(),
      toolTimeoutSeconds: (byId("optAutonomyToolTimeout").value || "").trim(),
      weightedToolRouting: (byId("optAutonomyWeightedRouting").value || "default").trim(),
      maxCandidateTools: (byId("optAutonomyMaxCandidates").value || "").trim()
    });
  }

  byId("optAutonomyMaxRounds").addEventListener("change", postAutonomySettings);
  byId("optAutonomyPlanReview").addEventListener("change", postAutonomySettings);
  byId("optAutonomyMaxReviewPasses").addEventListener("change", postAutonomySettings);
  byId("optAutonomyModelHeartbeat").addEventListener("change", postAutonomySettings);
  byId("optAutonomyTurnTimeout").addEventListener("change", postAutonomySettings);
  byId("optAutonomyToolTimeout").addEventListener("change", postAutonomySettings);
  byId("optAutonomyParallel").addEventListener("change", postAutonomySettings);
  byId("optAutonomyWeightedRouting").addEventListener("change", postAutonomySettings);
  byId("optAutonomyMaxCandidates").addEventListener("change", postAutonomySettings);
  var proactiveModeToggle = byId("optProactiveMode");
  if (proactiveModeToggle) {
    proactiveModeToggle.addEventListener("change", function(e) {
      post("set_proactive_mode", { enabled: e.target.checked === true });
    });
  }
  var queueAutoDispatchToggle = byId("optQueueAutoDispatch");
  if (queueAutoDispatchToggle) {
    queueAutoDispatchToggle.addEventListener("change", function(e) {
      post("set_queue_auto_dispatch", { enabled: e.target.checked === true });
    });
  }
  var runNextQueuedButton = byId("btnRunNextQueuedTurn");
  if (runNextQueuedButton) {
    runNextQueuedButton.addEventListener("click", function() {
      post("run_next_queued");
    });
  }
  var clearQueuedButton = byId("btnClearQueuedTurns");
  if (clearQueuedButton) {
    clearQueuedButton.addEventListener("click", function() {
      post("clear_queued_turns");
    });
  }
  byId("btnAutonomyReset").addEventListener("click", function() {
    post("reset_autonomy");
  });

  byId("optMemoryEnabled").addEventListener("change", function(e) {
    post("set_memory_enabled", { enabled: e.target.checked === true });
  });

  byId("btnAddMemoryNote").addEventListener("click", function() {
    var note = (byId("optMemoryNote").value || "").trim();
    if (!note) {
      return;
    }

    post("add_memory_note", { text: note, weight: "3" });
    byId("optMemoryNote").value = "";
  });

  byId("optMemoryNote").addEventListener("keydown", function(e) {
    if (e.key !== "Enter") {
      return;
    }

    e.preventDefault();
    byId("btnAddMemoryNote").click();
  });

  byId("btnClearMemory").addEventListener("click", function() {
    post("clear_memory");
  });

  byId("optEnableDebugTools").addEventListener("change", function(e) {
    setDebugToolsEnabled(e.target.checked === true, true);
    renderDebugPanel();
  });

  byId("btnDebugToggleEngine").addEventListener("click", function() {
    if (!normalizeBool(state.options.debugToolsEnabled)) {
      return;
    }
    post("toggle_debug");
  });

  byId("btnDebugCopyWheel").addEventListener("click", function() {
    if (!normalizeBool(state.options.debugToolsEnabled)) {
      return;
    }
    copyWheelDiagnosticsToClipboard();
  });

  var btnDebugCopyMemory = byId("btnDebugCopyMemory");
  if (btnDebugCopyMemory) {
    btnDebugCopyMemory.addEventListener("click", function() {
      if (!normalizeBool(state.options.debugToolsEnabled)) {
        return;
      }
      copyMemoryDiagnosticsToClipboard();
    });
  }

  var btnDebugRecomputeMemory = byId("btnDebugRecomputeMemory");
  if (btnDebugRecomputeMemory) {
    btnDebugRecomputeMemory.addEventListener("click", function() {
      if (!normalizeBool(state.options.debugToolsEnabled)) {
        return;
      }
      post("debug_memory_recompute");
    });
  }

  byId("btnDebugCopyStartupLog").addEventListener("click", function() {
    if (!normalizeBool(state.options.debugToolsEnabled)) {
      return;
    }
    post("debug_copy_startup_log");
  });

  byId("btnDebugRestartSidecar").addEventListener("click", function() {
    if (!normalizeBool(state.options.debugToolsEnabled)) {
      return;
    }
    post("debug_restart_runtime");
  });

  byId("optSidebarMode").addEventListener("change", function(e) {
    var mode = (e.target.value || "manual");
    setSidebarHoverMode(mode);
    if (mode === "hover" && !sidebarPrefs.collapsed) {
      setSidebarCollapsed(true);
    }
  });

  var profileDraftTimer = 0;

  function getProfileApplyMode() {
    var select = byId("optProfileApplyMode");
    var value = select ? select.value : state.options.profileApplyMode;
    return normalizeProfileApplyMode(value);
  }

  function setProfileApplyMode(mode) {
    var normalized = normalizeProfileApplyMode(mode);
    state.options.profileApplyMode = normalized;
    writeStorage("ixchat.profile.applyMode", normalized);
    var select = byId("optProfileApplyMode");
    if (select && select.value !== normalized) {
      select.value = normalized;
      syncCustomSelect(select);
    }
    renderProfileScopeHint();
  }

  function postLiveProfileUpdate(values) {
    var payload = Object.assign({
      scope: getProfileApplyMode()
    }, values || {});
    post("apply_profile_update", payload);
  }

  function flushLiveTextProfileUpdate() {
    profileDraftTimer = 0;
    postLiveProfileUpdate({
      userName: byId("optUserName").value || "",
      persona: byId("optPersona").value || ""
    });
  }

  function queueLiveTextProfileUpdate() {
    if (profileDraftTimer) {
      clearTimeout(profileDraftTimer);
    }
    profileDraftTimer = setTimeout(flushLiveTextProfileUpdate, 420);
  }

  byId("optProfileApplyMode").addEventListener("change", function(e) {
    setProfileApplyMode(e.target.value || "session");
  });
  setProfileApplyMode(state.options.profileApplyMode || "session");

  byId("optTheme").addEventListener("change", function(e) {
    postLiveProfileUpdate({ theme: e.target.value || "default" });
  });

  byId("optUserName").addEventListener("input", queueLiveTextProfileUpdate);
  byId("optUserName").addEventListener("blur", flushLiveTextProfileUpdate);
  byId("optPersona").addEventListener("input", queueLiveTextProfileUpdate);
  byId("optPersona").addEventListener("blur", flushLiveTextProfileUpdate);

  byId("btnSaveProfile").addEventListener("click", function() {
    post("save_profile", {
      userName: byId("optUserName").value || "",
      persona: byId("optPersona").value || "",
      theme: byId("optTheme").value || "default"
    });
  });

  byId("btnRestartOnboarding").addEventListener("click", function() {
    post("restart_onboarding");
    closeOptions();
  });

  byId("btnSwitchProfile").addEventListener("click", function() {
    var name = (byId("optProfileSelect").value || "").trim();
    if (!name) {
      return;
    }
    post("switch_profile", { name: name });
  });

  byId("btnCreateProfile").addEventListener("click", function() {
    var input = byId("optNewProfile");
    var name = (input.value || "").trim();
    if (!name) {
      return;
    }
    post("switch_profile", { name: name });
    input.value = "";
  });

  function normalizeLocalTransportValue(value) {
    var normalized = String(value || "").trim().toLowerCase();
    if (normalized === "compatible-http" || normalized === "compatiblehttp" || normalized === "http" || normalized === "local" || normalized === "ollama" || normalized === "lmstudio" || normalized === "lm-studio") {
      return "compatible-http";
    }
    if (normalized === "copilot-cli" || normalized === "copilot" || normalized === "github-copilot" || normalized === "githubcopilot") {
      return "copilot-cli";
    }
    return "native";
  }

  function transportUsesCompatibleHttp(transport) {
    return normalizeLocalTransportValue(transport) === "compatible-http";
  }

  function applyCompatiblePresetSelection(preset) {
    var key = String(preset || "").toLowerCase();
    var transport = byId("optLocalTransport");
    var baseUrl = byId("optLocalBaseUrl");
    var modelInput = byId("optLocalModelInput");
    var baseUrlRow = byId("optLocalBaseUrlRow");
    var apiKeyRow = byId("optLocalApiKeyRow");
    var apiKeyHint = byId("optLocalApiKeyHint");
    var apiKeyInput = byId("optLocalApiKey");

    if (!transport || !baseUrl) {
      return;
    }

    transport.value = "compatible-http";
    syncCustomSelect(transport);

    if (key === "lmstudio") {
      baseUrl.value = "http://127.0.0.1:1234/v1";
    } else if (key === "ollama") {
      baseUrl.value = "http://127.0.0.1:11434";
    } else if (key === "copilot") {
      baseUrl.value = "https://api.githubcopilot.com/v1";
    }

    if (baseUrlRow) {
      baseUrlRow.hidden = false;
    }
    baseUrl.disabled = false;
    if (apiKeyRow) {
      apiKeyRow.hidden = false;
    }
    if (apiKeyHint) {
      apiKeyHint.hidden = false;
    }
    if (apiKeyInput) {
      apiKeyInput.disabled = false;
    }
    if (modelInput) {
      modelInput.value = "";
    }
  }

  function applyLocalProviderSettings(forceRefresh, clearApiKey) {
    var transport = normalizeLocalTransportValue(byId("optLocalTransport").value || "native");
    var baseUrl = transportUsesCompatibleHttp(transport)
      ? (byId("optLocalBaseUrl").value || "").trim()
      : "";
    var model = (byId("optLocalModelInput").value || "").trim();
    var shouldClearApiKey = clearApiKey === true;
    var apiKey = transportUsesCompatibleHttp(transport)
      ? (byId("optLocalApiKey").value || "").trim()
      : "";
    if (shouldClearApiKey) {
      apiKey = "";
    }
    post("apply_local_provider", {
      transport: transport,
      baseUrl: baseUrl,
      model: model,
      apiKey: apiKey,
      clearApiKey: shouldClearApiKey,
      forceRefresh: forceRefresh !== false
    });
  }

  function hasPendingLocalProviderChanges() {
    var local = state.options.localModel || {};
    var currentTransport = normalizeLocalTransportValue(local.transport || "native");
    var currentBaseUrl = transportUsesCompatibleHttp(currentTransport)
      ? String(local.baseUrl || "").trim().toLowerCase()
      : "";
    var currentModel = String(local.model || "").trim();

    var draftTransport = normalizeLocalTransportValue(byId("optLocalTransport").value || "native");
    var draftBaseUrl = transportUsesCompatibleHttp(draftTransport)
      ? (byId("optLocalBaseUrl").value || "").trim().toLowerCase()
      : "";
    var draftModel = (byId("optLocalModelInput").value || "").trim();
    var draftApiKey = transportUsesCompatibleHttp(draftTransport)
      ? (byId("optLocalApiKey").value || "").trim()
      : "";

    return draftTransport !== currentTransport
      || draftBaseUrl !== currentBaseUrl
      || draftModel !== currentModel
      || draftApiKey.length > 0;
  }

  byId("optLocalTransport").addEventListener("change", function(e) {
    var next = normalizeLocalTransportValue(e.target.value || "native");
    var isCompatible = transportUsesCompatibleHttp(next);
    e.target.value = next;
    syncCustomSelect(e.target);

    var baseRow = byId("optLocalBaseUrlRow");
    var baseInput = byId("optLocalBaseUrl");
    var apiKeyRow = byId("optLocalApiKeyRow");
    var apiKeyHint = byId("optLocalApiKeyHint");
    var apiKeyInput = byId("optLocalApiKey");
    var autoDetectButton = byId("btnAutoDetectLocalRuntime");
    if (baseRow) {
      baseRow.hidden = !isCompatible;
    }
    if (baseInput) {
      baseInput.disabled = !isCompatible;
      if (isCompatible && !(baseInput.value || "").trim()) {
        var runtimeDetection = (((state.options || {}).localModel || {}).runtimeDetection || {});
        var lmStudioAvailable = runtimeDetection.lmStudioAvailable === true;
        var ollamaAvailable = runtimeDetection.ollamaAvailable === true;
        if (lmStudioAvailable && !ollamaAvailable) {
          baseInput.value = "http://127.0.0.1:1234/v1";
        } else if (ollamaAvailable && !lmStudioAvailable) {
          baseInput.value = "http://127.0.0.1:11434";
        }
      }
    }
    if (apiKeyRow) {
      apiKeyRow.hidden = !isCompatible;
    }
    if (apiKeyHint) {
      apiKeyHint.hidden = !isCompatible;
    }
    if (apiKeyInput) {
      apiKeyInput.disabled = !isCompatible;
    }
    if (autoDetectButton) {
      autoDetectButton.hidden = !isCompatible;
    }
  });

  byId("optLocalModelSelect").addEventListener("change", function(e) {
    var selected = (e.target.value || "").trim();
    var modelInput = byId("optLocalModelInput");
    var modelInputRow = byId("optLocalModelInputRow");
    if (!selected) {
      if (modelInput) {
        modelInput.disabled = false;
      }
      if (modelInputRow) {
        modelInputRow.hidden = false;
      }
      return;
    }
    if (modelInput) {
      modelInput.value = selected;
      modelInput.disabled = true;
    }
    if (modelInputRow) {
      modelInputRow.hidden = true;
    }
  });

  byId("optLocalModelFilter").addEventListener("input", function(e) {
    writeStorage(runtimeModelFilterStorageKey(), (e.target.value || "").trim());
    renderLocalModelOptions();
  });

  byId("btnToggleLocalAdvancedRuntime").addEventListener("click", function() {
    setRuntimeAdvancedOpen(!isRuntimeAdvancedOpen());
  });

  byId("btnUseOpenAiRuntime").addEventListener("click", function() {
    var transport = byId("optLocalTransport");
    transport.value = "native";
    syncCustomSelect(transport);
    applyLocalProviderSettings(true);
  });

  byId("btnConnectLmStudio").addEventListener("click", function() {
    applyCompatiblePresetSelection("lmstudio");
    applyLocalProviderSettings(true);
  });

  byId("btnUseCopilotRuntime").addEventListener("click", function() {
    var transport = byId("optLocalTransport");
    if (transport) {
      transport.value = "copilot-cli";
      syncCustomSelect(transport);
      transport.dispatchEvent(new Event("change"));
    }
    applyLocalProviderSettings(true);
  });

  byId("btnAutoDetectLocalRuntime").addEventListener("click", function() {
    post("auto_detect_local_runtime", { forceRefresh: true });
  });

  byId("btnLocalPresetOllama").addEventListener("click", function() {
    applyCompatiblePresetSelection("ollama");
    applyLocalProviderSettings(true);
  });

  byId("btnLocalPresetLmStudio").addEventListener("click", function() {
    applyCompatiblePresetSelection("lmstudio");
    applyLocalProviderSettings(true);
  });

  byId("btnLocalPresetCopilot").addEventListener("click", function() {
    applyCompatiblePresetSelection("copilot");
    applyLocalProviderSettings(true);
  });

  byId("btnRefreshModels").addEventListener("click", function() {
    if (hasPendingLocalProviderChanges()) {
      applyLocalProviderSettings(true);
      return;
    }
    post("refresh_models", { forceRefresh: true });
  });

  byId("btnApplyLocalProvider").addEventListener("click", function() {
    applyLocalProviderSettings(true);
  });

  byId("btnClearLocalApiKey").addEventListener("click", function() {
    if (!transportUsesCompatibleHttp(byId("optLocalTransport").value || "native")) {
      return;
    }
    byId("optLocalApiKey").value = "";
    applyLocalProviderSettings(true, true);
  });

  var btnNewConversation = byId("btnNewConversation");
  if (btnNewConversation) {
    btnNewConversation.addEventListener("click", function() {
      post("new_conversation");
      closeOptions();
    });
  }

  var optConversations = byId("optConversations");
  if (optConversations) {
    optConversations.addEventListener("click", function(e) {
      var button = e.target.closest(".options-conversation-switch");
      if (!button) {
        return;
      }

      var action = button.dataset.action || "switch";
      var id = button.dataset.conversationId || "";
      if (!id) {
        return;
      }

      if (action === "switch") {
        post("switch_conversation", { id: id });
        return;
      }

      if (action === "rename") {
        var current = (button.dataset.currentTitle || "").trim();
        var next = window.prompt("Conversation title:", current);
        if (next == null) {
          return;
        }
        post("rename_conversation", { id: id, title: (next || "").trim() });
        return;
      }

      if (action === "delete") {
        if (button.classList.contains("armed")) {
          button.classList.remove("armed");
          button.textContent = "Delete";
          post("delete_conversation", { id: id });
          return;
        }

        var prevArmed = optConversations.querySelectorAll(".options-conversation-switch.armed");
        for (var a = 0; a < prevArmed.length; a++) {
          prevArmed[a].classList.remove("armed");
          prevArmed[a].textContent = "Delete";
        }

        button.classList.add("armed");
        button.textContent = "Confirm?";
        setTimeout(function() {
          if (button.classList.contains("armed")) {
            button.classList.remove("armed");
            button.textContent = "Delete";
          }
        }, 3000);
      }
    });
  }

  byId("optNewProfile").addEventListener("keydown", function(e) {
    if (e.key === "Enter") {
      e.preventDefault();
      byId("btnCreateProfile").click();
    }
  });

  byId("optToolFilter").addEventListener("input", function(e) {
    state.options.toolFilter = e.target.value || "";
    renderTools();
  });

  menu.addEventListener("click", function(e) {
    var button = e.target.closest("button[data-cmd]");
    if (!button || button.disabled) {
      return;
    }
    post(button.getAttribute("data-cmd"));
    menu.classList.remove("open");
  });

  document.addEventListener("click", function(e) {
    if (openCustomSelect && !openCustomSelect.contains(e.target)) {
      closeOpenCustomSelect();
    }

    if (!menu.classList.contains("open")) {
      return;
    }
    if (!menu.contains(e.target) && e.target !== byId("btnMenu")) {
      menu.classList.remove("open");
    }
  });

  document.addEventListener("keydown", function(e) {
    if (e.key === "Escape") {
      if (document.body.classList.contains("data-view-open") && window.ixCloseDataView) {
        window.ixCloseDataView();
        return;
      }
      if (openCustomSelect) {
        closeOpenCustomSelect();
        return;
      }
      if (menu.classList.contains("open")) {
        menu.classList.remove("open");
        return;
      }
      if (document.body.classList.contains("options-open")) {
        closeOptions();
        return;
      }
    }

    if (e.key === "Enter" && !e.shiftKey && document.activeElement === promptEl) {
      if (byId("btnSend").disabled) {
        return;
      }
      e.preventDefault();
      byId("btnSend").click();
      return;
    }

    if (handleDataViewNavKey(e)) {
      e.preventDefault();
      return;
    }

    if (handleOptionsNavKey(e)) {
      e.preventDefault();
      return;
    }

    if (handleTranscriptNavKey(e)) {
      e.preventDefault();
    }
  });

  byId("btnSend").addEventListener("click", function() {
    if (byId("btnSend").disabled) {
      return;
    }

    var text = (promptEl.value || "").trim();
    if (normalizeBool(state.cancelable)) {
      if (text) {
        post("send", { text: text });
        promptEl.value = "";
        autoResizePrompt();
        updateComposerState();
        return;
      }

      post("cancel_turn");
      return;
    }

    if (!text) {
      return;
    }

    post("send", { text: text });
    promptEl.value = "";
    autoResizePrompt();
    updateComposerState();
  });

  promptEl.addEventListener("input", function() {
    autoResizePrompt();
    updateComposerState();
  });
