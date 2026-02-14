  var dataViewPanel = byId("dataViewPanel");
  var dataViewBackdrop = byId("dataViewBackdrop");
  var dataViewTitle = byId("dataViewTitle");
  var dataViewMeta = byId("dataViewMeta");
  var dataViewFeedback = byId("dataViewFeedback");
  var dataViewExportActions = byId("dataViewExportActions");
  var dataViewExportPath = byId("dataViewExportPath");
  var btnDataViewOpenExport = byId("btnDataViewOpenExport");
  var btnDataViewRevealExport = byId("btnDataViewRevealExport");
  var btnDataViewCopyExportPath = byId("btnDataViewCopyExportPath");
  var dataViewTable = byId("dataViewTable");
  var btnDataViewQuickExport = byId("btnDataViewQuickExport");
  var btnDataViewExportCsv = byId("btnDataViewExportCsv");
  var btnDataViewExportXlsx = byId("btnDataViewExportXlsx");
  var btnDataViewExportDocx = byId("btnDataViewExportDocx");
  var dataViewState = {
    title: "Data View",
    rows: [],
    lastExportPath: "",
    lastExportFormat: "",
    api: null
  };
  var dataViewFeedbackClearTimer = 0;
  var pendingExports = Object.create(null);

  function normalizeDataRows(rows) {
    if (!rows || rows.length === 0) {
      return [];
    }

    var normalized = [];
    var maxCols = 0;
    for (var i = 0; i < rows.length; i++) {
      var row = rows[i];
      if (!Array.isArray(row)) {
        continue;
      }

      var current = [];
      for (var c = 0; c < row.length; c++) {
        current.push(String(row[c] == null ? "" : row[c]));
      }

      if (current.length > maxCols) {
        maxCols = current.length;
      }
      normalized.push(current);
    }

    if (normalized.length === 0 || maxCols === 0) {
      return [];
    }

    for (var r = 0; r < normalized.length; r++) {
      while (normalized[r].length < maxCols) {
        normalized[r].push("");
      }
    }

    return normalized;
  }

  function getTableContextTitle(table) {
    var meta = "";
    var title = "Data View";
    var msg = table ? table.closest(".msg") : null;
    if (msg) {
      var metaEl = msg.querySelector(".meta");
      if (metaEl) {
        meta = (metaEl.textContent || "").trim();
      }

      var roleEl = msg.closest(".msg-row");
      if (roleEl) {
        if (roleEl.classList.contains("tools")) {
          title = "Tool Data View";
        } else if (roleEl.classList.contains("assistant")) {
          title = "Assistant Data View";
        } else if (roleEl.classList.contains("user")) {
          title = "Message Data View";
        }
      }
    }

    return {
      title: title,
      meta: meta
    };
  }

  function destroyDataViewTable() {
    if (dataViewState.api) {
      try {
        dataViewState.api.destroy();
      } catch (_) {
        // ignore
      }
      dataViewState.api = null;
    }

    if (dataViewTable) {
      dataViewTable.innerHTML = "";
    }
  }

  function renderDataViewTable() {
    if (!dataViewTable) {
      return;
    }

    destroyDataViewTable();

    var rows = dataViewState.rows || [];
    if (rows.length === 0) {
      dataViewMeta.textContent = "No rows available.";
      return;
    }

    var headers = rows[0];
    var bodyRows = rows.slice(1);
    if (headers.length === 0) {
      return;
    }

    if (typeof window.DataTable !== "function") {
      // Fallback plain rendering.
      var thead = document.createElement("thead");
      var trHead = document.createElement("tr");
      for (var h = 0; h < headers.length; h++) {
        var th = document.createElement("th");
        th.textContent = headers[h] || ("Column " + String(h + 1));
        trHead.appendChild(th);
      }
      thead.appendChild(trHead);
      dataViewTable.appendChild(thead);

      var tbody = document.createElement("tbody");
      for (var r = 0; r < bodyRows.length; r++) {
        var tr = document.createElement("tr");
        for (var c = 0; c < headers.length; c++) {
          var td = document.createElement("td");
          td.textContent = bodyRows[r][c] || "";
          tr.appendChild(td);
        }
        tbody.appendChild(tr);
      }
      dataViewTable.appendChild(tbody);
      return;
    }

    var columns = [];
    for (var i = 0; i < headers.length; i++) {
      columns.push({
        title: headers[i] || ("Column " + String(i + 1)),
        data: i,
        defaultContent: ""
      });
    }

    var pageLength = bodyRows.length > 300 ? 100 : (bodyRows.length > 100 ? 50 : 25);
    dataViewState.api = new window.DataTable(dataViewTable, {
      data: bodyRows,
      columns: columns,
      paging: bodyRows.length > 25,
      pageLength: pageLength,
      searching: true,
      ordering: true,
      info: true,
      autoWidth: false,
      scrollX: true,
      scrollY: "100%",
      order: [],
      language: {
        search: "",
        searchPlaceholder: "Filter data..."
      }
    });
  }

  function setDataViewMeta(metaText) {
    var rows = dataViewState.rows || [];
    var rowCount = Math.max(0, rows.length - 1);
    var colCount = rows.length > 0 ? rows[0].length : 0;
    var base = String(rowCount) + " rows · " + String(colCount) + " columns";
    if (metaText) {
      dataViewMeta.textContent = base + " · " + metaText;
    } else {
      dataViewMeta.textContent = base;
    }
  }

  function getDataViewExportButtons() {
    return [btnDataViewQuickExport, btnDataViewExportCsv, btnDataViewExportXlsx, btnDataViewExportDocx];
  }

  function clearDataViewFeedbackTimer() {
    if (dataViewFeedbackClearTimer) {
      window.clearTimeout(dataViewFeedbackClearTimer);
      dataViewFeedbackClearTimer = 0;
    }
  }

  function setDataViewFeedback(text, tone, autoClearMs) {
    if (!dataViewFeedback) {
      return;
    }

    clearDataViewFeedbackTimer();
    dataViewFeedback.classList.remove("show", "ok", "warn", "bad", "info");

    var content = String(text || "").trim();
    if (!content) {
      dataViewFeedback.textContent = "";
      return;
    }

    dataViewFeedback.textContent = content;
    dataViewFeedback.classList.add("show");

    var normalizedTone = String(tone || "").toLowerCase();
    if (normalizedTone === "ok" || normalizedTone === "warn" || normalizedTone === "bad" || normalizedTone === "info") {
      dataViewFeedback.classList.add(normalizedTone);
    } else {
      dataViewFeedback.classList.add("info");
    }

    var timeout = Number(autoClearMs);
    if (Number.isFinite(timeout) && timeout > 0) {
      dataViewFeedbackClearTimer = window.setTimeout(function() {
        setDataViewFeedback("", "info", 0);
      }, timeout);
    }
  }

  function setDataViewExportButtonsBusy() {
    var busy = false;
    for (var key in pendingExports) {
      if (Object.prototype.hasOwnProperty.call(pendingExports, key)) {
        busy = true;
        break;
      }
    }

    var buttons = getDataViewExportButtons();
    for (var i = 0; i < buttons.length; i++) {
      if (!buttons[i]) {
        continue;
      }

      buttons[i].disabled = busy;
      buttons[i].classList.toggle("busy", busy);
    }
  }

  function getFormatLabel(format) {
    var normalized = String(format || "").toLowerCase();
    if (normalized === "xlsx") {
      return "Excel";
    }
    if (normalized === "docx") {
      return "Word";
    }
    if (normalized === "csv") {
      return "CSV";
    }
    if (normalized.length > 0) {
      return normalized.toUpperCase();
    }
    return "file";
  }

  function normalizeExportSaveModeForDataView(value) {
    var normalized = String(value || "").toLowerCase();
    return normalized === "remember" ? "remember" : "ask";
  }

  function normalizeExportFormatForDataView(value) {
    var normalized = String(value || "").toLowerCase();
    if (normalized === "excel") return "xlsx";
    if (normalized === "word") return "docx";
    if (normalized === "csv" || normalized === "xlsx" || normalized === "docx") return normalized;
    return "xlsx";
  }

  function getExportPreferences() {
    var options = state && state.options ? state.options : {};
    var exportPrefs = options.export || {};
    return {
      saveMode: normalizeExportSaveModeForDataView(exportPrefs.saveMode),
      defaultFormat: normalizeExportFormatForDataView(exportPrefs.defaultFormat),
      lastDirectory: String(exportPrefs.lastDirectory || "").trim()
    };
  }

  function getExportFileExtension(format) {
    var normalized = normalizeExportFormatForDataView(format);
    if (normalized === "docx") return ".docx";
    if (normalized === "xlsx") return ".xlsx";
    return ".csv";
  }

  function buildExportTimestamp() {
    var now = new Date();
    function two(n) {
      return n < 10 ? "0" + String(n) : String(n);
    }
    return String(now.getFullYear())
      + two(now.getMonth() + 1)
      + two(now.getDate())
      + "-"
      + two(now.getHours())
      + two(now.getMinutes())
      + two(now.getSeconds());
  }

  function sanitizeExportFileStem(title) {
    var stem = String(title || "dataset").trim();
    if (!stem) {
      stem = "dataset";
    }
    stem = stem.replace(/[<>:\"\/\\|?*\u0000-\u001F]/g, "_");
    if (stem.length > 80) {
      stem = stem.substring(0, 80).trim();
    }
    return stem || "dataset";
  }

  function joinExportPath(directory, fileName) {
    var dir = String(directory || "").trim();
    if (!dir) {
      return "";
    }

    if (dir.endsWith("\\") || dir.endsWith("/")) {
      return dir + fileName;
    }

    var separator = dir.indexOf("\\") >= 0 ? "\\" : "/";
    return dir + separator + fileName;
  }

  function getDirectoryFromPath(path) {
    var full = String(path || "").trim();
    if (!full) {
      return "";
    }

    var slash = full.lastIndexOf("/");
    var backslash = full.lastIndexOf("\\");
    var idx = Math.max(slash, backslash);
    if (idx < 0) {
      return "";
    }

    return full.substring(0, idx);
  }

  function buildAutoExportPath(format) {
    var prefs = getExportPreferences();
    if (prefs.saveMode !== "remember" || !prefs.lastDirectory) {
      return "";
    }

    var stem = sanitizeExportFileStem(dataViewState.title || "dataset");
    var fileName = stem + "-" + buildExportTimestamp() + getExportFileExtension(format);
    return joinExportPath(prefs.lastDirectory, fileName);
  }

  function buildExportId(format) {
    return "exp-" + String(format || "file") + "-" + Date.now().toString(36) + "-" + Math.floor(Math.random() * 100000).toString(36);
  }

  function fileNameFromPath(path) {
    var raw = String(path || "").trim();
    if (!raw) {
      return "";
    }

    var slash = raw.lastIndexOf("/");
    var backslash = raw.lastIndexOf("\\");
    var idx = Math.max(slash, backslash);
    return idx >= 0 ? raw.substring(idx + 1) : raw;
  }

  function setDataViewExportPath(path, format) {
    dataViewState.lastExportPath = String(path || "").trim();
    dataViewState.lastExportFormat = String(format || "").trim().toLowerCase();

    var hasPath = dataViewState.lastExportPath.length > 0;
    if (dataViewExportActions) {
      dataViewExportActions.classList.toggle("show", hasPath);
    }
    if (dataViewExportPath) {
      dataViewExportPath.textContent = hasPath ? dataViewState.lastExportPath : "";
      dataViewExportPath.title = hasPath ? dataViewState.lastExportPath : "";
    }

    if (btnDataViewOpenExport) {
      btnDataViewOpenExport.disabled = !hasPath;
    }
    if (btnDataViewRevealExport) {
      btnDataViewRevealExport.disabled = !hasPath;
    }
    if (btnDataViewCopyExportPath) {
      btnDataViewCopyExportPath.disabled = !hasPath;
    }
  }

  function triggerDataViewExportAction(action) {
    var path = String(dataViewState.lastExportPath || "").trim();
    if (!path) {
      setDataViewFeedback("No export path available yet.", "warn", 2500);
      return;
    }

    post("data_view_export_action", {
      action: action,
      path: path
    });
  }

