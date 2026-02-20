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
    if (document.body.classList.contains("data-view-open")) {
      return false;
    }

    if (document.activeElement === promptEl) {
      return false;
    }

    var step = Math.max(120, Math.floor(transcript.clientHeight * 0.9));
    switch (e.key) {
      case "PageDown":
        transcript.scrollTop += step;
        return true;
      case "PageUp":
        transcript.scrollTop -= step;
        return true;
      case "Home":
        transcript.scrollTop = 0;
        return true;
      case "End":
        transcript.scrollTop = transcript.scrollHeight;
        return true;
      default:
        return false;
    }
  }

  function handleDataViewNavKey(e) {
    if (!document.body.classList.contains("data-view-open")) {
      return false;
    }

    var target = byId("dataViewBody");
    if (!target) {
      return false;
    }

    if (isEditableElement(document.activeElement)) {
      return false;
    }

    var step = Math.max(140, Math.floor(target.clientHeight * 0.9));
    switch (e.key) {
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

  function handleOptionsNavKey(e) {
    if (!document.body.classList.contains("options-open")) {
      return false;
    }

    if (!optionsBody) {
      return false;
    }

    if (document.activeElement === promptEl || document.activeElement === byId("optPersona")) {
      return false;
    }

    var step = Math.max(120, Math.floor(optionsBody.clientHeight * 0.9));
    switch (e.key) {
      case "PageDown":
        optionsBody.scrollTop += step;
        return true;
      case "PageUp":
        optionsBody.scrollTop -= step;
        return true;
      case "Home":
        optionsBody.scrollTop = 0;
        return true;
      case "End":
        optionsBody.scrollTop = optionsBody.scrollHeight;
        return true;
      default:
        return false;
    }
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
    if (typeof nextState.windowMaximized === "boolean") {
      state.windowMaximized = nextState.windowMaximized;
    }

    updateStatusVisual(state.status, state.statusTone);
    updateWindowControlsState();
    updateMenuState();
    updateComposerState();
    renderAutonomy();
    renderDebugPanel();
  };

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
    state.options.localModel = nextOptions.localModel || state.options.localModel;
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

  function isNearBottom(el) {
    return el.scrollHeight - el.scrollTop - el.clientHeight < 80;
  }

  function scrollToBottom(el) {
    el.scrollTop = el.scrollHeight;
  }

  window.ixSetActivity = function(text, timeline) {
    var el = byId("activity");
    var label = el.querySelector(".activity-text");
    if (text) {
      var timelineSummary = "";
      if (Array.isArray(timeline) && timeline.length > 0) {
        state.activityTimeline = timeline;
        timelineSummary = " | " + timeline.join(" > ");
      }
      label.textContent = text + timelineSummary;
      el.classList.add("active");
      if (isNearBottom(transcript)) {
        scrollToBottom(transcript);
      }
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

      if (document.body.classList.contains("options-open")) {
        var optionsTarget = optionsBody;
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
              zone: "options"
            });
          }
        }
        return;
      }

      if (document.body.classList.contains("data-view-open")) {
        var dataTarget = dataViewBody;
        if (dataTarget) {
          var dataBefore = dataTarget.scrollTop;
          dataTarget.scrollTop += amount;
          if (window.ixWheelDiagRecord) {
            window.ixWheelDiagRecord(dataTarget.scrollTop !== dataBefore ? "applied" : "not_applied", {
              deltaY: amount,
              zone: "dataView"
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

  window.ixSetTranscript = function(html) {
    var shouldStickBottom = isNearBottom(transcript);
    var previousTop = transcript.scrollTop;
    if (window.ixDisposeTranscriptVisuals) {
      window.ixDisposeTranscriptVisuals(transcript);
    }
    transcript.innerHTML = html || "";
    if (window.ixRenderTranscriptVisuals) {
      window.ixRenderTranscriptVisuals(transcript);
    }
    if (window.ixEnhanceTranscriptTables) {
      window.ixEnhanceTranscriptTables(transcript);
    }
    if (window.ixExtractToolDataViewPayloads) {
      window.ixExtractToolDataViewPayloads(transcript);
    }
    setupCodeCopyButtons();
    setupTableCopyButtons();
    if (shouldStickBottom) {
      transcript.scrollTop = transcript.scrollHeight;
    } else {
      transcript.scrollTop = previousTop;
    }
  };
