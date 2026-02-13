  var ixDataTablesState = {
    loadPromise: null,
    loadFailed: false,
    cssInjected: false,
    nextTableId: 1
  };

  // Local assets are mapped via WebView2 virtual host mapping in MainWindow.
  var ixDataTablesAssets = {
    cssUrl: "https://ixchat.local/vendor/datatables/dataTables.dataTables.min.css",
    jsUrl: "https://ixchat.local/vendor/datatables/dataTables.min.js"
  };

  function ensureDataTablesCss() {
    if (ixDataTablesState.cssInjected) {
      return;
    }

    if (document.querySelector("link[data-ix-datatables='1']")) {
      ixDataTablesState.cssInjected = true;
      return;
    }

    var link = document.createElement("link");
    link.rel = "stylesheet";
    link.href = ixDataTablesAssets.cssUrl;
    link.setAttribute("data-ix-datatables", "1");
    document.head.appendChild(link);
    ixDataTablesState.cssInjected = true;
  }

  function loadScriptOnce(url, attrName) {
    var existing = document.querySelector("script[" + attrName + "='1']");
    if (existing) {
      if (window.DataTable) {
        return Promise.resolve(true);
      }

      return new Promise(function(resolve) {
        existing.addEventListener("load", function() { resolve(!!window.DataTable); }, { once: true });
        existing.addEventListener("error", function() { resolve(false); }, { once: true });
      });
    }

    return new Promise(function(resolve) {
      var script = document.createElement("script");
      script.src = url;
      script.async = true;
      script.setAttribute(attrName, "1");
      script.addEventListener("load", function() { resolve(!!window.DataTable); }, { once: true });
      script.addEventListener("error", function() { resolve(false); }, { once: true });
      document.head.appendChild(script);
    });
  }

  function ensureDataTablesReady() {
    if (typeof window.DataTable === "function") {
      return Promise.resolve(true);
    }

    if (ixDataTablesState.loadFailed) {
      return Promise.resolve(false);
    }

    if (ixDataTablesState.loadPromise) {
      return ixDataTablesState.loadPromise;
    }

    ensureDataTablesCss();
    ixDataTablesState.loadPromise = loadScriptOnce(ixDataTablesAssets.jsUrl, "data-ix-datatables-js")
      .then(function(ok) {
        if (!ok || typeof window.DataTable !== "function") {
          ixDataTablesState.loadFailed = true;
          return false;
        }
        return true;
      })
      .catch(function() {
        ixDataTablesState.loadFailed = true;
        return false;
      })
      .finally(function() {
        if (ixDataTablesState.loadFailed) {
          ixDataTablesState.loadPromise = null;
        }
      });

    return ixDataTablesState.loadPromise;
  }

  function normalizeInlineText(value) {
    return String(value == null ? "" : value).replace(/\s+/g, " ").trim();
  }

  function htmlToText(value) {
    var html = String(value == null ? "" : value);
    if (html.indexOf("<") < 0 && html.indexOf("&") < 0) {
      return normalizeInlineText(html);
    }

    var tmp = document.createElement("div");
    tmp.innerHTML = html;
    return normalizeInlineText(tmp.textContent || tmp.innerText || "");
  }

  function ensureTranscriptTableId(table) {
    if (table.dataset.ixTableId) {
      return table.dataset.ixTableId;
    }

    var id = "ixdt-" + String(ixDataTablesState.nextTableId++);
    table.dataset.ixTableId = id;
    return id;
  }

  function ensureDataTableWrap(table) {
    if (!table || !table.parentElement) {
      return null;
    }

    if (table.parentElement.classList.contains("ix-dt-wrap")) {
      return table.parentElement;
    }

    var wrap = document.createElement("div");
    wrap.className = "ix-dt-wrap";
    wrap.dataset.tableId = ensureTranscriptTableId(table);
    table.parentElement.insertBefore(wrap, table);
    wrap.appendChild(table);
    return wrap;
  }

  function shouldEnhanceTable(table) {
    if (!table) {
      return false;
    }

    var body = table.tBodies && table.tBodies.length > 0 ? table.tBodies[0] : null;
    var rowCount = body ? body.rows.length : Math.max(0, table.rows.length - 1);
    var columnCount = table.tHead && table.tHead.rows.length > 0
      ? table.tHead.rows[0].cells.length
      : (table.rows.length > 0 ? table.rows[0].cells.length : 0);

    table.dataset.ixRowCount = String(rowCount);
    table.dataset.ixColCount = String(columnCount);

    if (rowCount < 8) {
      return false;
    }

    // Prevent locking up WebView on extreme in-bubble result sets.
    if (rowCount > 5000) {
      return false;
    }

    return columnCount > 0;
  }

  function buildMatrixFromDom(table) {
    var lines = [];
    var headRows = table.querySelectorAll("thead tr");
    if (headRows.length > 0) {
      var headerCells = headRows[headRows.length - 1].querySelectorAll("th, td");
      var header = [];
      for (var h = 0; h < headerCells.length; h++) {
        header.push(normalizeInlineText(headerCells[h].textContent));
      }
      if (header.length > 0) {
        lines.push(header);
      }
    }

    var bodyRows = table.querySelectorAll("tbody tr");
    for (var r = 0; r < bodyRows.length; r++) {
      var rowCells = bodyRows[r].querySelectorAll("th, td");
      var row = [];
      for (var c = 0; c < rowCells.length; c++) {
        row.push(normalizeInlineText(rowCells[c].textContent));
      }
      if (row.length > 0) {
        lines.push(row);
      }
    }

    if (lines.length > 0) {
      return lines;
    }

    var allRows = table.querySelectorAll("tr");
    for (var i = 0; i < allRows.length; i++) {
      var cells = allRows[i].querySelectorAll("th, td");
      var values = [];
      for (var j = 0; j < cells.length; j++) {
        values.push(normalizeInlineText(cells[j].textContent));
      }
      if (values.length > 0) {
        lines.push(values);
      }
    }

    return lines;
  }

  function buildMatrixFromDataTable(table) {
    var api = table && table._ixDataTableApi;
    if (!api) {
      return null;
    }

    var rows = [];
    var headers = [];

    try {
      var headerNodes = api.columns().header().toArray();
      for (var h = 0; h < headerNodes.length; h++) {
        headers.push(normalizeInlineText(headerNodes[h].textContent));
      }
      if (headers.length > 0) {
        rows.push(headers);
      }
    } catch (_) {
      // Fallback to data-only export.
    }

    try {
      var data = api.rows({ search: "applied", order: "applied" }).data().toArray();
      for (var r = 0; r < data.length; r++) {
        var sourceRow = data[r];
        if (Array.isArray(sourceRow)) {
          var arrRow = [];
          for (var c = 0; c < sourceRow.length; c++) {
            arrRow.push(htmlToText(sourceRow[c]));
          }
          rows.push(arrRow);
          continue;
        }

        if (sourceRow && typeof sourceRow === "object") {
          var values = [];
          var keys = Object.keys(sourceRow);
          for (var k = 0; k < keys.length; k++) {
            values.push(htmlToText(sourceRow[keys[k]]));
          }
          if (values.length > 0) {
            rows.push(values);
          }
        }
      }
    } catch (_) {
      return null;
    }

    return rows.length > 0 ? rows : null;
  }

  function initTranscriptDataTable(table) {
    if (!table || table.dataset.ixDtEnhanced === "1") {
      return;
    }

    var rowCount = Number(table.dataset.ixRowCount || "0");
    var enablePaging = rowCount > 20;
    var enableInfo = rowCount > 12;
    var pageLength = rowCount > 120 ? 50 : (rowCount > 40 ? 25 : 10);

    try {
      table.classList.add("ix-dt-table");
      var api = new window.DataTable(table, {
        paging: enablePaging,
        pageLength: pageLength,
        lengthChange: enablePaging,
        searching: true,
        ordering: true,
        info: enableInfo,
        autoWidth: false,
        scrollX: true,
        order: [],
        language: {
          search: "",
          searchPlaceholder: "Filter rows..."
        },
        layout: {
          topStart: "search",
          topEnd: enablePaging ? "pageLength" : null,
          bottomStart: enableInfo ? "info" : null,
          bottomEnd: enablePaging ? "paging" : null
        }
      });
      table._ixDataTableApi = api;
      table.dataset.ixDtEnhanced = "1";
    } catch (_) {
      table.dataset.ixDtEnhanced = "0";
    }
  }

  function markTableDataHint(table) {
    if (!table) {
      return;
    }

    var wrap = table.closest(".ix-dt-wrap");
    if (!wrap) {
      return;
    }

    var rowCount = Number(table.dataset.ixRowCount || "0");
    if (rowCount < 8) {
      wrap.classList.add("ix-dt-plain");
      return;
    }

    wrap.classList.add("ix-dt-pending");
  }

  window.ixBuildTableMatrix = function(table) {
    return buildMatrixFromDataTable(table) || buildMatrixFromDom(table);
  };

  window.ixEnhanceTranscriptTables = function(root) {
    if (!root) {
      return;
    }

    var tables = root.querySelectorAll(".bubble .markdown-body table");
    if (!tables || tables.length === 0) {
      return;
    }

    var candidates = [];
    for (var i = 0; i < tables.length; i++) {
      var table = tables[i];
      ensureTranscriptTableId(table);
      ensureDataTableWrap(table);
      if (shouldEnhanceTable(table)) {
        candidates.push(table);
      } else {
        markTableDataHint(table);
      }
    }

    if (candidates.length === 0) {
      return;
    }

    ensureDataTablesReady().then(function(ok) {
      if (!ok) {
        for (var i = 0; i < candidates.length; i++) {
          var wrap = candidates[i].closest(".ix-dt-wrap");
          if (wrap) {
            wrap.classList.add("ix-dt-unavailable");
          }
        }
        return;
      }

      for (var i = 0; i < candidates.length; i++) {
        initTranscriptDataTable(candidates[i]);
        var wrap = candidates[i].closest(".ix-dt-wrap");
        if (wrap) {
          wrap.classList.remove("ix-dt-pending");
          wrap.classList.add("ix-dt-ready");
        }
      }

      if (root && typeof root.scrollHeight === "number") {
        root.scrollTop = root.scrollHeight;
      }
    });
  };
