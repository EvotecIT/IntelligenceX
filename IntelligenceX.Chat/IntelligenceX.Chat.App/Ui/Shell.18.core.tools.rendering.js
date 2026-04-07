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
    ensureCustomSelect("optSchedulerScopePack");
    ensureCustomSelect("optSchedulerScopeThread");
    ensureCustomSelect("optSchedulerMaintenancePackId");
    ensureCustomSelect("optSchedulerMaintenanceThreadId");

    renderToolLocalityQuickFilters();
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
    updateRoutingStatusVisual();
  }

  function queueActiveToolsTabRender() {
    if (!optionsPanel || !document.body.classList.contains("options-open")) {
      return;
    }

    var activeTab = optionsPanel.querySelector(".options-tab.active");
    if (!activeTab || activeTab.dataset.tab !== "tools") {
      return;
    }

    var scheduled = false;
    function runRender() {
      renderToolLocalityQuickFilters();
      renderTools();
    }

    var schedule = typeof requestAnimationFrame === "function"
      ? requestAnimationFrame
      : function(callback) { return setTimeout(callback, 0); };
    schedule(function() {
      scheduled = true;
      runRender();
    });
    setTimeout(function() {
      if (!scheduled) {
        runRender();
        return;
      }

      if (state.connected
        && Array.isArray(state.options.tools)
        && state.options.tools.length === 0
        && Array.isArray(state.options.packs)
        && state.options.packs.length === 0) {
        runRender();
      }
    }, 180);
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
    if (typeof nextState.hasExplicitUnauthenticatedProbeSnapshot === "boolean") {
      state.hasExplicitUnauthenticatedProbeSnapshot = nextState.hasExplicitUnauthenticatedProbeSnapshot;
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
    if (Array.isArray(nextState.routingPromptExposureHistory)) {
      state.routingPromptExposureHistory = nextState.routingPromptExposureHistory;
    }
    if (Array.isArray(nextState.statusTimeline)) {
      state.statusTimeline = nextState.statusTimeline;
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
    if (!wasSending && state.sending) {
      // New turn started; keep transcript anchored to latest output when user is already near bottom.
      if (isNearBottom(transcript, TRANSCRIPT_FOLLOW_DISABLE_SENDING_THRESHOLD_PX + 40)) {
        transcriptFollowState.enabled = true;
        scrollToBottom(transcript);
      }
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
    if (Object.prototype.hasOwnProperty.call(nextOptions, "runtimeScheduler")) {
      state.options.runtimeScheduler = nextOptions.runtimeScheduler;
    }
    if (Object.prototype.hasOwnProperty.call(nextOptions, "runtimeSchedulerScoped")) {
      state.options.runtimeSchedulerScoped = nextOptions.runtimeSchedulerScoped;
    }
    if (Object.prototype.hasOwnProperty.call(nextOptions, "runtimeSchedulerGlobal")) {
      state.options.runtimeSchedulerGlobal = nextOptions.runtimeSchedulerGlobal;
    }
    state.options.memoryDebug = nextOptions.memoryDebug || null;
    state.options.startupDiagnostics = nextOptions.startupDiagnostics || null;
    state.options.toolCatalogRoutingCatalog = nextOptions.toolCatalogRoutingCatalog || null;
    state.options.toolCatalogPlugins = Array.isArray(nextOptions.toolCatalogPlugins) ? nextOptions.toolCatalogPlugins : [];
    state.options.toolCatalogCapabilitySnapshot = nextOptions.toolCatalogCapabilitySnapshot || null;
    state.options.latestRoutingPromptExposure = nextOptions.latestRoutingPromptExposure || null;
    var previousDebug = state.options.debug && typeof state.options.debug === "object"
      ? state.options.debug
      : { showTurnTrace: false, showDraftBubbles: false };
    var incomingDebug = nextOptions.debug && typeof nextOptions.debug === "object"
      ? nextOptions.debug
      : {};
    state.options.debug = {
      showTurnTrace: typeof incomingDebug.showTurnTrace === "boolean"
        ? incomingDebug.showTurnTrace
        : normalizeBool(previousDebug.showTurnTrace),
      showDraftBubbles: typeof incomingDebug.showDraftBubbles === "boolean"
        ? incomingDebug.showDraftBubbles
        : (typeof previousDebug.showDraftBubbles === "boolean" ? previousDebug.showDraftBubbles : false)
    };
    state.options.activeProfileName = nextOptions.activeProfileName || state.options.activeProfileName;
    state.options.profileNames = nextOptions.profileNames || state.options.profileNames;
    if (Object.prototype.hasOwnProperty.call(nextOptions, "activeConversationId")) {
      state.options.activeConversationId = nextOptions.activeConversationId || "";
    }
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
    var incomingHasVisibleToolState = incomingPacks.length > 0 || incomingTools.length > 0;
    var previousHasVisibleToolState = (Array.isArray(state.options.packs) && state.options.packs.length > 0)
      || (Array.isArray(state.options.tools) && state.options.tools.length > 0);
    var activeOptionsTab = optionsPanel ? optionsPanel.querySelector(".options-tab.active") : null;
    var toolsTabOpen = !!activeOptionsTab && activeOptionsTab.dataset.tab === "tools" && document.body.classList.contains("options-open");
    var keepLoadingForConnectedEmptyState = !incomingHasVisibleToolState && state.connected && toolsTabOpen;
    if (typeof nextOptions.toolsLoading === "boolean") {
      state.options.toolsLoading = nextOptions.toolsLoading || keepLoadingForConnectedEmptyState;
    } else {
      state.options.toolsLoading = keepLoadingForConnectedEmptyState;
    }
    if (typeof nextOptions.toolsCatalogPendingCount === "number" && Number.isFinite(nextOptions.toolsCatalogPendingCount)) {
      state.options.toolsCatalogPendingCount = Math.max(0, Math.floor(nextOptions.toolsCatalogPendingCount));
    } else {
      state.options.toolsCatalogPendingCount = 0;
    }

    var preservePreviousTools = !incomingHasVisibleToolState
      && previousHasVisibleToolState
      && (state.options.toolsLoading || state.connected);
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
    queueActiveToolsTabRender();
  };

  var transcriptFollowState = {
    enabled: true,
    suppressScrollEvent: false
  };
  var TRANSCRIPT_FOLLOW_ENABLE_THRESHOLD_PX = 28;
  var TRANSCRIPT_FOLLOW_DISABLE_THRESHOLD_PX = 84;
  var TRANSCRIPT_FOLLOW_DISABLE_SENDING_THRESHOLD_PX = 180;
  var TRANSCRIPT_FOLLOW_USER_UPWARD_SCROLL_MIN_PX = 2;
  var TRANSCRIPT_USER_SCROLL_INTENT_WINDOW_MS = 1400;
  var transcriptLastUserScrollIntentAt = 0;
  var transcriptLastObservedScrollTop = transcript ? transcript.scrollTop : 0;

  function markTranscriptUserScrollIntent() {
    transcriptLastUserScrollIntentAt = Date.now();
  }

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
    transcriptLastObservedScrollTop = transcript.scrollTop;
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
      transcriptLastObservedScrollTop = transcript.scrollTop;
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

  transcript.addEventListener("wheel", markTranscriptUserScrollIntent, { passive: true });
  transcript.addEventListener("pointerdown", markTranscriptUserScrollIntent);
  transcript.addEventListener("touchstart", markTranscriptUserScrollIntent, { passive: true });
  transcript.addEventListener("scroll", function() {
    if (transcriptFollowState.suppressScrollEvent) {
      return;
    }

    var currentTop = transcript.scrollTop;
    var scrollDelta = currentTop - transcriptLastObservedScrollTop;
    transcriptLastObservedScrollTop = currentTop;

    var allowDisable = true;
    if (state.sending === true && transcriptFollowState.enabled) {
      var userIntentAge = Date.now() - transcriptLastUserScrollIntentAt;
      var hasRecentUserIntent = userIntentAge >= 0 && userIntentAge <= TRANSCRIPT_USER_SCROLL_INTENT_WINDOW_MS;
      var userScrolledUp = scrollDelta <= -TRANSCRIPT_FOLLOW_USER_UPWARD_SCROLL_MIN_PX;
      allowDisable = hasRecentUserIntent && userScrolledUp;
    }

    refreshTranscriptFollowState({ allowDisable: allowDisable, allowEnable: true });
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
    var wasActive = el.classList.contains("active");
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
      if (!wasActive) {
        refreshTranscriptFollowState();
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
