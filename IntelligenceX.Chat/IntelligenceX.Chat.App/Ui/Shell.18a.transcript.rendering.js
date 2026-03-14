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

        if (pre.querySelector("code.language-ix-network, code.language-visnetwork, code.language-network")) {
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
    var nextHtml = html || "";
    if (transcriptLastHtml === nextHtml) {
      return;
    }

    refreshTranscriptFollowState();
    var shouldStickBottom = transcriptFollowState.enabled;
    var previousTop = transcript.scrollTop;
    var previousDistance = distanceFromBottom(transcript);
    var preserveDistanceAfterVisual = null;
    var nonFollowAnchorTop = previousTop;
    var renderRevision = ++transcriptRenderRevision;
    var visualRenderTask = null;
    var shouldDeferEnhancements = state.sending === true;
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
