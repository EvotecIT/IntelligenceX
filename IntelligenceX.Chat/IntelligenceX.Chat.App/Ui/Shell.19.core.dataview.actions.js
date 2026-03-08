  var toolDataViewRowsByTableId = Object.create(null);
  var toolDataViewTableIdCounter = 0;
  var dataViewSessionCounter = 0;
  var activeDataViewSessionId = 0;

  function clearToolDataViewRowsCache() {
    toolDataViewRowsByTableId = Object.create(null);
  }

  function getOrCreateTableId(table) {
    if (!table) {
      return "";
    }

    var current = String(table.dataset.ixTableId || "").trim();
    if (current) {
      return current;
    }

    toolDataViewTableIdCounter += 1;
    var next = "ix-table-" + toolDataViewTableIdCounter.toString(36);
    table.dataset.ixTableId = next;
    return next;
  }

  function findPayloadTargetTable(pre) {
    if (!pre) {
      return null;
    }

    var cursor = pre.nextElementSibling;
    while (cursor) {
      if (cursor.tagName === "TABLE") {
        return cursor;
      }
      if (cursor.querySelector) {
        var nested = cursor.querySelector("table");
        if (nested) {
          return nested;
        }
      }
      cursor = cursor.nextElementSibling;
    }

    return null;
  }

  function parseToolDataViewPayload(raw) {
    var text = String(raw || "").trim();
    if (!text) {
      return null;
    }

    try {
      var payload = JSON.parse(text);
      if (!payload || String(payload.kind || "").toLowerCase() !== "ix_tool_dataview_v1") {
        return null;
      }

      var rows = normalizeDataRows(Array.isArray(payload.rows) ? payload.rows : []);
      if (!rows || rows.length === 0) {
        return null;
      }

      return rows;
    } catch (_) {
      return null;
    }
  }

  function isDataViewPayloadCodeBlock(code) {
    if (!code) {
      return false;
    }

    var className = String(code.className || "").toLowerCase();
    return className.indexOf("language-ix-dataview") >= 0 || className.indexOf("ix-dataview") >= 0;
  }

  window.ixExtractToolDataViewPayloads = function(root) {
    clearToolDataViewRowsCache();
    if (!root) {
      return;
    }

    var codes = root.querySelectorAll(".bubble .markdown-body pre code");
    for (var i = 0; i < codes.length; i++) {
      var code = codes[i];
      var declaredPayload = isDataViewPayloadCodeBlock(code);
      if (!declaredPayload) {
        continue;
      }

      var pre = code.closest ? code.closest("pre") : null;
      var payloadRows = parseToolDataViewPayload(code.textContent || "");

      if (payloadRows && pre) {
        var targetTable = findPayloadTargetTable(pre);
        if (targetTable) {
          var tableId = getOrCreateTableId(targetTable);
          if (tableId) {
            toolDataViewRowsByTableId[tableId] = payloadRows;
          }
        }
      }

      if (pre && pre.parentElement) {
        pre.parentElement.removeChild(pre);
      }
    }
  };

  window.ixGetDataViewRowsForTable = function(table) {
    if (!table) {
      return null;
    }

    var tableId = String(table.dataset.ixTableId || "").trim();
    if (!tableId) {
      return null;
    }

    if (!Object.prototype.hasOwnProperty.call(toolDataViewRowsByTableId, tableId)) {
      return null;
    }

    return normalizeDataRows(toolDataViewRowsByTableId[tableId] || []);
  };

  function openDataView(rows, title, metaText) {
    dataViewState.rows = normalizeDataRows(rows);
    if (!dataViewState.rows || dataViewState.rows.length === 0) {
      return;
    }

    dataViewSessionCounter += 1;
    activeDataViewSessionId = dataViewSessionCounter;
    pendingExports = Object.create(null);
    setDataViewExportButtonsBusy();
    setDataViewFeedback("", "info", 0);
    setDataViewExportPath("", "");

    dataViewState.title = title || "Data View";
    if (dataViewTitle) {
      dataViewTitle.textContent = dataViewState.title;
    }
    setDataViewMeta(metaText || "");
    if (typeof updateDataViewQuickExportLabel === "function") {
      updateDataViewQuickExportLabel();
    }

    document.body.classList.add("data-view-open");
    if (dataViewPanel) {
      dataViewPanel.setAttribute("aria-hidden", "false");
    }

    if (typeof ensureDataTablesReady === "function") {
      ensureDataTablesReady().finally(function() {
        renderDataViewTable();
      });
    } else {
      renderDataViewTable();
    }
  }

  function closeDataView() {
    document.body.classList.remove("data-view-open");
    if (dataViewPanel) {
      dataViewPanel.setAttribute("aria-hidden", "true");
    }
    clearDataViewFeedbackTimer();
  }

  function rowsToSeparated(rows, separator) {
    var lines = [];
    for (var r = 0; r < rows.length; r++) {
      var values = [];
      for (var c = 0; c < rows[r].length; c++) {
        var text = String(rows[r][c] == null ? "" : rows[r][c]);
        if (separator === "," && (text.indexOf(",") >= 0 || text.indexOf("\"") >= 0 || text.indexOf("\n") >= 0 || text.indexOf("\r") >= 0)) {
          text = "\"" + text.replace(/\"/g, "\"\"") + "\"";
        }
        values.push(text);
      }
      lines.push(values.join(separator));
    }
    return lines.join("\n");
  }

  function exportDataView(format) {
    var rows = dataViewState.rows || [];
    if (rows.length === 0) {
      return;
    }

    var resolvedFormat = normalizeExportFormatForDataView(format || getExportPreferences().defaultFormat);
    var exportId = buildExportId(resolvedFormat);
    pendingExports[exportId] = {
      format: resolvedFormat,
      startedAt: Date.now(),
      sessionId: activeDataViewSessionId
    };
    setDataViewExportButtonsBusy();

    var autoOutputPath = buildAutoExportPath(resolvedFormat);
    if (autoOutputPath) {
      pendingExports[exportId].path = autoOutputPath;
      setDataViewFeedback("Exporting " + getFormatLabel(resolvedFormat) + " to last folder...", "info", 0);
      post("export_table_artifact", {
        exportId: exportId,
        format: resolvedFormat,
        title: dataViewState.title || "dataset",
        rows: rows,
        outputPath: autoOutputPath
      });
      return;
    }

    setDataViewFeedback("Choose where to save " + getFormatLabel(resolvedFormat) + "...", "info", 0);

    post("pick_export_path", {
      requestId: exportId,
      format: resolvedFormat,
      title: dataViewState.title || "dataset"
    });
  }

  window.ixOnExportPathSelected = function(payload) {
    payload = payload || {};
    var requestId = String(payload.requestId || "");
    if (!requestId) {
      return;
    }

    var pending = pendingExports[requestId];
    if (!pending) {
      return;
    }

    var ok = payload.ok === true;
    var path = String(payload.path || "").trim();
    var message = String(payload.message || "").trim();
    var canceled = payload.canceled === true;

    if (!ok || !path) {
      delete pendingExports[requestId];
      setDataViewExportButtonsBusy();
      if (!message) {
        message = canceled ? "Export canceled." : "Failed to select save location.";
      }
      setDataViewFeedback(message, canceled ? "warn" : "bad", canceled ? 3200 : 0);
      return;
    }

    pending.path = path;
    var selectedDir = getDirectoryFromPath(path);
    if (selectedDir) {
      state.options.export = state.options.export || {};
      state.options.export.lastDirectory = selectedDir;
      if (typeof renderExportPreferences === "function") {
        renderExportPreferences();
      }
    }
    setDataViewFeedback("Exporting " + getFormatLabel(pending.format) + "...", "info", 0);

    var rows = dataViewState.rows || [];
    post("export_table_artifact", {
      exportId: requestId,
      format: pending.format,
      title: dataViewState.title || "dataset",
      rows: rows,
      outputPath: path
    });
  };

  window.ixOnDataViewExportResult = function(payload) {
    payload = payload || {};
    var exportId = String(payload.exportId || "");
    if (!exportId || !Object.prototype.hasOwnProperty.call(pendingExports, exportId)) {
      return;
    }

    var pending = pendingExports[exportId];
    delete pendingExports[exportId];
    if (pending && pending.sessionId && pending.sessionId !== activeDataViewSessionId) {
      setDataViewExportButtonsBusy();
      return;
    }
    setDataViewExportButtonsBusy();

    var ok = payload.ok === true;
    var tone = ok ? "ok" : "bad";
    var message = String(payload.message || "").trim();
    var path = String(payload.filePath || "").trim();
    if (ok && !path && pending && pending.path) {
      path = String(pending.path || "").trim();
    }

    if (!message) {
      if (ok) {
        var fileName = fileNameFromPath(path);
        if (fileName) {
          message = "Exported " + getFormatLabel(payload.format) + ": " + fileName;
        } else {
          message = "Export completed.";
        }
      } else {
        message = "Export failed.";
      }
    }

    if (ok && path) {
      setDataViewExportPath(path, payload.format || "");
      var dir = getDirectoryFromPath(path);
      if (dir) {
        state.options.export = state.options.export || {};
        state.options.export.lastDirectory = dir;
        if (typeof renderExportPreferences === "function") {
          renderExportPreferences();
        }
      }
    }

    setDataViewFeedback(message, tone, ok ? 6000 : 0);
  };

  window.ixOnDataViewActionResult = function(payload) {
    payload = payload || {};
    var ok = payload.ok === true;
    var message = String(payload.message || "").trim();
    if (!message) {
      message = ok ? "Done." : "Action failed.";
    }

    setDataViewFeedback(message, ok ? "ok" : "bad", ok ? 3500 : 0);
  };

  window.ixCloseDataView = closeDataView;
  window.ixOpenDataViewForTable = function(table) {
    if (!table) {
      return;
    }

    var rows = window.ixGetDataViewRowsForTable ? window.ixGetDataViewRowsForTable(table) : null;
    if (!rows || rows.length === 0) {
      rows = window.ixBuildTableMatrix ? window.ixBuildTableMatrix(table) : null;
    }
    rows = normalizeDataRows(rows || []);
    if (rows.length === 0) {
      return;
    }

    var ctx = getTableContextTitle(table);
    openDataView(rows, ctx.title, ctx.meta);
  };

  if (dataViewBackdrop) {
    dataViewBackdrop.addEventListener("click", closeDataView);
  }

  var btnDataViewClose = byId("btnDataViewClose");
  if (btnDataViewClose) {
    btnDataViewClose.addEventListener("click", closeDataView);
  }

  var btnDataViewCopyTsv = byId("btnDataViewCopyTsv");
  if (btnDataViewCopyTsv) {
    btnDataViewCopyTsv.addEventListener("click", function() {
      var rows = dataViewState.rows || [];
      if (rows.length === 0) {
        return;
      }
      post("omd_copy", { text: rowsToSeparated(rows, "\t") });
    });
  }

  var btnDataViewCopyCsv = byId("btnDataViewCopyCsv");
  if (btnDataViewCopyCsv) {
    btnDataViewCopyCsv.addEventListener("click", function() {
      var rows = dataViewState.rows || [];
      if (rows.length === 0) {
        return;
      }
      post("omd_copy", { text: rowsToSeparated(rows, ",") });
    });
  }

  if (btnDataViewQuickExport) {
    btnDataViewQuickExport.addEventListener("click", function() { exportDataView(""); });
  }

  if (btnDataViewExportCsv) {
    btnDataViewExportCsv.addEventListener("click", function() { exportDataView("csv"); });
  }

  if (btnDataViewExportXlsx) {
    btnDataViewExportXlsx.addEventListener("click", function() { exportDataView("xlsx"); });
  }

  if (btnDataViewExportDocx) {
    btnDataViewExportDocx.addEventListener("click", function() { exportDataView("docx"); });
  }

  if (btnDataViewOpenExport) {
    btnDataViewOpenExport.addEventListener("click", function() {
      triggerDataViewExportAction("open");
    });
  }

  if (btnDataViewRevealExport) {
    btnDataViewRevealExport.addEventListener("click", function() {
      triggerDataViewExportAction("reveal");
    });
  }

  if (btnDataViewCopyExportPath) {
    btnDataViewCopyExportPath.addEventListener("click", function() {
      triggerDataViewExportAction("copy_path");
    });
  }
