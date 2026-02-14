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
