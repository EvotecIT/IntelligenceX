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

    // Avoid falling back to document.activeElement for document targets.
    // In WebView2 this can point at the prompt while wheel happens elsewhere.
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

  function isEditableWheelTarget(el) {
    if (typeof isEditableElement === "function") {
      return isEditableElement(el);
    }

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

    return !!el.isContentEditable;
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
    var modalMode = getActiveModalMode();
    var zone = (modalMode === IX_MODAL_MODE_NONE && !transcript)
      ? ""
      : resolveWheelZoneName(modalMode);

    var inTranscript = targetEl && targetEl.closest ? targetEl.closest("#transcript") : null;
    if (!inTranscript) {
      inTranscript = eventPathContainsSelector(e, "#transcript");
    }

    if (modalMode === IX_MODAL_MODE_NONE && zone === "transcript" && !inTranscript && transcript) {
      inTranscript = transcript;
      wheelDiag.counters.fallbackTranscript++;
      recordWheelDiag("fallback_transcript", { deltaY: Number(deltaY) });
    }

    if (!zone) {
      wheelDiag.counters.noZone++;
      recordWheelDiag("no_zone", { deltaY: Number(deltaY) });
      return;
    }

    if (modalMode === IX_MODAL_MODE_NONE && isEditableWheelTarget(targetEl) && !inTranscript) {
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
        zone: zone
      });
      e.preventDefault();
      return;
    }

    wheelDiag.counters.notApplied++;
    recordWheelDiag("not_applied", {
      deltaY: Number(deltaY),
      zone: zone
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
  var visualWheelBody = byId("visualViewBody");
  attachWheelListeners(visualWheelBody);
  recordWheelDiag("wheel_listeners_attached", {
    hasWindow: !!window,
    hasDocument: !!document,
    hasTranscript: !!transcript,
    hasOptionsBody: !!optionsBody,
    hasDataViewBody: !!dataViewBody,
    hasVisualViewBody: !!visualWheelBody
  });

  transcript.addEventListener("pointerdown", function() {
    transcript.focus();
  });

  document.addEventListener("click", function(e) {
    var actionBtn = e.target.closest(".ix-action-btn");
    if (actionBtn && !actionBtn.disabled) {
      var cmd = (actionBtn.getAttribute("data-act-cmd") || "").trim();
      if (!cmd) {
        return;
      }

      post("send", { text: cmd });
      actionBtn.classList.add("done");
      setTimeout(function() {
        actionBtn.classList.remove("done");
      }, 900);
      return;
    }

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
  setInterval(function() {
    if (!document.body.classList.contains("options-open")) {
      return;
    }
    if (typeof refreshAccountUsageRetryCountdowns === "function") {
      refreshAccountUsageRetryCountdowns();
    }
  }, 15000);
  renderOptions();
})();
