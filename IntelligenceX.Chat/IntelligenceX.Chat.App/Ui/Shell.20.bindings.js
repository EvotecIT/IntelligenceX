  dragBar.addEventListener("pointerdown", function(e) {
    if (e.button !== 0) {
      return;
    }
    if (e.target.closest("[data-no-drag],button,input,textarea,a,select")) {
      return;
    }
    post("window_drag");
  });

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
      parallelMode: (byId("optAutonomyParallel").value || "default").trim(),
      turnTimeoutSeconds: (byId("optAutonomyTurnTimeout").value || "").trim(),
      toolTimeoutSeconds: (byId("optAutonomyToolTimeout").value || "").trim(),
      weightedToolRouting: (byId("optAutonomyWeightedRouting").value || "default").trim(),
      maxCandidateTools: (byId("optAutonomyMaxCandidates").value || "").trim()
    });
  }

  byId("optAutonomyMaxRounds").addEventListener("change", postAutonomySettings);
  byId("optAutonomyTurnTimeout").addEventListener("change", postAutonomySettings);
  byId("optAutonomyToolTimeout").addEventListener("change", postAutonomySettings);
  byId("optAutonomyParallel").addEventListener("change", postAutonomySettings);
  byId("optAutonomyWeightedRouting").addEventListener("change", postAutonomySettings);
  byId("optAutonomyMaxCandidates").addEventListener("change", postAutonomySettings);
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

  byId("btnDebugCopyMemory").addEventListener("click", function() {
    if (!normalizeBool(state.options.debugToolsEnabled)) {
      return;
    }
    copyMemoryDiagnosticsToClipboard();
  });

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

    if (normalizeBool(state.cancelable)) {
      post("cancel_turn");
      return;
    }

    var text = (promptEl.value || "").trim();
    if (!text) {
      return;
    }

    post("send", { text: text });
    promptEl.value = "";
    autoResizePrompt();
  });

  promptEl.addEventListener("input", autoResizePrompt);
