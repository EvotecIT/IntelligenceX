  function renderOptions() {
    loadDebugToolsEnabledForActiveProfile();
    var selector = byId("optTimeMode");
    var timestampMode = (state.options.timestampMode || "").toLowerCase();
    if (timestampMode === "date_minutes") {
      timestampMode = "date-minutes";
    } else if (timestampMode === "date_seconds") {
      timestampMode = "date-seconds";
    }
    if (timestampMode === "minutes"
      || timestampMode === "seconds"
      || timestampMode === "date-minutes"
      || timestampMode === "date-seconds") {
      selector.value = timestampMode;
    } else {
      selector.value = "seconds";
    }
    var sidebarModeSelect = byId("optSidebarMode");
    if (sidebarModeSelect) {
      sidebarModeSelect.value = normalizeSidebarMode(sidebarPrefs.mode);
    }

    renderProfileSelector();
    var applyModeSelect = byId("optProfileApplyMode");
    if (applyModeSelect) {
      applyModeSelect.value = normalizeProfileApplyMode(state.options.profileApplyMode);
    }

    var profile = state.options.profile || {};
    byId("optUserName").value = profile.userName || "";
    byId("optPersona").value = profile.persona || "";
    byId("optTheme").value = profile.theme || "default";
    var toolFilterInput = byId("optToolFilter");
    if (toolFilterInput && toolFilterInput.value !== (state.options.toolFilter || "")) {
      toolFilterInput.value = state.options.toolFilter || "";
    }
    ensureCustomSelect("optTheme");
    ensureCustomSelect("optTimeMode");
    ensureCustomSelect("optExportSaveMode");
    ensureCustomSelect("optExportDefaultFormat");
    ensureCustomSelect("optExportVisualThemeMode");
    ensureCustomSelect("optSidebarMode");
    ensureCustomSelect("optProfileApplyMode");
    ensureCustomSelect("optLocalTransport");
    ensureCustomSelect("optLocalProviderPreset");
    ensureCustomSelect("optLocalAuthMode");
    ensureCustomSelect("optNativeAccountSlot");
    ensureCustomSelect("optRuntimePanelView");
    ensureCustomSelect("optLocalModelSelect");
    ensureCustomSelect("optReasoningEffort");
    ensureCustomSelect("optReasoningSummary");
    ensureCustomSelect("optTextVerbosity");
    ensureCustomSelect("optAutonomyParallel");
    ensureCustomSelect("optAutonomyPlanReview");
    ensureCustomSelect("optAutonomyWeightedRouting");

    renderPolicy();
    renderAutonomy();
    renderMemory();
    renderLocalModelOptions();
    renderTools();
    renderSidebarConversations();
    renderProfileScopeHint();
    renderExportPreferences();
    renderDebugPanel();
    updateMenuState();
  }

  function handleTranscriptNavKey(e) {
    if (getActiveModalMode() !== IX_MODAL_MODE_NONE) {
      return false;
    }

    if (document.activeElement === promptEl) {
      return false;
    }

    return applyPagedScrollKey(transcript, e.key, 120);
  }

  function handleDataViewNavKey(e) {
    if (getActiveModalMode() !== IX_MODAL_MODE_DATA_VIEW) {
      return false;
    }

    var target = getModalPrimaryScrollTarget(IX_MODAL_MODE_DATA_VIEW);
    if (!target) {
      return false;
    }

    if (isEditableElement(document.activeElement)) {
      return false;
    }

    return applyPagedScrollKey(target, e.key, 140);
  }

  function handleVisualViewNavKey(e) {
    if (getActiveModalMode() !== IX_MODAL_MODE_VISUAL_VIEW) {
      return false;
    }

    var target = getModalPrimaryScrollTarget(IX_MODAL_MODE_VISUAL_VIEW);
    if (!target) {
      return false;
    }

    if (isEditableElement(document.activeElement)) {
      return false;
    }

    return applyPagedScrollKey(target, e.key, 140);
  }

  function handleOptionsNavKey(e) {
    if (getActiveModalMode() !== IX_MODAL_MODE_OPTIONS) {
      return false;
    }

    var target = getModalPrimaryScrollTarget(IX_MODAL_MODE_OPTIONS);
    if (!target) {
      return false;
    }

    if (document.activeElement === promptEl || document.activeElement === byId("optPersona")) {
      return false;
    }

    return applyPagedScrollKey(target, e.key, 120);
  }

  window.ixSetTheme = function(vars) {
    for (var key in vars) {
      if (Object.prototype.hasOwnProperty.call(vars, key) && key.indexOf("--ix-") === 0) {
        document.documentElement.style.setProperty(key, vars[key]);
      }
    }
  };

  window.ixResetTheme = function() {
    var inline = document.documentElement.style;
    var keys = [];
    for (var i = 0; i < inline.length; i++) {
      var key = inline[i];
      if (key.indexOf("--ix-") === 0) {
        keys.push(key);
      }
    }
    for (var j = 0; j < keys.length; j++) {
      inline.removeProperty(keys[j]);
    }
  };

  window.ixSetStatus = function(text, tone) {
    state.status = text || "";
    if (typeof tone === "string") {
      state.statusTone = tone;
    }
    updateStatusVisual(state.status, state.statusTone);
  };

  window.ixSetSessionState = function(nextState) {
    nextState = nextState || {};
    var wasSending = state.sending === true;
    if (typeof nextState.status === "string") {
      state.status = nextState.status;
    }
    if (typeof nextState.statusTone === "string") {
      state.statusTone = nextState.statusTone;
    }
    if (typeof nextState.usageLimitSwitchRecommended === "boolean") {
      state.usageLimitSwitchRecommended = nextState.usageLimitSwitchRecommended;
    }
    if (typeof nextState.queuedPromptPending === "boolean") {
      state.queuedPromptPending = nextState.queuedPromptPending;
    }
    if (typeof nextState.queuedPromptCount === "number" && Number.isFinite(nextState.queuedPromptCount)) {
      state.queuedPromptCount = Math.max(0, Math.floor(nextState.queuedPromptCount));
    }
    if (typeof nextState.queuedTurnCount === "number" && Number.isFinite(nextState.queuedTurnCount)) {
      state.queuedTurnCount = Math.max(0, Math.floor(nextState.queuedTurnCount));
    }
    if (typeof nextState.connected === "boolean") {
      state.connected = nextState.connected;
    }
    if (typeof nextState.authenticated === "boolean") {
      state.authenticated = nextState.authenticated;
    }
    if (typeof nextState.accountId === "string") {
      state.accountId = nextState.accountId;
    }
    if (typeof nextState.loginInProgress === "boolean") {
      state.loginInProgress = nextState.loginInProgress;
    }
    if (typeof nextState.sending === "boolean") {
      state.sending = nextState.sending;
    }
    if (typeof nextState.cancelable === "boolean") {
      state.cancelable = nextState.cancelable;
    }
    if (typeof nextState.cancelRequested === "boolean") {
      state.cancelRequested = nextState.cancelRequested;
    }
    if (typeof nextState.debugMode === "boolean") {
      state.debugMode = nextState.debugMode;
    }
    if (Array.isArray(nextState.activityTimeline)) {
      state.activityTimeline = nextState.activityTimeline;
    }
    if (nextState.lastTurnMetrics && typeof nextState.lastTurnMetrics === "object") {
      state.lastTurnMetrics = nextState.lastTurnMetrics;
    } else if (nextState.lastTurnMetrics === null) {
      state.lastTurnMetrics = null;
    }
    if (nextState.latencySummary && typeof nextState.latencySummary === "object") {
      state.latencySummary = nextState.latencySummary;
    } else if (nextState.latencySummary === null) {
      state.latencySummary = null;
    }
    if (nextState.providerCircuit && typeof nextState.providerCircuit === "object") {
      state.providerCircuit = nextState.providerCircuit;
    } else if (nextState.providerCircuit === null) {
      state.providerCircuit = null;
    }
    if (typeof nextState.windowMaximized === "boolean") {
      state.windowMaximized = nextState.windowMaximized;
    }
    if (wasSending && !state.sending) {
      applyPendingTranscriptEnhancements();
    }

    updateStatusVisual(state.status, state.statusTone);
    updateWindowControlsState();
    updateMenuState();
    updateComposerState();
    renderAutonomy();
    renderDebugPanel();
  };

  function normalizeRuntimeApplyRequestId(value) {
    var parsed = Number(value);
    if (!Number.isFinite(parsed)) {
      return 0;
    }
    parsed = Math.floor(parsed);
    return parsed > 0 ? parsed : 0;
  }

  function resolveRuntimeApplyRequestId(localModel) {
    if (!localModel || typeof localModel !== "object") {
      return 0;
    }
    var runtimeApply = localModel.runtimeApply && typeof localModel.runtimeApply === "object"
      ? localModel.runtimeApply
      : {};
    return normalizeRuntimeApplyRequestId(runtimeApply.requestId);
  }

  window.ixSetOptionsData = function(nextOptions) {
    nextOptions = nextOptions || {};
    state.options.timestampMode = nextOptions.timestampMode || state.options.timestampMode;
    state.options.timestampFormat = nextOptions.timestampFormat || state.options.timestampFormat;
    state.options.export = nextOptions.export || state.options.export;
    state.options.autonomy = nextOptions.autonomy || state.options.autonomy;
    state.options.memory = nextOptions.memory || state.options.memory;
    state.options.memoryDebug = nextOptions.memoryDebug || null;
    state.options.activeProfileName = nextOptions.activeProfileName || state.options.activeProfileName;
    state.options.profileNames = nextOptions.profileNames || state.options.profileNames;
    state.options.activeConversationId = nextOptions.activeConversationId || state.options.activeConversationId;
    state.options.conversations = nextOptions.conversations || [];
    state.options.profile = nextOptions.profile || state.options.profile;
    var currentLocalModel = state.options.localModel && typeof state.options.localModel === "object"
      ? state.options.localModel
      : null;
    var incomingLocalModel = nextOptions.localModel && typeof nextOptions.localModel === "object"
      ? nextOptions.localModel
      : currentLocalModel;
    if (incomingLocalModel && currentLocalModel) {
      var incomingRequestId = resolveRuntimeApplyRequestId(incomingLocalModel);
      var currentRequestId = resolveRuntimeApplyRequestId(currentLocalModel);
      if (currentRequestId > 0 && incomingRequestId > 0 && incomingRequestId < currentRequestId) {
        incomingLocalModel.runtimeApply = currentLocalModel.runtimeApply;
        incomingLocalModel.isApplying = currentLocalModel.isApplying === true
          || (currentLocalModel.runtimeApply && currentLocalModel.runtimeApply.isActive === true);
      }
    }
    state.options.localModel = incomingLocalModel;
    if (window.ixRememberRuntimeApplyRequestId && incomingLocalModel) {
      window.ixRememberRuntimeApplyRequestId(resolveRuntimeApplyRequestId(incomingLocalModel));
    }
    state.options.policy = nextOptions.policy || null;
    var incomingPacks = Array.isArray(nextOptions.packs) ? nextOptions.packs : [];
    var incomingTools = Array.isArray(nextOptions.tools) ? nextOptions.tools : [];
    if (typeof nextOptions.toolsLoading === "boolean") {
      state.options.toolsLoading = nextOptions.toolsLoading;
    } else {
      state.options.toolsLoading = false;
    }

    var preservePreviousTools = state.options.toolsLoading
      && incomingTools.length === 0
      && Array.isArray(state.options.tools)
      && state.options.tools.length > 0;
    if (preservePreviousTools) {
      if (incomingPacks.length > 0) {
        state.options.packs = incomingPacks;
      }
    } else {
      state.options.packs = incomingPacks;
      state.options.tools = incomingTools;
    }
    loadDebugToolsEnabledForActiveProfile();
    renderOptions();
  };

  var transcriptFollowState = {
    enabled: true,
    suppressScrollEvent: false
  };
  var TRANSCRIPT_FOLLOW_ENABLE_THRESHOLD_PX = 28;
  var TRANSCRIPT_FOLLOW_DISABLE_THRESHOLD_PX = 84;
  var TRANSCRIPT_FOLLOW_DISABLE_SENDING_THRESHOLD_PX = 180;

  function distanceFromBottom(el) {
    if (!el) {
      return 0;
    }

    var distance = el.scrollHeight - el.scrollTop - el.clientHeight;
    if (!Number.isFinite(distance)) {
      return 0;
    }

    return distance < 0 ? 0 : distance;
  }

  function isNearBottom(el, thresholdPx) {
    if (!el) {
      return true;
    }

    var threshold = Number(thresholdPx);
    if (!Number.isFinite(threshold)) {
      threshold = 80;
    }

    return distanceFromBottom(el) < threshold;
  }

  function setTranscriptScrollTop(top) {
    transcriptFollowState.suppressScrollEvent = true;
    transcript.scrollTop = top;
    transcriptFollowState.suppressScrollEvent = false;
  }

  function scrollToBottom(el) {
    if (!el) {
      return;
    }

    if (el === transcript) {
      transcriptFollowState.suppressScrollEvent = true;
      el.scrollTop = el.scrollHeight;
      transcriptFollowState.suppressScrollEvent = false;
      transcriptFollowState.enabled = true;
      return;
    }

    el.scrollTop = el.scrollHeight;
  }

  function refreshTranscriptFollowState(options) {
    options = options || {};
    var allowDisable = options.allowDisable === true;
    var allowEnable = options.allowEnable !== false;
    var distance = distanceFromBottom(transcript);
    var disableThreshold = TRANSCRIPT_FOLLOW_DISABLE_THRESHOLD_PX;
    if (state.sending === true) {
      disableThreshold = Math.max(disableThreshold, TRANSCRIPT_FOLLOW_DISABLE_SENDING_THRESHOLD_PX);
    }
    if (transcriptFollowState.enabled) {
      if (allowDisable && distance > disableThreshold) {
        transcriptFollowState.enabled = false;
      }
      return transcriptFollowState.enabled;
    }

    if (allowEnable && distance <= TRANSCRIPT_FOLLOW_ENABLE_THRESHOLD_PX) {
      transcriptFollowState.enabled = true;
    }
    return transcriptFollowState.enabled;
  }

  transcript.addEventListener("scroll", function() {
    if (transcriptFollowState.suppressScrollEvent) {
      return;
    }
    refreshTranscriptFollowState({ allowDisable: true, allowEnable: true });
  });
  refreshTranscriptFollowState();

  window.ixEnableTranscriptFollow = function(stickBottom) {
    transcriptFollowState.enabled = true;
    if (stickBottom !== false) {
      scrollToBottom(transcript);
    }
  };

  window.ixSetActivity = function(text, timeline) {
    var el = byId("activity");
    var label = el.querySelector(".activity-text");
    if (text) {
      var timelineSummary = "";
      if (Array.isArray(timeline) && timeline.length > 0) {
        state.activityTimeline = timeline;
        if (state.debugEnabled) {
          timelineSummary = " | " + timeline.join(" > ");
        }
      }
      label.textContent = text + timelineSummary;
      el.classList.add("active");
      refreshTranscriptFollowState();
    } else {
      el.classList.remove("active");
    }
  };

  window.ixScrollTranscript = function(delta, source) {
    if (!delta) {
      return;
    }

    var amount = -Number(delta);
    if (!Number.isFinite(amount) || amount === 0) {
      return;
    }

    if (source === "host") {
      if (Date.now() - lastNativeWheelAt < 30) {
        if (window.ixWheelDiagRecord) {
          window.ixWheelDiagRecord("host_skip_after_native", { delta: Number(delta) });
        }
        return;
      }
      lastHostWheelAt = Date.now();
      if (window.ixWheelDiagRecord) {
        window.ixWheelDiagRecord("host_forward", { delta: Number(delta) });
      }

      var modalMode = getActiveModalMode();
      if (modalMode === IX_MODAL_MODE_OPTIONS) {
        var optionsTarget = getModalPrimaryScrollTarget(IX_MODAL_MODE_OPTIONS);
        var selectMenu = openCustomSelect ? openCustomSelect.querySelector(".ix-select-menu") : null;
        if (selectMenu) {
          optionsTarget = selectMenu;
        }

        if (optionsTarget) {
          var optionsBefore = optionsTarget.scrollTop;
          optionsTarget.scrollTop += amount;
          if (window.ixWheelDiagRecord) {
            window.ixWheelDiagRecord(optionsTarget.scrollTop !== optionsBefore ? "applied" : "not_applied", {
              deltaY: amount,
              zone: resolveWheelZoneName(modalMode)
            });
          }
        }
        return;
      }

      if (modalMode === IX_MODAL_MODE_DATA_VIEW) {
        var dataTarget = getModalPrimaryScrollTarget(IX_MODAL_MODE_DATA_VIEW);
        if (dataTarget) {
          var dataBefore = dataTarget.scrollTop;
          dataTarget.scrollTop += amount;
          if (window.ixWheelDiagRecord) {
            window.ixWheelDiagRecord(dataTarget.scrollTop !== dataBefore ? "applied" : "not_applied", {
              deltaY: amount,
              zone: resolveWheelZoneName(modalMode)
            });
          }
        }
        return;
      }

      if (modalMode === IX_MODAL_MODE_VISUAL_VIEW) {
        var visualTarget = getModalPrimaryScrollTarget(IX_MODAL_MODE_VISUAL_VIEW);
        if (visualTarget) {
          var visualBefore = visualTarget.scrollTop;
          visualTarget.scrollTop += amount;
          if (window.ixWheelDiagRecord) {
            window.ixWheelDiagRecord(visualTarget.scrollTop !== visualBefore ? "applied" : "not_applied", {
              deltaY: amount,
              zone: resolveWheelZoneName(modalMode)
            });
          }
        }
        return;
      }
    }

    var applied = applyWheelDelta(amount, null);
    if (source === "host" && window.ixWheelDiagRecord) {
      window.ixWheelDiagRecord(applied ? "applied" : "not_applied", {
        deltaY: amount,
        zone: "transcript"
      });
    }
  };

  function setupCodeCopyButtons() {
    var blocks = document.querySelectorAll(".bubble pre");
    for (var i = 0; i < blocks.length; i++) {
      (function(pre) {
        if (pre.classList && pre.classList.contains("mermaid")) {
          return;
        }

        if (pre.querySelector("code.language-ix-chart, code.language-chart")) {
          return;
        }

        if (pre.querySelector("code.language-ix-network")) {
          return;
        }

        if (pre.querySelector(".code-copy-btn")) {
          return;
        }

        var btn = document.createElement("button");
        btn.className = "code-copy-btn";
        btn.title = "Copy code";
        btn.innerHTML = "<svg width='13' height='13' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'><rect x='9' y='9' width='13' height='13' rx='2'/><path d='M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1'/></svg>";
        btn.addEventListener("click", function() {
          var code = pre.querySelector("code");
          var text = code ? code.textContent : pre.textContent;
          post("omd_copy", { text: text });
          btn.innerHTML = "<svg width='13' height='13' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2.5' stroke-linecap='round' stroke-linejoin='round'><polyline points='20 6 9 17 4 12'/></svg>";
          setTimeout(function() {
            btn.innerHTML = "<svg width='13' height='13' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'><rect x='9' y='9' width='13' height='13' rx='2'/><path d='M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1'/></svg>";
          }, 1500);
        });
        pre.appendChild(btn);
      })(blocks[i]);
    }
  }

  function copyTableAs(table, separator) {
    var rows = window.ixGetDataViewRowsForTable ? window.ixGetDataViewRowsForTable(table) : null;
    if (!rows || rows.length === 0) {
      rows = window.ixBuildTableMatrix ? window.ixBuildTableMatrix(table) : null;
    }
    if (!rows || rows.length === 0) {
      rows = [];
      var sourceRows = table.querySelectorAll("tr");
      for (var sr = 0; sr < sourceRows.length; sr++) {
        var sourceCells = sourceRows[sr].querySelectorAll("th, td");
        var sourceValues = [];
        for (var sc = 0; sc < sourceCells.length; sc++) {
          sourceValues.push((sourceCells[sc].textContent || "").trim());
        }
        if (sourceValues.length > 0) {
          rows.push(sourceValues);
        }
      }
    }

    var lines = [];
    for (var r = 0; r < rows.length; r++) {
      var values = [];
      for (var c = 0; c < rows[r].length; c++) {
        var text = String(rows[r][c] == null ? "" : rows[r][c]).trim();
        if (separator === "," && (text.indexOf(",") >= 0 || text.indexOf("\"") >= 0)) {
          text = "\"" + text.replace(/\"/g, "\"\"") + "\"";
        }
        values.push(text);
      }
      lines.push(values.join(separator));
    }
    post("omd_copy", { text: lines.join("\n") });
  }

  function setupTableCopyButtons() {
    var tables = document.querySelectorAll(".bubble table");
    for (var i = 0; i < tables.length; i++) {
      (function(table) {
        if (!table) {
          return;
        }

        var wrap = table.closest(".ix-dt-wrap") || table.parentElement;
        if (!wrap) {
          return;
        }

        var tableId = table.dataset.ixTableId || "";
        if (tableId && wrap.querySelector(".table-copy-bar[data-table-id='" + tableId + "']")) {
          return;
        }

        if (!tableId && wrap.querySelector(".table-copy-bar")) {
          return;
        }

        var bar = document.createElement("div");
        bar.className = "table-copy-bar";
        if (tableId) {
          bar.dataset.tableId = tableId;
        }

        var btnOpen = document.createElement("button");
        btnOpen.textContent = "Open Data View";
        btnOpen.addEventListener("click", function() {
          if (window.ixOpenDataViewForTable) {
            window.ixOpenDataViewForTable(table);
          }
        });

        var btnTsv = document.createElement("button");
        btnTsv.textContent = "Copy TSV";
        btnTsv.addEventListener("click", function() { copyTableAs(table, "\t"); });

        var btnCsv = document.createElement("button");
        btnCsv.textContent = "Copy CSV";
        btnCsv.addEventListener("click", function() { copyTableAs(table, ","); });

        bar.appendChild(btnOpen);
        bar.appendChild(btnTsv);
        bar.appendChild(btnCsv);
        if (wrap) {
          wrap.appendChild(bar);
        }
      })(tables[i]);
    }
  }

  var transcriptRenderRevision = 0;
  var transcriptLastHtml = null;
  var transcriptPendingVisualRefresh = false;

  function runTranscriptEnhancements() {
    var visualRenderTask = null;
    if (window.ixRenderTranscriptVisuals) {
      visualRenderTask = window.ixRenderTranscriptVisuals(transcript);
    }
    if (window.ixEnhanceTranscriptTables) {
      window.ixEnhanceTranscriptTables(transcript);
    }
    if (window.ixExtractToolDataViewPayloads) {
      window.ixExtractToolDataViewPayloads(transcript);
    }
    setupCodeCopyButtons();
    setupTableCopyButtons();
    return visualRenderTask;
  }

  function applyTranscriptVisualAnchoringAsync(
    visualRenderTask,
    renderRevision,
    shouldStickBottom,
    preserveDistanceAfterVisual,
    nonFollowAnchorTop) {
    if (!visualRenderTask || typeof visualRenderTask.then !== "function") {
      return;
    }

    visualRenderTask.then(function() {
      if (renderRevision !== transcriptRenderRevision) {
        return;
      }

      if (shouldStickBottom) {
        if (!transcriptFollowState.enabled) {
          return;
        }

        scrollToBottom(transcript);
        return;
      }

      if (preserveDistanceAfterVisual === null) {
        return;
      }

      // Keep non-follow views stable through async diagram/chart expansion unless user scrolled.
      if (Math.abs(transcript.scrollTop - nonFollowAnchorTop) > 40) {
        return;
      }

      var maxScrollTopAfterVisual = Math.max(0, transcript.scrollHeight - transcript.clientHeight);
      var anchoredTopAfterVisual = maxScrollTopAfterVisual - preserveDistanceAfterVisual;
      if (!Number.isFinite(anchoredTopAfterVisual)) {
        return;
      }

      setTranscriptScrollTop(Math.max(0, Math.min(maxScrollTopAfterVisual, anchoredTopAfterVisual)));
    }).catch(function() {
      // Ignore visual rendering failures; transcript already has raw fallback blocks.
    });
  }

  function applyPendingTranscriptEnhancements() {
    if (!transcriptPendingVisualRefresh) {
      return;
    }

    transcriptPendingVisualRefresh = false;
    refreshTranscriptFollowState();
    var shouldStickBottom = transcriptFollowState.enabled;
    var previousTop = transcript.scrollTop;
    var previousDistance = distanceFromBottom(transcript);
    var preserveDistanceAfterVisual = null;
    var nonFollowAnchorTop = previousTop;
    var renderRevision = transcriptRenderRevision;
    var visualRenderTask = runTranscriptEnhancements();

    if (shouldStickBottom) {
      scrollToBottom(transcript);
    } else {
      preserveDistanceAfterVisual = Number.isFinite(previousDistance) ? previousDistance : null;
      nonFollowAnchorTop = transcript.scrollTop;
    }

    applyTranscriptVisualAnchoringAsync(
      visualRenderTask,
      renderRevision,
      shouldStickBottom,
      preserveDistanceAfterVisual,
      nonFollowAnchorTop);
  }

  window.ixSetTranscript = function(html) {
    refreshTranscriptFollowState();
    var shouldStickBottom = transcriptFollowState.enabled;
    var previousTop = transcript.scrollTop;
    var previousDistance = distanceFromBottom(transcript);
    var preserveDistanceAfterVisual = null;
    var nonFollowAnchorTop = previousTop;
    var renderRevision = ++transcriptRenderRevision;
    var visualRenderTask = null;
    var shouldDeferEnhancements = state.sending === true;
    var nextHtml = html || "";
    if (transcriptLastHtml === nextHtml) {
      return;
    }
    if (window.ixDisposeTranscriptVisuals) {
      window.ixDisposeTranscriptVisuals(transcript);
    }
    transcript.innerHTML = nextHtml;
    transcriptLastHtml = nextHtml;
    if (shouldDeferEnhancements) {
      transcriptPendingVisualRefresh = true;
    } else {
      transcriptPendingVisualRefresh = false;
      visualRenderTask = runTranscriptEnhancements();
    }
    if (shouldStickBottom) {
      scrollToBottom(transcript);
    } else {
      preserveDistanceAfterVisual = Number.isFinite(previousDistance) ? previousDistance : null;
      var restoreTop = previousTop;
      if (Number.isFinite(previousDistance)) {
        var maxScrollTop = Math.max(0, transcript.scrollHeight - transcript.clientHeight);
        var anchoredTop = transcript.scrollHeight - transcript.clientHeight - previousDistance;
        if (Number.isFinite(anchoredTop)) {
          restoreTop = Math.max(0, Math.min(maxScrollTop, anchoredTop));
        }
      }
      setTranscriptScrollTop(restoreTop);
      nonFollowAnchorTop = transcript.scrollTop;
    }

    applyTranscriptVisualAnchoringAsync(
      visualRenderTask,
      renderRevision,
      shouldStickBottom,
      preserveDistanceAfterVisual,
      nonFollowAnchorTop);
  };
