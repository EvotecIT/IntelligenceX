  var MAX_NATIVE_ACCOUNT_SLOTS = 32;

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
  var menuExportTranscriptForensics = byId("menuExportTranscriptForensics");
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

  if (menuExportTranscriptForensics) {
    menuExportTranscriptForensics.addEventListener("click", function() {
      if (!normalizeBool(state.options.debugToolsEnabled)) {
        return;
      }
      post("debug_export_transcript_forensics");
      menu.classList.remove("open");
    });
  }

  byId("btnSidebarNewChat").addEventListener("click", function() {
    setPendingConversationSelection("");
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

  function renderConversationSelectionState() {
    renderSidebarConversations();
    renderOptionsConversations();
  }

  function setPendingConversationSelection(conversationId) {
    state.options.activeConversationId = String(conversationId || "").trim();
    renderConversationSelectionState();
  }

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
    setPendingConversationSelection(id);
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

  byId("optExportVisualThemeMode").addEventListener("change", function(e) {
    post("set_export_visual_theme_mode", { value: e.target.value || "preserve_ui_theme" });
  });

  var docxVisualMaxWidthInput = byId("optExportDocxVisualMaxWidthPx");
  if (docxVisualMaxWidthInput) {
    docxVisualMaxWidthInput.addEventListener("change", function(e) {
      post("set_export_docx_visual_max_width", { value: String(e.target.value || "").trim() });
    });
  }

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
  ensureCustomSelect("optSchedulerMaintenanceDay");
  ensureCustomSelect("optSchedulerScopePack");
  ensureCustomSelect("optSchedulerScopeThread");
  ensureCustomSelect("optSchedulerMaintenancePackId");
  ensureCustomSelect("optSchedulerMaintenanceThreadId");

  var schedulerRefreshButton = byId("btnSchedulerRefresh");
  if (schedulerRefreshButton) {
    schedulerRefreshButton.addEventListener("click", function() {
      post("scheduler_refresh", {
        threadId: (byId("optSchedulerScopeThread").value || "").trim()
      });
    });
  }
  var schedulerScopeTogglePackMuteButton = byId("btnSchedulerScopeTogglePackMute");
  if (schedulerScopeTogglePackMuteButton) {
    schedulerScopeTogglePackMuteButton.addEventListener("click", function() {
      var packId = String(schedulerScopeTogglePackMuteButton.dataset.packId || "").trim();
      if (!packId) {
        return;
      }

      var blocked = String(schedulerScopeTogglePackMuteButton.dataset.blocked || "").trim().toLowerCase() === "true";
      post("scheduler_set_pack_block", {
        packId: packId,
        blocked: !blocked
      });
    });
  }
  var schedulerScopeTempPackMuteButton = byId("btnSchedulerScopeTempPackMute");
  if (schedulerScopeTempPackMuteButton) {
    schedulerScopeTempPackMuteButton.addEventListener("click", function() {
      var packId = String(schedulerScopeTempPackMuteButton.dataset.packId || "").trim();
      if (!packId) {
        return;
      }

      post("scheduler_set_pack_block", {
        packId: packId,
        blocked: true,
        durationMinutes: (byId("optSchedulerSuppressMinutes").value || "").trim()
      });
    });
  }
  var schedulerScopePackMuteUntilMaintenanceButton = byId("btnSchedulerScopePackMuteUntilMaintenance");
  if (schedulerScopePackMuteUntilMaintenanceButton) {
    schedulerScopePackMuteUntilMaintenanceButton.addEventListener("click", function() {
      var packId = String(schedulerScopePackMuteUntilMaintenanceButton.dataset.packId || "").trim();
      if (!packId) {
        return;
      }

      post("scheduler_set_pack_block", {
        packId: packId,
        blocked: true,
        untilNextMaintenanceWindow: true
      });
    });
  }
  var schedulerScopePackMuteUntilMaintenanceStartButton = byId("btnSchedulerScopePackMuteUntilMaintenanceStart");
  if (schedulerScopePackMuteUntilMaintenanceStartButton) {
    schedulerScopePackMuteUntilMaintenanceStartButton.addEventListener("click", function() {
      var packId = String(schedulerScopePackMuteUntilMaintenanceStartButton.dataset.packId || "").trim();
      if (!packId) {
        return;
      }

      post("scheduler_set_pack_block", {
        packId: packId,
        blocked: true,
        untilNextMaintenanceWindowStart: true
      });
    });
  }
  var schedulerClearPackBlocksButton = byId("btnSchedulerClearPackBlocks");
  if (schedulerClearPackBlocksButton) {
    schedulerClearPackBlocksButton.addEventListener("click", function() {
      post("scheduler_clear_pack_blocks");
    });
  }
  var schedulerScopeToggleMuteButton = byId("btnSchedulerScopeToggleMute");
  if (schedulerScopeToggleMuteButton) {
    schedulerScopeToggleMuteButton.addEventListener("click", function() {
      var threadId = String(schedulerScopeToggleMuteButton.dataset.threadId || "").trim();
      if (!threadId) {
        return;
      }

      var blocked = String(schedulerScopeToggleMuteButton.dataset.blocked || "").trim().toLowerCase() === "true";
      post("scheduler_set_thread_block", {
        threadId: threadId,
        blocked: !blocked
      });
    });
  }
  var schedulerScopeTempMuteButton = byId("btnSchedulerScopeTempMute");
  if (schedulerScopeTempMuteButton) {
    schedulerScopeTempMuteButton.addEventListener("click", function() {
      var threadId = String(schedulerScopeTempMuteButton.dataset.threadId || "").trim();
      if (!threadId) {
        return;
      }

      post("scheduler_set_thread_block", {
        threadId: threadId,
        blocked: true,
        durationMinutes: (byId("optSchedulerSuppressMinutes").value || "").trim()
      });
    });
  }
  var schedulerScopeThreadMuteUntilMaintenanceButton = byId("btnSchedulerScopeThreadMuteUntilMaintenance");
  if (schedulerScopeThreadMuteUntilMaintenanceButton) {
    schedulerScopeThreadMuteUntilMaintenanceButton.addEventListener("click", function() {
      var threadId = String(schedulerScopeThreadMuteUntilMaintenanceButton.dataset.threadId || "").trim();
      if (!threadId) {
        return;
      }

      post("scheduler_set_thread_block", {
        threadId: threadId,
        blocked: true,
        untilNextMaintenanceWindow: true
      });
    });
  }
  var schedulerScopeThreadMuteUntilMaintenanceStartButton = byId("btnSchedulerScopeThreadMuteUntilMaintenanceStart");
  if (schedulerScopeThreadMuteUntilMaintenanceStartButton) {
    schedulerScopeThreadMuteUntilMaintenanceStartButton.addEventListener("click", function() {
      var threadId = String(schedulerScopeThreadMuteUntilMaintenanceStartButton.dataset.threadId || "").trim();
      if (!threadId) {
        return;
      }

      post("scheduler_set_thread_block", {
        threadId: threadId,
        blocked: true,
        untilNextMaintenanceWindowStart: true
      });
    });
  }
  var schedulerClearThreadBlocksButton = byId("btnSchedulerClearThreadBlocks");
  if (schedulerClearThreadBlocksButton) {
    schedulerClearThreadBlocksButton.addEventListener("click", function() {
      post("scheduler_clear_thread_blocks");
    });
  }
  var schedulerPauseButton = byId("btnSchedulerPause");
  if (schedulerPauseButton) {
    schedulerPauseButton.addEventListener("click", function() {
      post("scheduler_pause", {
        minutes: (byId("optSchedulerPauseMinutes").value || "").trim()
      });
    });
  }
  var schedulerResumeButton = byId("btnSchedulerResume");
  if (schedulerResumeButton) {
    schedulerResumeButton.addEventListener("click", function() {
      post("scheduler_resume");
    });
  }
  var schedulerAddMaintenanceButton = byId("btnSchedulerAddMaintenance");
  if (schedulerAddMaintenanceButton) {
    schedulerAddMaintenanceButton.addEventListener("click", function() {
      post("scheduler_add_maintenance", {
        day: (byId("optSchedulerMaintenanceDay").value || "daily").trim(),
        startTimeLocal: (byId("optSchedulerMaintenanceStart").value || "").trim(),
        durationMinutes: (byId("optSchedulerMaintenanceDuration").value || "").trim(),
        packId: (byId("optSchedulerMaintenancePackId").value || "").trim(),
        threadId: (byId("optSchedulerMaintenanceThreadId").value || "").trim()
      });
    });
  }
  var schedulerClearMaintenanceButton = byId("btnSchedulerClearMaintenance");
  if (schedulerClearMaintenanceButton) {
    schedulerClearMaintenanceButton.addEventListener("click", function() {
      post("scheduler_clear_maintenance");
    });
  }
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

  byId("optShowTurnTrace").addEventListener("change", function(e) {
    post("set_show_turn_trace", { enabled: e.target.checked === true });
  });
  byId("optShowDraftBubbles").addEventListener("change", function(e) {
    post("set_show_draft_bubbles", { enabled: e.target.checked === true });
  });

  byId("btnDebugToggleEngine").addEventListener("click", function() {
    if (!normalizeBool(state.options.debugToolsEnabled)) {
      return;
    }
    post("toggle_debug");
  });

  byId("btnDebugExportTranscriptForensics").addEventListener("click", function() {
    if (!normalizeBool(state.options.debugToolsEnabled)) {
      return;
    }
    post("debug_export_transcript_forensics");
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

  function normalizeCompatibleAuthMode(value) {
    var normalized = String(value || "").trim().toLowerCase();
    if (normalized === "basic") {
      return "basic";
    }
    if (normalized === "none" || normalized === "off") {
      return "none";
    }
    return "bearer";
  }

  function detectCompatibleProviderPreset(baseUrl) {
    var normalized = String(baseUrl || "").trim().toLowerCase();
    if (!normalized) {
      return "manual";
    }
    if (normalized.indexOf("127.0.0.1:1234") >= 0 || normalized.indexOf("localhost:1234") >= 0) {
      return "lmstudio";
    }
    if (normalized.indexOf("127.0.0.1:11434") >= 0 || normalized.indexOf("localhost:11434") >= 0) {
      return "ollama";
    }
    if (normalized.indexOf("api.openai.com") >= 0) {
      return "openai";
    }
    if (normalized.indexOf(".openai.azure.com") >= 0) {
      return "azure-openai";
    }
    if (normalized.indexOf("anthropic") >= 0 || normalized.indexOf("claude") >= 0) {
      return "anthropic-bridge";
    }
    if (normalized.indexOf("gemini") >= 0 || normalized.indexOf("googleapis.com") >= 0) {
      return "gemini-bridge";
    }
    return "manual";
  }

  function resolveReasoningSupportForDraft(transport, baseUrl) {
    var normalizedTransport = normalizeLocalTransportValue(transport);
    if (normalizedTransport === "copilot-cli") {
      return {
        supported: false,
        reason: "GitHub Copilot subscription runtime currently does not expose reasoning controls."
      };
    }

    return {
      supported: true,
      reason: ""
    };
  }

  function resolveNativeSlotCount(local) {
    var runtimeCapabilities = local && local.runtimeCapabilities && typeof local.runtimeCapabilities === "object"
      ? local.runtimeCapabilities
      : {};
    var slotCount = Number(runtimeCapabilities.nativeAccountSlots);
    if (!Number.isFinite(slotCount) || slotCount < 1) {
      slotCount = Array.isArray(local && local.nativeAccountSlots) ? local.nativeAccountSlots.length : 0;
    }
    if (!Number.isFinite(slotCount) || slotCount < 1) {
      slotCount = 3;
    }
    slotCount = Math.floor(slotCount);
    if (slotCount < 1) {
      slotCount = 1;
    }
    if (slotCount > MAX_NATIVE_ACCOUNT_SLOTS) {
      slotCount = MAX_NATIVE_ACCOUNT_SLOTS;
    }
    return slotCount;
  }

  function normalizeNativeSlot(value, maxSlots) {
    var parsed = Number(value);
    if (!Number.isFinite(parsed)) {
      return 1;
    }

    var max = Number(maxSlots);
    if (!Number.isFinite(max) || max < 1) {
      max = 3;
    } else {
      max = Math.floor(max);
    }
    if (max < 1) {
      max = 1;
    } else if (max > MAX_NATIVE_ACCOUNT_SLOTS) {
      max = MAX_NATIVE_ACCOUNT_SLOTS;
    }

    var normalized = Math.floor(parsed);
    if (normalized < 1) {
      return 1;
    }
    if (normalized > max) {
      return max;
    }
    return normalized;
  }

  function resolveNativeSlotAccountId(local, slot) {
    if (!local || !Array.isArray(local.nativeAccountSlots)) {
      return "";
    }

    var selectedSlot = normalizeNativeSlot(slot, resolveNativeSlotCount(local));
    for (var i = 0; i < local.nativeAccountSlots.length; i++) {
      var item = local.nativeAccountSlots[i] || {};
      var itemSlot = normalizeNativeSlot(item.slot, resolveNativeSlotCount(local));
      if (itemSlot === selectedSlot) {
        return String(item.accountId || "").trim();
      }
    }

    return "";
  }

  function applyCompatiblePresetSelection(preset) {
    var key = String(preset || "").toLowerCase();
    var transport = byId("optLocalTransport");
    var baseUrl = byId("optLocalBaseUrl");
    var providerPreset = byId("optLocalProviderPreset");
    var authMode = byId("optLocalAuthMode");
    var basicUsername = byId("optLocalBasicUsername");
    var basicPassword = byId("optLocalBasicPassword");
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
      if (authMode) authMode.value = "none";
    } else if (key === "ollama") {
      baseUrl.value = "http://127.0.0.1:11434";
      if (authMode) authMode.value = "none";
    } else if (key === "copilot") {
      baseUrl.value = "https://api.githubcopilot.com/v1";
      if (authMode) authMode.value = "bearer";
    } else if (key === "openai") {
      baseUrl.value = "https://api.openai.com/v1";
      if (authMode) authMode.value = "bearer";
    } else if (key === "azure-openai") {
      baseUrl.value = "https://your-resource.openai.azure.com/openai/deployments/your-deployment";
      if (authMode) authMode.value = "bearer";
    } else if (key === "anthropic-bridge") {
      baseUrl.value = "http://127.0.0.1:4000/v1";
      if (authMode) authMode.value = "basic";
    } else if (key === "gemini-bridge") {
      baseUrl.value = "http://127.0.0.1:5001/v1";
      if (authMode) authMode.value = "basic";
    }
    if (authMode) {
      authMode.value = normalizeCompatibleAuthMode(authMode.value);
      syncCustomSelect(authMode);
    }
    if (providerPreset) {
      providerPreset.value = key;
      syncCustomSelect(providerPreset);
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
    if (basicUsername && key !== "manual") {
      basicUsername.value = "";
    }
    if (basicPassword && key !== "manual") {
      basicPassword.value = "";
    }
    if (basicUsername) {
      if (key === "anthropic-bridge") {
        basicUsername.placeholder = "Anthropic bridge login/email";
      } else if (key === "gemini-bridge") {
        basicUsername.placeholder = "Gemini bridge login/email";
      } else {
        basicUsername.placeholder = "Leave blank to keep current value";
      }
    }
    if (basicPassword) {
      if (key === "anthropic-bridge") {
        basicPassword.placeholder = "Anthropic bridge secret/token";
      } else if (key === "gemini-bridge") {
        basicPassword.placeholder = "Gemini bridge secret/token";
      } else {
        basicPassword.placeholder = "Leave blank to keep existing secret";
      }
    }
    if (modelInput) {
      modelInput.value = "";
    }
  }

  var scheduledLocalProviderApplyTimer = 0;
  var localProviderAutoApplyDelayMs = 220;
  var pendingLocalProviderApply = null;
  var localProviderApplyRequestId = 0;

  function normalizeRuntimeApplyRequestId(value) {
    var parsed = Number(value);
    if (!Number.isFinite(parsed)) {
      return 0;
    }
    parsed = Math.floor(parsed);
    return parsed > 0 ? parsed : 0;
  }

  function nextLocalProviderApplyRequestId() {
    localProviderApplyRequestId = localProviderApplyRequestId + 1;
    return localProviderApplyRequestId;
  }

  window.ixRememberRuntimeApplyRequestId = function(value) {
    var normalized = normalizeRuntimeApplyRequestId(value);
    if (normalized > localProviderApplyRequestId) {
      localProviderApplyRequestId = normalized;
    }
  };

  function clearScheduledLocalProviderApply() {
    if (scheduledLocalProviderApplyTimer) {
      clearTimeout(scheduledLocalProviderApplyTimer);
      scheduledLocalProviderApplyTimer = 0;
    }
    pendingLocalProviderApply = null;
  }

  function scheduleLocalProviderApply(forceRefresh, clearApiKey, clearBasicAuth) {
    var nextForceRefresh = forceRefresh !== false;
    var nextClearApiKey = clearApiKey === true;
    var nextClearBasicAuth = clearBasicAuth === true;
    if (!pendingLocalProviderApply) {
      pendingLocalProviderApply = {
        forceRefresh: nextForceRefresh,
        clearApiKey: nextClearApiKey,
        clearBasicAuth: nextClearBasicAuth
      };
    } else {
      pendingLocalProviderApply.forceRefresh = pendingLocalProviderApply.forceRefresh || nextForceRefresh;
      pendingLocalProviderApply.clearApiKey = pendingLocalProviderApply.clearApiKey || nextClearApiKey;
      pendingLocalProviderApply.clearBasicAuth = pendingLocalProviderApply.clearBasicAuth || nextClearBasicAuth;
    }
    if (scheduledLocalProviderApplyTimer) {
      clearTimeout(scheduledLocalProviderApplyTimer);
      scheduledLocalProviderApplyTimer = 0;
    }
    scheduledLocalProviderApplyTimer = setTimeout(function() {
      var applyRequest = pendingLocalProviderApply || {
        forceRefresh: nextForceRefresh,
        clearApiKey: nextClearApiKey,
        clearBasicAuth: nextClearBasicAuth
      };
      pendingLocalProviderApply = null;
      scheduledLocalProviderApplyTimer = 0;
      applyLocalProviderSettings(applyRequest.forceRefresh, applyRequest.clearApiKey, applyRequest.clearBasicAuth);
    }, localProviderAutoApplyDelayMs);
  }

  function markLocalProviderDraftChanged() {
    clearScheduledLocalProviderApply();
    renderLocalModelOptions();
  }

  function applyLocalProviderSettings(forceRefresh, clearApiKey, clearBasicAuth) {
    clearScheduledLocalProviderApply();
    var local = ((state.options || {}).localModel || {});
    var wasApplying = local.isApplying === true;
    var requestId = nextLocalProviderApplyRequestId();
    var transport = normalizeLocalTransportValue(byId("optLocalTransport").value || "native");
    var baseUrl = transportUsesCompatibleHttp(transport)
      ? (byId("optLocalBaseUrl").value || "").trim()
      : "";
    var model = (byId("optLocalModelInput").value || "").trim();
    var activeNativeAccountSlot = normalizeNativeSlot(byId("optNativeAccountSlot").value || "1", resolveNativeSlotCount(local));
    var activeSlotAccountId = (byId("optNativeAccountId").value || "").trim();
    var openAIAccountId = activeSlotAccountId;
    var reasoningEffort = (byId("optReasoningEffort").value || "").trim();
    var reasoningSummary = (byId("optReasoningSummary").value || "").trim();
    var textVerbosity = (byId("optTextVerbosity").value || "").trim();
    var temperature = (byId("optTemperature").value || "").trim();
    var reasoningSupport = resolveReasoningSupportForDraft(transport, baseUrl);
    if (!reasoningSupport.supported) {
      reasoningEffort = "";
      reasoningSummary = "";
      textVerbosity = "";
      var effortSelect = byId("optReasoningEffort");
      var summarySelect = byId("optReasoningSummary");
      var verbositySelect = byId("optTextVerbosity");
      if (effortSelect) {
        effortSelect.value = "";
        syncCustomSelect(effortSelect);
      }
      if (summarySelect) {
        summarySelect.value = "";
        syncCustomSelect(summarySelect);
      }
      if (verbositySelect) {
        verbositySelect.value = "";
        syncCustomSelect(verbositySelect);
      }
    }
    var openAIAuthMode = normalizeCompatibleAuthMode(byId("optLocalAuthMode").value || "bearer");
    var openAIBasicUsername = (byId("optLocalBasicUsername").value || "").trim();
    var openAIBasicPassword = (byId("optLocalBasicPassword").value || "").trim();
    var basicPasswordInput = byId("optLocalBasicPassword");
    var shouldClearApiKey = clearApiKey === true;
    var shouldClearBasicAuth = clearBasicAuth === true;
    var apiKey = transportUsesCompatibleHttp(transport)
      ? (byId("optLocalApiKey").value || "").trim()
      : "";
    var apiKeyInput = byId("optLocalApiKey");
    if (shouldClearApiKey) {
      apiKey = "";
    }
    if (shouldClearBasicAuth) {
      openAIBasicUsername = "";
      openAIBasicPassword = "";
      byId("optLocalBasicUsername").value = "";
      byId("optLocalBasicPassword").value = "";
    }
    if (!transportUsesCompatibleHttp(transport)) {
      openAIAuthMode = "bearer";
      openAIBasicUsername = "";
      openAIBasicPassword = "";
    }
    if (basicPasswordInput) {
      basicPasswordInput.value = "";
    }
    if (apiKeyInput) {
      apiKeyInput.value = "";
    }
    if (state.options) {
      if (!state.options.localModel) {
        state.options.localModel = {};
      }
      state.options.localModel.transport = transport;
      state.options.localModel.baseUrl = baseUrl;
      state.options.localModel.model = model;
      state.options.localModel.openAIAuthMode = openAIAuthMode;
      state.options.localModel.openAIBasicUsername = openAIBasicUsername;
      state.options.localModel.openAIAccountId = openAIAccountId;
      state.options.localModel.activeNativeAccountSlot = activeNativeAccountSlot;
      if (!Array.isArray(state.options.localModel.nativeAccountSlots)) {
        state.options.localModel.nativeAccountSlots = [];
      }
      var updatedSlot = false;
      for (var slotIndex = 0; slotIndex < state.options.localModel.nativeAccountSlots.length; slotIndex++) {
        var slotItem = state.options.localModel.nativeAccountSlots[slotIndex] || {};
        if (normalizeNativeSlot(slotItem.slot, resolveNativeSlotCount(state.options.localModel)) === activeNativeAccountSlot) {
          slotItem.slot = activeNativeAccountSlot;
          slotItem.accountId = activeSlotAccountId;
          state.options.localModel.nativeAccountSlots[slotIndex] = slotItem;
          updatedSlot = true;
          break;
        }
      }
      if (!updatedSlot) {
        state.options.localModel.nativeAccountSlots.push({ slot: activeNativeAccountSlot, accountId: activeSlotAccountId });
      }
      state.options.localModel.reasoningEffort = reasoningEffort;
      state.options.localModel.reasoningSummary = reasoningSummary;
      state.options.localModel.textVerbosity = textVerbosity;
      state.options.localModel.temperature = temperature;
      state.options.localModel.isApplying = true;
      var runtimeApply = state.options.localModel.runtimeApply;
      if (!runtimeApply || typeof runtimeApply !== "object") {
        runtimeApply = {};
        state.options.localModel.runtimeApply = runtimeApply;
      }
      runtimeApply.stage = wasApplying ? "queued" : "applying";
      runtimeApply.detail = wasApplying
        ? "Runtime switch queued. Latest settings will apply next."
        : "Applying runtime settings...";
      runtimeApply.isActive = true;
      runtimeApply.updatedLocal = "";
      runtimeApply.requestId = requestId;
      renderLocalModelOptions();
    }
    post("apply_local_provider", {
      requestId: requestId,
      transport: transport,
      baseUrl: baseUrl,
      model: model,
      openAIAuthMode: openAIAuthMode,
      openAIBasicUsername: openAIBasicUsername,
      openAIBasicPassword: openAIBasicPassword,
      openAIAccountId: openAIAccountId,
      activeNativeAccountSlot: activeNativeAccountSlot,
      activeSlotAccountId: activeSlotAccountId,
      reasoningEffort: reasoningEffort,
      reasoningSummary: reasoningSummary,
      textVerbosity: textVerbosity,
      temperature: temperature,
      apiKey: apiKey,
      clearBasicAuth: shouldClearBasicAuth,
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
    var currentOpenAIAuthMode = normalizeCompatibleAuthMode(local.openAIAuthMode || "bearer");
    var currentOpenAIBasicUsername = String(local.openAIBasicUsername || "").trim();
    var currentOpenAIAccountId = String(local.openAIAccountId || "").trim();
    var currentNativeSlotCount = resolveNativeSlotCount(local);
    var currentActiveNativeAccountSlot = normalizeNativeSlot(local.activeNativeAccountSlot || 1, currentNativeSlotCount);
    var currentSlotAccountId = resolveNativeSlotAccountId(local, currentActiveNativeAccountSlot);

    var draftTransport = normalizeLocalTransportValue(byId("optLocalTransport").value || "native");
    var draftBaseUrl = transportUsesCompatibleHttp(draftTransport)
      ? (byId("optLocalBaseUrl").value || "").trim().toLowerCase()
      : "";
    var draftModel = (byId("optLocalModelInput").value || "").trim();
    var draftActiveNativeAccountSlot = normalizeNativeSlot(byId("optNativeAccountSlot").value || "1", currentNativeSlotCount);
    var draftSlotAccountId = (byId("optNativeAccountId").value || "").trim();
    var draftOpenAIAccountId = draftSlotAccountId;
    var draftReasoningEffort = (byId("optReasoningEffort").value || "").trim().toLowerCase();
    var draftReasoningSummary = (byId("optReasoningSummary").value || "").trim().toLowerCase();
    var draftTextVerbosity = (byId("optTextVerbosity").value || "").trim().toLowerCase();
    var draftReasoningSupport = resolveReasoningSupportForDraft(draftTransport, draftBaseUrl);
    if (!draftReasoningSupport.supported) {
      draftReasoningEffort = "";
      draftReasoningSummary = "";
      draftTextVerbosity = "";
    }
    var currentReasoningSupport = resolveReasoningSupportForDraft(currentTransport, currentBaseUrl);
    var currentReasoningEffort = currentReasoningSupport.supported
      ? String(local.reasoningEffort || "").trim().toLowerCase()
      : "";
    var currentReasoningSummary = currentReasoningSupport.supported
      ? String(local.reasoningSummary || "").trim().toLowerCase()
      : "";
    var currentTextVerbosity = currentReasoningSupport.supported
      ? String(local.textVerbosity || "").trim().toLowerCase()
      : "";
    var draftTemperature = (byId("optTemperature").value || "").trim();
    var draftOpenAIAuthMode = transportUsesCompatibleHttp(draftTransport)
      ? normalizeCompatibleAuthMode(byId("optLocalAuthMode").value || "bearer")
      : "bearer";
    var draftOpenAIBasicUsername = transportUsesCompatibleHttp(draftTransport)
      ? (byId("optLocalBasicUsername").value || "").trim()
      : "";
    var draftOpenAIBasicPassword = transportUsesCompatibleHttp(draftTransport)
      ? (byId("optLocalBasicPassword").value || "").trim()
      : "";
    var draftApiKey = transportUsesCompatibleHttp(draftTransport)
      ? (byId("optLocalApiKey").value || "").trim()
      : "";
    var compareNativeAccountState = draftTransport === "native" || currentTransport === "native";

    return draftTransport !== currentTransport
      || draftBaseUrl !== currentBaseUrl
      || draftModel !== currentModel
      || (compareNativeAccountState && draftOpenAIAccountId !== currentOpenAIAccountId)
      || (compareNativeAccountState && draftActiveNativeAccountSlot !== currentActiveNativeAccountSlot)
      || (compareNativeAccountState
        && draftActiveNativeAccountSlot === currentActiveNativeAccountSlot
        && draftSlotAccountId !== currentSlotAccountId)
      || draftReasoningEffort !== currentReasoningEffort
      || draftReasoningSummary !== currentReasoningSummary
      || draftTextVerbosity !== currentTextVerbosity
      || draftTemperature !== String(local.temperature == null ? "" : local.temperature).trim()
      || draftOpenAIAuthMode !== currentOpenAIAuthMode
      || draftOpenAIBasicUsername !== currentOpenAIBasicUsername
      || draftOpenAIBasicPassword.length > 0
      || draftApiKey.length > 0;
  }

  byId("optLocalTransport").addEventListener("change", function(e) {
    var next = normalizeLocalTransportValue(e.target.value || "native");
    var previousTransport = normalizeLocalTransportValue((((state.options || {}).localModel || {}).transport || "native"));
    var isCompatible = transportUsesCompatibleHttp(next);
    e.target.value = next;
    syncCustomSelect(e.target);

    var baseRow = byId("optLocalBaseUrlRow");
    var baseInput = byId("optLocalBaseUrl");
    var providerPresetRow = byId("optLocalProviderPresetRow");
    var providerPresetSelect = byId("optLocalProviderPreset");
    var apiKeyRow = byId("optLocalApiKeyRow");
    var apiKeyHint = byId("optLocalApiKeyHint");
    var apiKeyInput = byId("optLocalApiKey");
    var authModeRow = byId("optLocalAuthModeRow");
    var authModeSelect = byId("optLocalAuthMode");
    var basicUsernameRow = byId("optLocalBasicUsernameRow");
    var basicUsernameInput = byId("optLocalBasicUsername");
    var basicPasswordRow = byId("optLocalBasicPasswordRow");
    var basicPasswordInput = byId("optLocalBasicPassword");
    var authHint = byId("optLocalAuthHint");
    var autoDetectButton = byId("btnAutoDetectLocalRuntime");
    var clearBasicButton = byId("btnClearLocalBasicAuth");
    if (baseRow) {
      baseRow.hidden = !isCompatible;
    }
    if (providerPresetRow) {
      providerPresetRow.hidden = !isCompatible;
    }
    if (providerPresetSelect) {
      if (isCompatible) {
        providerPresetSelect.value = detectCompatibleProviderPreset(baseInput ? baseInput.value : "");
        syncCustomSelect(providerPresetSelect);
      }
      providerPresetSelect.disabled = !isCompatible;
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
    var modelInput = byId("optLocalModelInput");
    if (modelInput && previousTransport !== next && !isCompatible) {
      modelInput.value = "";
    }
    if (apiKeyRow) {
      apiKeyRow.hidden = !isCompatible;
    }
    if (authModeRow) {
      authModeRow.hidden = !isCompatible;
    }
    if (authModeSelect) {
      authModeSelect.disabled = !isCompatible;
      if (!isCompatible) {
        authModeSelect.value = "bearer";
      } else {
        authModeSelect.value = normalizeCompatibleAuthMode(authModeSelect.value || "bearer");
      }
      syncCustomSelect(authModeSelect);
    }
    var authMode = authModeSelect ? normalizeCompatibleAuthMode(authModeSelect.value || "bearer") : "bearer";
    if (basicUsernameRow) {
      basicUsernameRow.hidden = !isCompatible || authMode !== "basic";
    }
    if (basicPasswordRow) {
      basicPasswordRow.hidden = !isCompatible || authMode !== "basic";
    }
    if (basicUsernameInput) {
      basicUsernameInput.disabled = !isCompatible || authMode !== "basic";
    }
    if (basicPasswordInput) {
      basicPasswordInput.disabled = !isCompatible || authMode !== "basic";
      if (!isCompatible || authMode !== "basic") {
        basicPasswordInput.value = "";
      }
    }
    if (apiKeyHint) {
      apiKeyHint.hidden = !isCompatible;
      if (isCompatible && authMode === "basic") {
        apiKeyHint.textContent = "Bearer API key is ignored while auth mode is Basic.";
      } else {
        apiKeyHint.textContent = "Use Clear Saved API Key to remove the currently stored key.";
      }
    }
    if (apiKeyInput) {
      apiKeyInput.disabled = !isCompatible || authMode !== "bearer";
    }
    if (authHint) {
      authHint.hidden = !isCompatible;
    }
    if (clearBasicButton) {
      clearBasicButton.hidden = !isCompatible;
      clearBasicButton.disabled = !isCompatible;
    }
    if (autoDetectButton) {
      autoDetectButton.hidden = !isCompatible;
    }

    markLocalProviderDraftChanged();
  });

  byId("optLocalProviderPreset").addEventListener("change", function(e) {
    var preset = String(e.target.value || "manual").trim().toLowerCase();
    if (preset && preset !== "manual") {
      applyCompatiblePresetSelection(preset);
    }
    syncCustomSelect(e.target);
    markLocalProviderDraftChanged();
  });

  byId("optLocalAuthMode").addEventListener("change", function(e) {
    e.target.value = normalizeCompatibleAuthMode(e.target.value || "bearer");
    syncCustomSelect(e.target);
    byId("optLocalTransport").dispatchEvent(new Event("change"));
    markLocalProviderDraftChanged();
  });

  byId("optLocalBaseUrl").addEventListener("change", function() {
    markLocalProviderDraftChanged();
  });

  byId("optLocalApiKey").addEventListener("change", function() {
    markLocalProviderDraftChanged();
  });

  byId("optLocalModelInput").addEventListener("change", function() {
    markLocalProviderDraftChanged();
  });

  byId("optReasoningEffort").addEventListener("change", function() {
    markLocalProviderDraftChanged();
  });

  byId("optReasoningSummary").addEventListener("change", function() {
    markLocalProviderDraftChanged();
  });

  byId("optTextVerbosity").addEventListener("change", function() {
    markLocalProviderDraftChanged();
  });

  byId("optTemperature").addEventListener("change", function() {
    markLocalProviderDraftChanged();
  });

  byId("optLocalBasicUsername").addEventListener("change", function() {
    markLocalProviderDraftChanged();
  });

  byId("optLocalBasicPassword").addEventListener("change", function() {
    markLocalProviderDraftChanged();
  });

  byId("optLocalModelSelect").addEventListener("change", function(e) {
    var selected = (e.target.value || "").trim();
    var modelInput = byId("optLocalModelInput");
    if (!selected) {
      if (modelInput) {
        if (typeof modelInput.scrollIntoView === "function") {
          modelInput.scrollIntoView({ behavior: "smooth", block: "center" });
        }
        modelInput.focus();
        modelInput.select();
      }
      return;
    }
    if (modelInput) {
      modelInput.value = selected;
    }
    applyLocalProviderSettings(true);
  });

  byId("optNativeAccountSlot").addEventListener("change", function(e) {
    var local = (state.options || {}).localModel || {};
    var slot = normalizeNativeSlot(e.target.value || "1", resolveNativeSlotCount(local));
    e.target.value = String(slot);
    syncCustomSelect(e.target);

    var accountIdInput = byId("optNativeAccountId");
    if (accountIdInput) {
      accountIdInput.value = resolveNativeSlotAccountId(local, slot);
    }

    markLocalProviderDraftChanged();
  });

  byId("optNativeAccountId").addEventListener("change", function() {
    markLocalProviderDraftChanged();
  });

  byId("optLocalModelFilter").addEventListener("input", function(e) {
    writeStorage(runtimeModelFilterStorageKey(), (e.target.value || "").trim());
    renderLocalModelOptions();
  });

  byId("optRuntimePanelView").addEventListener("change", function(e) {
    var normalized = setRuntimePanelView(e.target.value || "provider");
    e.target.value = normalized;
    syncCustomSelect(e.target);
    renderLocalModelOptions();
  });

  byId("btnToggleLocalAdvancedRuntime").addEventListener("click", function() {
    setRuntimeAdvancedOpen(!isRuntimeAdvancedOpen());
  });

  byId("btnUseOpenAiRuntime").addEventListener("click", function() {
    var transport = byId("optLocalTransport");
    var modelInput = byId("optLocalModelInput");
    transport.value = "native";
    syncCustomSelect(transport);
    if (modelInput) {
      modelInput.value = "";
    }
    applyLocalProviderSettings(true);
  });

  byId("btnConnectLmStudio").addEventListener("click", function() {
    applyCompatiblePresetSelection("lmstudio");
    applyLocalProviderSettings(true);
  });

  byId("btnUseCopilotRuntime").addEventListener("click", function() {
    var transport = byId("optLocalTransport");
    var modelInput = byId("optLocalModelInput");
    if (transport) {
      transport.value = "copilot-cli";
      syncCustomSelect(transport);
      transport.dispatchEvent(new Event("change"));
    }
    if (modelInput) {
      modelInput.value = "";
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

  byId("btnLocalPresetOpenAI").addEventListener("click", function() {
    applyCompatiblePresetSelection("openai");
    applyLocalProviderSettings(true);
  });

  byId("btnLocalPresetAzureOpenAI").addEventListener("click", function() {
    applyCompatiblePresetSelection("azure-openai");
    applyLocalProviderSettings(true);
  });

  byId("btnLocalPresetAnthropicBridge").addEventListener("click", function() {
    applyCompatiblePresetSelection("anthropic-bridge");
    applyLocalProviderSettings(true);
  });

  byId("btnLocalPresetGeminiBridge").addEventListener("click", function() {
    applyCompatiblePresetSelection("gemini-bridge");
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

  byId("btnClearLocalBasicAuth").addEventListener("click", function() {
    if (!transportUsesCompatibleHttp(byId("optLocalTransport").value || "native")) {
      return;
    }
    byId("optLocalBasicUsername").value = "";
    byId("optLocalBasicPassword").value = "";
    applyLocalProviderSettings(true, false, true);
  });

  var btnNewConversation = byId("btnNewConversation");
  if (btnNewConversation) {
    btnNewConversation.addEventListener("click", function() {
      setPendingConversationSelection("");
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
        setPendingConversationSelection(id);
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

    optConversations.addEventListener("change", function(e) {
      var modelSelect = e.target.closest(".options-conversation-model-select");
      if (!modelSelect) {
        return;
      }

      var modelId = modelSelect.dataset.conversationId || "";
      if (!modelId) {
        return;
      }

      post("set_conversation_model", { id: modelId, model: (modelSelect.value || "").trim() });
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

  var toolLocalityFilters = byId("optToolLocalityFilters");
  if (toolLocalityFilters) {
    toolLocalityFilters.addEventListener("click", function(e) {
      var button = e.target.closest("[data-locality-filter]");
      if (!button) {
        return;
      }

      state.options.toolLocalityFilter = normalizeToolLocalityFilter(button.getAttribute("data-locality-filter"));
      renderTools();
    });
  }

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
      if (document.body.classList.contains("visual-view-open") && window.ixCloseVisualView) {
        window.ixCloseVisualView();
        return;
      }
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

    if (handleVisualViewNavKey(e)) {
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
        if (window.ixEnableTranscriptFollow) {
          window.ixEnableTranscriptFollow(true);
        }
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

    if (window.ixEnableTranscriptFollow) {
      window.ixEnableTranscriptFollow(true);
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
