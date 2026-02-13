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
      toolTimeoutSeconds: (byId("optAutonomyToolTimeout").value || "").trim()
    });
  }

  byId("optAutonomyMaxRounds").addEventListener("change", postAutonomySettings);
  byId("optAutonomyTurnTimeout").addEventListener("change", postAutonomySettings);
  byId("optAutonomyToolTimeout").addEventListener("change", postAutonomySettings);
  byId("optAutonomyParallel").addEventListener("change", postAutonomySettings);
  byId("btnAutonomyReset").addEventListener("click", function() {
    post("reset_autonomy");
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

  function resolveWheelEventTargetElement(e) {
    if (!e || !e.target) {
      return null;
    }

    var target = e.target;
    if (target.nodeType === 1) {
      return target;
    }

    if (target.nodeType === 3 && target.parentElement) {
      return target.parentElement;
    }

    if (target.nodeType === 9 && target.activeElement) {
      return target.activeElement;
    }

    return target.parentElement || null;
  }

  function eventPathContainsSelector(e, selector) {
    if (!e || typeof e.composedPath !== "function") {
      return null;
    }

    var path = e.composedPath();
    for (var i = 0; i < path.length; i++) {
      var node = path[i];
      if (!node || node.nodeType !== 1 || typeof node.matches !== "function") {
        continue;
      }
      if (node.matches(selector)) {
        return node;
      }
    }

    return null;
  }

  function handleWheelInput(e, deltaY) {
    if (e && e.__ixWheelProcessed === true) {
      wheelDiag.counters.duplicates++;
      recordWheelDiag("duplicate", { deltaY: Number(deltaY) });
      return;
    }

    if (e) {
      e.__ixWheelProcessed = true;
    }

    var targetEl = resolveWheelEventTargetElement(e);
    var inTranscript = targetEl && targetEl.closest ? targetEl.closest("#transcript") : null;
    var inOptions = targetEl && targetEl.closest ? targetEl.closest(".options-panel") : null;
    var inSelect = targetEl && targetEl.closest ? targetEl.closest(".ix-select-menu") : null;
    var inDataView = targetEl && targetEl.closest ? targetEl.closest(".data-view-panel") : null;

    // WebView2 can sometimes produce non-element or unusual wheel targets.
    // Fall back to composedPath to avoid dropping valid wheel input.
    if (!inTranscript) {
      inTranscript = eventPathContainsSelector(e, "#transcript");
    }
    if (!inOptions) {
      inOptions = eventPathContainsSelector(e, ".options-panel");
    }
    if (!inSelect) {
      inSelect = eventPathContainsSelector(e, ".ix-select-menu");
    }
    if (!inDataView) {
      inDataView = eventPathContainsSelector(e, ".data-view-panel");
    }

    // If no area was detected but we're in the main shell, route to transcript.
    if (!inTranscript && !inOptions && !inSelect && !inDataView && transcript) {
      inTranscript = transcript;
      wheelDiag.counters.fallbackTranscript++;
      recordWheelDiag("fallback_transcript", { deltaY: Number(deltaY) });
    }

    if (!inTranscript && !inOptions && !inSelect && !inDataView) {
      wheelDiag.counters.noZone++;
      recordWheelDiag("no_zone", { deltaY: Number(deltaY) });
      return;
    }

    if (isEditableElement(targetEl) && !inOptions) {
      wheelDiag.counters.skippedEditable++;
      recordWheelDiag("editable_skip", { deltaY: Number(deltaY) });
      return;
    }

    var applied = applyWheelDelta(deltaY, targetEl);
    if (applied) {
      lastNativeWheelAt = Date.now();
      wheelDiag.counters.applied++;
      recordWheelDiag("applied", {
        deltaY: Number(deltaY),
        zone: inDataView ? "dataView" : (inOptions ? "options" : "transcript")
      });
      e.preventDefault();
      return;
    }

    wheelDiag.counters.notApplied++;
    recordWheelDiag("not_applied", {
      deltaY: Number(deltaY),
      zone: inDataView ? "dataView" : (inOptions ? "options" : "transcript")
    });
  }

  function onWheelEvent(e) {
    wheelDiag.counters.nativeWheel++;
    handleWheelInput(e, e.deltaY);
  }

  function onLegacyWheelEvent(e) {
    wheelDiag.counters.nativeLegacyWheel++;
    var deltaY = Number(e.deltaY);
    if (!Number.isFinite(deltaY)) {
      deltaY = -(Number(e.wheelDelta) || 0);
    }
    handleWheelInput(e, deltaY);
  }

  function attachWheelListeners(el) {
    if (!el || typeof el.addEventListener !== "function") {
      return;
    }
    el.addEventListener("wheel", onWheelEvent, { passive: false, capture: true });
    el.addEventListener("mousewheel", onLegacyWheelEvent, { passive: false, capture: true });
  }

  attachWheelListeners(window);
  attachWheelListeners(document);
  attachWheelListeners(document.documentElement);
  attachWheelListeners(document.body);
  attachWheelListeners(transcript);
  attachWheelListeners(optionsBody);
  attachWheelListeners(dataViewBody);
  recordWheelDiag("wheel_listeners_attached", {
    hasWindow: !!window,
    hasDocument: !!document,
    hasTranscript: !!transcript,
    hasOptionsBody: !!optionsBody,
    hasDataViewBody: !!dataViewBody
  });

  transcript.addEventListener("pointerdown", function() {
    transcript.focus();
  });

  document.addEventListener("click", function(e) {
    var copyBtn = e.target.closest(".msg-copy-btn");
    if (!copyBtn) {
      return;
    }

    var idx = copyBtn.getAttribute("data-msg-index");
    if (idx === null) {
      return;
    }

    post("copy_message", { index: idx });
    copyBtn.innerHTML = "<svg width='14' height='14' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2.5' stroke-linecap='round' stroke-linejoin='round'><polyline points='20 6 9 17 4 12'/></svg>";
    setTimeout(function() {
      copyBtn.innerHTML = "<svg width='14' height='14' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'><rect x='9' y='9' width='13' height='13' rx='2'/><path d='M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1'/></svg>";
    }, 1500);
  });

  autoResizePrompt();
  updateStatusVisual(state.status);
  updateWindowControlsState();
  updateMenuState();
  updateComposerState();
  loadSidebarPrefs();
  setSidebarHoverMode(sidebarPrefs.mode);
  setSidebarWidth(sidebarPrefs.width);
  setSidebarCollapsed(sidebarPrefs.collapsed);
  renderOptions();
})();
