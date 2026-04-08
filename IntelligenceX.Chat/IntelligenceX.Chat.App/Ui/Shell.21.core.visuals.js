  var ixVisualMermaidState = {
    loadPromise: null,
    loadFailed: false,
    initialized: false,
    lastThemeSignature: "",
    nextRenderId: 1,
    maxBlocksPerMessage: 8,
    maxSourceChars: 12000,
    renderTimeoutMs: 3500
  };

  var ixVisualChartState = {
    loadPromise: null,
    loadFailed: false,
    maxBlocksPerMessage: 6,
    maxSourceChars: 20000,
    maxLabels: 200,
    maxDatasets: 12,
    maxPointsPerDataset: 200,
    maxTotalPoints: 1200
  };

  var ixVisualNetworkState = {
    loadPromise: null,
    loadFailed: false,
    maxBlocksPerMessage: 4,
    maxSourceChars: 24000,
    maxNodes: 220,
    maxEdges: 520,
    maxNodeLabelChars: 120,
    maxEdgeLabelChars: 120
  };

  var ixVisualRuntimeDiagnostics = {
    mermaid: {
      script: { state: "idle", url: "", detail: "" },
      ready: false
    },
    chart: {
      script: { state: "idle", url: "", detail: "" },
      ready: false
    },
    network: {
      script: { state: "idle", url: "", detail: "" },
      stylesheet: { state: "idle", url: "", detail: "" },
      ready: false
    }
  };
  var ixVisualMermaidBlockIdCounter = 1;

  // Local assets are mapped via WebView2 virtual host mapping in MainWindow.
  var ixVisualAssets = {
    mermaidUrl: "https://ixchat.local/vendor/mermaid/mermaid.min.js",
    chartJsUrl: "https://ixchat.local/vendor/chartjs/chart.umd.min.js",
    visNetworkJsUrl: "https://ixchat.local/vendor/vis-network/vis-network.min.js",
    visNetworkCssUrl: "https://ixchat.local/vendor/vis-network/vis-network.min.css"
  };
  var ixVisualActionBarState = typeof WeakMap === "function" ? new WeakMap() : new Map();
  var ixNativeVisualRegistry = {
    chart: {
      type: "ix-chart",
      contractKind: "chart",
      legacySelector: ".bubble .markdown-body canvas.omd-chart",
      renderedSelector: ".bubble .markdown-body .omd-visual[data-omd-visual-kind='chart'][data-omd-visual-rendered='true'], .bubble .markdown-body canvas.omd-chart[data-chart-rendered='1']",
      messageWideSelector: ".markdown-body pre[data-visual-chart-rendered='1'], .markdown-body .omd-visual[data-omd-visual-kind='chart'][data-omd-visual-rendered='true'], .markdown-body canvas.omd-chart[data-chart-rendered='1'], .markdown-body .visual-chart-host",
      datasetKey: "visualChartBlockId",
      fallbackHashAttribute: "data-chart-hash",
      fallbackPrefix: "omd-chart",
      cachedSourceAttribute: "data-visual-chart-source",
      fallbackConfigAttribute: "data-chart-config-b64",
      overflowReason: "too many charts",
      unavailableReason: "renderer unavailable",
      getState: function() { return ixVisualChartState; },
      ensureReady: ensureChartReady,
      markInvalid: markChartInvalid,
      renderNative: renderOfficeImoChartBlock,
      renderVisualView: renderChartInVisualView,
      renderExport: renderChartForExport,
      dispose: disposeChartBlock,
      resolveDocxRenderSize: function(width) {
        return {
          width: normalizeVisualRenderDimension(width, ixVisualExportState.chartWidth, exportDocxVisualMaxWidthContract.minPx, exportDocxVisualMaxWidthContract.maxPx),
          height: normalizeVisualRenderDimension(Math.round(width * 0.63), 480, 220, 1600)
        };
      }
    },
    network: {
      type: "ix-network",
      contractKind: "network",
      legacySelector: ".bubble .markdown-body .omd-network",
      renderedSelector: ".bubble .markdown-body .omd-visual[data-omd-visual-kind='network'][data-omd-visual-rendered='true'], .bubble .markdown-body .omd-network[data-network-rendered='1']",
      messageWideSelector: ".markdown-body pre[data-visual-network-rendered='1'], .markdown-body .omd-visual[data-omd-visual-kind='network'][data-omd-visual-rendered='true'], .markdown-body .omd-network[data-network-rendered='1'], .markdown-body .visual-network-host",
      datasetKey: "visualNetworkBlockId",
      fallbackHashAttribute: "data-network-hash",
      fallbackPrefix: "omd-network",
      cachedSourceAttribute: "data-visual-network-source",
      fallbackConfigAttribute: "data-network-config-b64",
      overflowReason: "too many networks",
      unavailableReason: "renderer unavailable",
      getState: function() { return ixVisualNetworkState; },
      ensureReady: ensureNetworkReady,
      markInvalid: markNetworkInvalid,
      renderNative: renderOfficeImoNetworkBlock,
      renderVisualView: renderNetworkInVisualView,
      renderExport: renderNetworkForExport,
      dispose: disposeNetworkBlock,
      resolveDocxRenderSize: function(width) {
        return {
          width: normalizeVisualRenderDimension(width, ixVisualExportState.networkWidth, exportDocxVisualMaxWidthContract.minPx, exportDocxVisualMaxWidthContract.maxPx),
          height: normalizeVisualRenderDimension(Math.round(width * 0.62), 500, 240, 1700)
        };
      }
    }
  };

  function isPlainObject(value) {
    return !!value && typeof value === "object" && !Array.isArray(value);
  }

  function isFiniteNumber(value) {
    return typeof value === "number" && Number.isFinite(value);
  }

  function normalizeText(value) {
    return String(value == null ? "" : value)
      .replace(/\r\n/g, "\n")
      .replace(/\r/g, "\n")
      .trim();
  }

  var mermaidEdgeStatementStartRegex = /[A-Za-z_][A-Za-z0-9_-]*\s+(?:-->|---|-.->|==>)/g;
  var mermaidNodeStatementStartRegex = /^[A-Za-z_][A-Za-z0-9_-]*\s*(?:\[|\(|\{)/;
  var mermaidContinuationKeywords = [
    "subgraph",
    "classDef",
    "class",
    "style",
    "click",
    "linkStyle"
  ];

  function trySplitCompactMermaidDirectiveLine(line) {
    var text = String(line == null ? "" : line);
    if (!text.trim()) {
      return null;
    }

    var leadingWhitespaceMatch = text.match(/^\s*/);
    var leadingWhitespace = leadingWhitespaceMatch ? leadingWhitespaceMatch[0] : "";
    var trimmed = text.slice(leadingWhitespace.length);
    var match = trimmed.match(/^(flowchart|graph)\s+([A-Za-z]+)\s+(.+)$/i);
    if (!match) {
      return null;
    }

    return {
      header: leadingWhitespace + match[1] + " " + match[2],
      content: leadingWhitespace + match[3]
    };
  }

  function trySplitCollapsedMermaidEndLine(line) {
    var text = String(line == null ? "" : line);
    if (!text.trim()) {
      return null;
    }

    var leadingWhitespaceMatch = text.match(/^\s*/);
    var leadingWhitespace = leadingWhitespaceMatch ? leadingWhitespaceMatch[0] : "";
    var trimmed = text.slice(leadingWhitespace.length);
    if (!/^end\s+/i.test(trimmed)) {
      return null;
    }

    var remainder = trimmed.slice(3).trimStart();
    if (!remainder || !looksLikeMermaidContinuationAfterStandaloneEnd(remainder)) {
      return null;
    }

    return {
      first: leadingWhitespace + "end",
      remainder: leadingWhitespace + remainder
    };
  }

  function looksLikeMermaidContinuationAfterStandaloneEnd(remainder) {
    var candidate = String(remainder == null ? "" : remainder).trimStart();
    if (!candidate) {
      return false;
    }

    for (var i = 0; i < mermaidContinuationKeywords.length; i += 1) {
      var keyword = mermaidContinuationKeywords[i];
      if (!candidate.toLowerCase().startsWith(keyword.toLowerCase())) {
        continue;
      }

      if (candidate.length === keyword.length || /\s/.test(candidate.charAt(keyword.length))) {
        return true;
      }
    }

    if (findMermaidEdgeStatementStartIndex(candidate, 0) === 0) {
      return true;
    }

    return mermaidNodeStatementStartRegex.test(candidate);
  }

  function findMermaidEdgeStatementStartIndex(text, startIndex) {
    var source = String(text == null ? "" : text);
    var minimumIndex = Math.max(0, startIndex || 0);
    mermaidEdgeStatementStartRegex.lastIndex = minimumIndex;
    try {
      while (true) {
        var match = mermaidEdgeStatementStartRegex.exec(source);
        if (!match) {
          return -1;
        }

        var index = isFiniteNumber(match.index) ? match.index : -1;
        if (index < 0) {
          continue;
        }

        if (index === 0 || /\s/.test(source.charAt(index - 1))) {
          return index;
        }

        if (mermaidEdgeStatementStartRegex.lastIndex <= index) {
          mermaidEdgeStatementStartRegex.lastIndex = index + 1;
        }
      }
    } finally {
      mermaidEdgeStatementStartRegex.lastIndex = 0;
    }
  }

  function trySplitCollapsedMermaidEdgeStatements(line) {
    var text = String(line == null ? "" : line);
    if (!text.trim()) {
      return null;
    }

    var leadingWhitespaceMatch = text.match(/^\s*/);
    var leadingWhitespace = leadingWhitespaceMatch ? leadingWhitespaceMatch[0] : "";
    var trimmed = text.slice(leadingWhitespace.length);
    var splitIndex = findMermaidEdgeStatementStartIndex(trimmed, 0);
    if (splitIndex < 0) {
      return null;
    }

    splitIndex = findMermaidEdgeStatementStartIndex(trimmed, splitIndex + 1);
    if (!isFiniteNumber(splitIndex) || splitIndex <= 0 || splitIndex >= trimmed.length) {
      return null;
    }

    var first = trimmed.slice(0, splitIndex).trimEnd();
    var remainder = trimmed.slice(splitIndex).trimStart();
    if (!first || !remainder) {
      return null;
    }

    return {
      first: leadingWhitespace + first,
      remainder: leadingWhitespace + remainder
    };
  }

  function normalizeMermaidSourceForRender(source) {
    var normalized = normalizeText(source || "");
    if (!normalized) {
      return "";
    }

    var repaired = [];
    var pending = normalized.split("\n");
    while (pending.length > 0) {
      var current = pending.shift();
      var split = trySplitCompactMermaidDirectiveLine(current);
      if (split) {
        repaired.push(split.header);
        pending.unshift(split.content);
        continue;
      }

      split = trySplitCollapsedMermaidEndLine(current);
      if (split) {
        repaired.push(split.first);
        pending.unshift(split.remainder);
        continue;
      }

      split = trySplitCollapsedMermaidEdgeStatements(current);
      if (split) {
        repaired.push(split.first);
        pending.unshift(split.remainder);
        continue;
      }

      repaired.push(current);
    }

    return repaired.join("\n").trim();
  }

  function decodeBase64Utf8Value(value) {
    var text = String(value || "").trim();
    if (!text) {
      return "";
    }

    var padding = 0;
    if (text.endsWith("==")) {
      padding = 2;
    } else if (text.endsWith("=")) {
      padding = 1;
    }
    var predictedDecodedBytes = Math.max(0, Math.floor(text.length / 4) * 3 - padding);
    var maxDecodedBytes = Math.max(ixVisualChartState.maxSourceChars, ixVisualNetworkState.maxSourceChars) + 1024;
    if (predictedDecodedBytes > maxDecodedBytes) {
      return "";
    }

    try {
      var bytes = Uint8Array.from(atob(text), function(ch) { return ch.charCodeAt(0); });
      if (window.TextDecoder) {
        return new TextDecoder("utf-8").decode(bytes);
      }
      var chunkSize = 8192;
      var decoded = "";
      for (var i = 0; i < bytes.length; i += chunkSize) {
        decoded += String.fromCharCode.apply(null, Array.prototype.slice.call(bytes, i, i + chunkSize));
      }
      return decodeURIComponent(escape(decoded));
    } catch (_) {
      return "";
    }
  }

  function getNativeVisualRegistryEntry(kind) {
    var normalized = normalizeVisualType(kind || "");
    if (normalized === "ix-chart") {
      return ixNativeVisualRegistry.chart;
    }
    if (normalized === "ix-network") {
      return ixNativeVisualRegistry.network;
    }
    return null;
  }

  function getOfficeImoVisualHash(element, fallbackAttribute) {
    if (!element || typeof element.getAttribute !== "function") {
      return "";
    }

    return String(
      element.getAttribute("data-omd-visual-hash")
      || element.getAttribute(fallbackAttribute || "")
      || "").trim();
  }

  function getOfficeImoVisualSource(element, cachedAttribute, fallbackConfigAttribute) {
    if (!element || typeof element.getAttribute !== "function") {
      return "";
    }

    var source = String(element.getAttribute(cachedAttribute || "") || "").trim();
    var sharedConfigB64 = String(element.getAttribute("data-omd-config-b64") || "").trim();
    var hasContract = String(element.getAttribute("data-omd-visual-contract") || "").trim() === "v1";
    var configEncoding = String(element.getAttribute("data-omd-config-encoding") || "").trim().toLowerCase();
    var canDecodeSharedConfig = !!sharedConfigB64 && (!hasContract || !configEncoding || configEncoding === "base64-utf8");
    if (!source && canDecodeSharedConfig) {
      source = decodeBase64Utf8Value(sharedConfigB64);
    }
    if (!source) {
      source = decodeBase64Utf8Value(element.getAttribute(fallbackConfigAttribute || ""));
    }
    return normalizeText(source);
  }

  function getOfficeImoVisualKind(element, fallbackKind) {
    var raw = "";
    if (element && typeof element.getAttribute === "function") {
      raw = String(element.getAttribute("data-omd-visual-kind") || "").trim();
    }

    return normalizeVisualType(raw || fallbackKind || "");
  }

  function getOfficeImoVisualSourceByKind(element, kind) {
    var entry = getNativeVisualRegistryEntry(kind);
    if (!entry) {
      return "";
    }

    return getOfficeImoVisualSource(element, entry.cachedSourceAttribute, entry.fallbackConfigAttribute);
  }

  function getOfficeImoVisualSelector(kind, legacySelector) {
    var normalized = normalizeVisualType(kind || "");
    var contractKind = normalized === "ix-chart"
      ? "chart"
      : (normalized === "ix-network" ? "network" : normalized);
    var sharedSelector = contractKind
      ? ".bubble .markdown-body .omd-visual[data-omd-visual-contract='v1'][data-omd-visual-kind='" + contractKind + "']"
      : "";
    if (!legacySelector) {
      return sharedSelector;
    }
    if (!sharedSelector) {
      return legacySelector;
    }
    return sharedSelector + ", " + legacySelector;
  }

  function collectOfficeImoVisualBlocks(root, kind, legacySelector, datasetKey, fallbackHashAttribute, fallbackPrefix) {
    var selector = getOfficeImoVisualSelector(kind, legacySelector);
    if (!selector) {
      return [];
    }

    var nodes = root.querySelectorAll(selector);
    if (!nodes || nodes.length === 0) {
      return [];
    }

    var blocks = [];
    for (var i = 0; i < nodes.length; i++) {
      var node = nodes[i];
      if (!node || !node.parentElement) {
        continue;
      }

      if (!node.dataset[datasetKey]) {
        node.dataset[datasetKey] = getOfficeImoVisualHash(node, fallbackHashAttribute)
          || (fallbackPrefix + "-" + String(i + 1) + "-" + String(Math.floor(Math.random() * 1000000)));
      }

      blocks.push(node);
    }

    return blocks;
  }

  function collectRegisteredOfficeImoVisualBlocks(root, kind) {
    var entry = getNativeVisualRegistryEntry(kind);
    if (!entry) {
      return [];
    }

    return collectOfficeImoVisualBlocks(
      root,
      entry.type,
      entry.legacySelector,
      entry.datasetKey,
      entry.fallbackHashAttribute,
      entry.fallbackPrefix);
  }

  function compareVisualBlockDocumentOrder(left, right) {
    if (left === right) {
      return 0;
    }
    if (!left || typeof left.compareDocumentPosition !== "function") {
      return -1;
    }
    if (!right || typeof right.compareDocumentPosition !== "function") {
      return 1;
    }

    var relation = left.compareDocumentPosition(right);
    if (relation & Node.DOCUMENT_POSITION_FOLLOWING) {
      return -1;
    }
    if (relation & Node.DOCUMENT_POSITION_PRECEDING) {
      return 1;
    }
    return 0;
  }

  function buildOrderedVisualEntries(fenceBlocks, nativeBlocks) {
    var entries = [];

    for (var i = 0; i < fenceBlocks.length; i++) {
      entries.push({ block: fenceBlocks[i], isNative: false });
    }
    for (var j = 0; j < nativeBlocks.length; j++) {
      entries.push({ block: nativeBlocks[j], isNative: true });
    }

    entries.sort(function(left, right) {
      return compareVisualBlockDocumentOrder(left.block, right.block);
    });

    return entries;
  }

  function ensureVisualNotice(anchor, text) {
    if (!anchor || !anchor.parentElement) {
      return;
    }

    var next = anchor.nextElementSibling;
    if (next && next.classList && next.classList.contains("ix-visual-invalid")) {
      next.textContent = text;
      return;
    }

    var notice = document.createElement("div");
    notice.className = "ix-visual-invalid";
    notice.textContent = text;
    anchor.insertAdjacentElement("afterend", notice);
  }

  function clearVisualNotice(anchor) {
    if (!anchor) {
      return;
    }

    var next = anchor.nextElementSibling;
    if (next && next.classList && next.classList.contains("ix-visual-invalid")) {
      next.remove();
    }
  }

  function getVisualKindLabel(kind) {
    var normalized = normalizeVisualType(kind);
    if (normalized === "mermaid") {
      return "Mermaid Diagram";
    }
    if (normalized === "ix-chart") {
      return "Chart";
    }
    if (normalized === "ix-network") {
      return "Network Diagram";
    }
    return "Visual";
  }

  function markVisualMessageWide(pre) {
    if (!pre || !pre.closest) {
      return;
    }

    var msg = pre.closest(".msg");
    if (msg && msg.classList) {
      msg.classList.add("ix-msg-has-visual");
    }
  }

  function recordVisualRuntimeAssetState(kind, asset, state, url, detail) {
    if (!kind || !asset || !ixVisualRuntimeDiagnostics[kind] || !ixVisualRuntimeDiagnostics[kind][asset]) {
      return;
    }

    var target = ixVisualRuntimeDiagnostics[kind][asset];
    target.state = String(state || "idle");
    target.url = String(url || "");
    target.detail = String(detail || "");
  }

  function recordVisualRuntimeReady(kind, ready) {
    if (!kind || !ixVisualRuntimeDiagnostics[kind]) {
      return;
    }

    ixVisualRuntimeDiagnostics[kind].ready = ready === true;
  }

  window.ixGetVisualRuntimeDiagnostics = function() {
    return JSON.parse(JSON.stringify(ixVisualRuntimeDiagnostics));
  };

  function clearVisualMessageWideWhenEmpty(pre) {
    if (!pre || !pre.closest) {
      return;
    }

    var msg = pre.closest(".msg");
    if (!msg || !msg.classList || !msg.classList.contains("ix-msg-has-visual")) {
      return;
    }

    var selectors = [
      ".markdown-body pre[data-ix-mermaid-rendered='1']"
    ];
    var registryKeys = Object.keys(ixNativeVisualRegistry);
    for (var i = 0; i < registryKeys.length; i++) {
      var entry = ixNativeVisualRegistry[registryKeys[i]];
      if (entry && entry.messageWideSelector) {
        selectors.push(entry.messageWideSelector);
      }
    }

    var hasVisual = msg.querySelector(selectors.join(", "));
    if (!hasVisual) {
      msg.classList.remove("ix-msg-has-visual");
    }
  }

  function getVisualActionBar(pre) {
    if (!pre) {
      return null;
    }

    return ixVisualActionBarState.get(pre) || null;
  }

  function setVisualActionBar(pre, bar) {
    if (!pre) {
      return;
    }

    if (!bar) {
      ixVisualActionBarState.delete(pre);
      return;
    }

    ixVisualActionBarState.set(pre, bar);
  }

  function removeVisualActionBar(pre) {
    if (!pre) {
      return;
    }

    var bar = getVisualActionBar(pre);
    if (bar && bar.parentElement) {
      bar.remove();
    }
    setVisualActionBar(pre, null);
    clearVisualMessageWideWhenEmpty(pre);
  }

  function ensureVisualActionBar(pre, kind) {
    if (!pre || !pre.parentElement) {
      return;
    }

    var existing = getVisualActionBar(pre);
    if (existing && existing.parentElement) {
      return;
    }
    if (existing && !existing.parentElement) {
      setVisualActionBar(pre, null);
    }

    var anchor = pre;
    var next = pre.nextElementSibling;
    if (kind === "ix-chart" && next && next.classList && next.classList.contains("visual-chart-host")) {
      anchor = next;
    } else if (kind === "ix-network" && next && next.classList && next.classList.contains("visual-network-host")) {
      anchor = next;
    }

    var bar = document.createElement("div");
    bar.className = "ix-visual-actions-bar";

    var btnOpen = document.createElement("button");
    btnOpen.type = "button";
    btnOpen.textContent = "Open " + getVisualKindLabel(kind);
    btnOpen.addEventListener("click", function() {
      openVisualViewFromBlock(pre, kind);
    });

    bar.appendChild(btnOpen);
    anchor.insertAdjacentElement("afterend", bar);
    setVisualActionBar(pre, bar);
    markVisualMessageWide(pre);
  }

  function loadVisualScriptOnce(url, attrName, readyCheck, diagnosticsKind, diagnosticsAsset) {
    var isReady = typeof readyCheck === "function"
      ? readyCheck
      : function() { return true; };
    var existing = document.querySelector("script[" + attrName + "='1']");
    if (existing) {
      var existingState = existing.getAttribute("data-ix-load-state") || "";
      if (isReady()) {
        return Promise.resolve(true);
      }
      if (existingState === "error") {
        recordVisualRuntimeAssetState(diagnosticsKind, diagnosticsAsset, "error", url, "existing_tag_error");
        return Promise.resolve(false);
      }
      if (existingState === "loaded") {
        var ready = isReady();
        recordVisualRuntimeAssetState(diagnosticsKind, diagnosticsAsset, ready ? "loaded" : "loaded_not_ready", url, ready ? "" : "ready_check_failed");
        return Promise.resolve(ready);
      }

      return new Promise(function(resolve) {
        existing.addEventListener("load", function() {
          var ready = isReady();
          recordVisualRuntimeAssetState(diagnosticsKind, diagnosticsAsset, ready ? "loaded" : "loaded_not_ready", url, ready ? "" : "ready_check_failed");
          resolve(ready);
        }, { once: true });
        existing.addEventListener("error", function() {
          recordVisualRuntimeAssetState(diagnosticsKind, diagnosticsAsset, "error", url, "load_event_error");
          resolve(false);
        }, { once: true });
      });
    }

    return new Promise(function(resolve) {
      var script = document.createElement("script");
      script.src = url;
      script.async = true;
      script.setAttribute(attrName, "1");
      script.setAttribute("data-ix-load-state", "loading");
      recordVisualRuntimeAssetState(diagnosticsKind, diagnosticsAsset, "loading", url, "");
      script.addEventListener("load", function() {
        script.setAttribute("data-ix-load-state", "loaded");
        var ready = isReady();
        recordVisualRuntimeAssetState(diagnosticsKind, diagnosticsAsset, ready ? "loaded" : "loaded_not_ready", url, ready ? "" : "ready_check_failed");
        resolve(ready);
      }, { once: true });
      script.addEventListener("error", function() {
        script.setAttribute("data-ix-load-state", "error");
        recordVisualRuntimeAssetState(diagnosticsKind, diagnosticsAsset, "error", url, "load_event_error");
        resolve(false);
      }, { once: true });
      document.head.appendChild(script);
    });
  }

  function loadVisualStylesheetOnce(url, attrName, diagnosticsKind, diagnosticsAsset) {
    var existing = document.querySelector("link[" + attrName + "='1']");
    if (existing) {
      var existingState = existing.getAttribute("data-ix-load-state") || "";
      if (existingState === "error") {
        recordVisualRuntimeAssetState(diagnosticsKind, diagnosticsAsset, "error", url, "existing_tag_error");
        return Promise.resolve(false);
      }
      if (existingState === "loaded") {
        recordVisualRuntimeAssetState(diagnosticsKind, diagnosticsAsset, "loaded", url, "");
        return Promise.resolve(true);
      }

      return new Promise(function(resolve) {
        existing.addEventListener("load", function() {
          recordVisualRuntimeAssetState(diagnosticsKind, diagnosticsAsset, "loaded", url, "");
          resolve(true);
        }, { once: true });
        existing.addEventListener("error", function() {
          recordVisualRuntimeAssetState(diagnosticsKind, diagnosticsAsset, "error", url, "load_event_error");
          resolve(false);
        }, { once: true });
      });
    }

    return new Promise(function(resolve) {
      var link = document.createElement("link");
      link.rel = "stylesheet";
      link.href = url;
      link.setAttribute(attrName, "1");
      link.setAttribute("data-ix-load-state", "loading");
      recordVisualRuntimeAssetState(diagnosticsKind, diagnosticsAsset, "loading", url, "");
      link.addEventListener("load", function() {
        link.setAttribute("data-ix-load-state", "loaded");
        recordVisualRuntimeAssetState(diagnosticsKind, diagnosticsAsset, "loaded", url, "");
        resolve(true);
      }, { once: true });
      link.addEventListener("error", function() {
        link.setAttribute("data-ix-load-state", "error");
        recordVisualRuntimeAssetState(diagnosticsKind, diagnosticsAsset, "error", url, "load_event_error");
        resolve(false);
      }, { once: true });
      document.head.appendChild(link);
    });
  }

  function withVisualTimeout(promise, timeoutMs) {
    var timeout = Number(timeoutMs);
    if (!Number.isFinite(timeout) || timeout <= 0) {
      return promise;
    }

    return Promise.race([
      promise,
      new Promise(function(_, reject) {
        setTimeout(function() { reject(new Error("timeout")); }, timeout);
      })
    ]);
  }

  function resolveMermaidThemeVariables(themeMode) {
    var palette = resolveVisualExportPalette(themeMode);
    var accentA = palette.accents[0] || "#4cc3ff";
    var accentB = palette.accents[1] || "#66d9a8";
    return {
      background: palette.background,
      primaryColor: accentA,
      primaryTextColor: palette.text,
      secondaryColor: accentB,
      tertiaryColor: palette.surface,
      lineColor: palette.grid,
      textColor: palette.text,
      mainBkg: palette.surface,
      nodeBorder: palette.grid,
      clusterBkg: palette.background,
      clusterBorder: palette.grid,
      edgeLabelBackground: palette.background,
      actorBorder: palette.grid,
      actorBkg: palette.surface,
      actorTextColor: palette.text,
      labelBoxBkgColor: palette.surface,
      labelBoxBorderColor: palette.grid
    };
  }

  function ensureMermaidThemeInitialized(themeMode, renderProfile) {
    var mermaidRuntime = getMermaidRuntime();
    if (!mermaidRuntime || typeof mermaidRuntime.initialize !== "function") {
      return;
    }

    var normalizedThemeMode = normalizeVisualExportThemeMode(themeMode);
    var normalizedRenderProfile = String(renderProfile || "ui").trim().toLowerCase() === "export"
      ? "export"
      : "ui";
    var themeVariables = resolveMermaidThemeVariables(normalizedThemeMode);
    var signature = normalizedThemeMode + "|" + normalizedRenderProfile + "|" + JSON.stringify(themeVariables);
    if (ixVisualMermaidState.initialized && ixVisualMermaidState.lastThemeSignature === signature) {
      return;
    }

    mermaidRuntime.initialize({
      startOnLoad: false,
      securityLevel: "strict",
      theme: "base",
      themeVariables: themeVariables,
      flowchart: {
        htmlLabels: normalizedRenderProfile !== "export"
      }
    });
    ixVisualMermaidState.initialized = true;
    ixVisualMermaidState.lastThemeSignature = signature;
  }

  function resolveMermaidRuntimeCandidate() {
    if (window.mermaid && typeof window.mermaid.render === "function") {
      return window.mermaid;
    }

    if (typeof globalThis !== "undefined"
      && globalThis.__esbuild_esm_mermaid_nm
      && globalThis.__esbuild_esm_mermaid_nm.mermaid
      && globalThis.__esbuild_esm_mermaid_nm.mermaid.default
      && typeof globalThis.__esbuild_esm_mermaid_nm.mermaid.default.render === "function") {
      return globalThis.__esbuild_esm_mermaid_nm.mermaid.default;
    }

    return null;
  }

  function getMermaidRuntime() {
    var runtime = resolveMermaidRuntimeCandidate();
    if (runtime && window.mermaid !== runtime) {
      window.mermaid = runtime;
    }
    return runtime;
  }

  function ensureMermaidReady() {
    if (getMermaidRuntime()) {
      ensureMermaidThemeInitialized("preserve_ui_theme", "ui");
      return Promise.resolve(true);
    }

    if (ixVisualMermaidState.loadFailed) {
      return Promise.resolve(false);
    }

    if (ixVisualMermaidState.loadPromise) {
      return ixVisualMermaidState.loadPromise;
    }

    ixVisualMermaidState.loadPromise = loadVisualScriptOnce(
      ixVisualAssets.mermaidUrl,
      "data-ix-mermaid-js",
      function() {
        return !!getMermaidRuntime();
      },
      "mermaid",
      "script")
      .then(function(ok) {
        if (!ok || !getMermaidRuntime()) {
          ixVisualMermaidState.loadFailed = true;
          recordVisualRuntimeReady("mermaid", false);
          return false;
        }

        ensureMermaidThemeInitialized("preserve_ui_theme", "ui");
        recordVisualRuntimeReady("mermaid", true);

        return true;
      })
      .catch(function() {
        ixVisualMermaidState.loadFailed = true;
        recordVisualRuntimeReady("mermaid", false);
        return false;
      })
      .finally(function() {
        if (ixVisualMermaidState.loadFailed) {
          ixVisualMermaidState.loadPromise = null;
        }
      });

    return ixVisualMermaidState.loadPromise;
  }

  function markMermaidInvalid(pre, reason) {
    if (!pre) {
      return;
    }

    removeVisualActionBar(pre);

    var source = pre.getAttribute("data-ix-mermaid-source");
    if (typeof source === "string" && source.length > 0) {
      pre.textContent = source;
    }

    pre.setAttribute("data-ix-mermaid-rendered", "0");
    pre.setAttribute("data-ix-mermaid-invalid", "1");
    var suffix = reason ? " (" + reason + ")" : "";
    ensureVisualNotice(pre, "invalid visual block: mermaid" + suffix);
    clearVisualMessageWideWhenEmpty(pre);
  }

  async function renderMermaidBlock(pre) {
    if (!pre || pre.getAttribute("data-ix-mermaid-pending") === "1") {
      return;
    }

    var source = pre.getAttribute("data-ix-mermaid-source");
    if (typeof source !== "string" || source.length === 0) {
      source = normalizeMermaidSourceForRender(pre.textContent || "");
      if (source.length > 0) {
        pre.setAttribute("data-ix-mermaid-source", source);
      }
    } else {
      source = normalizeMermaidSourceForRender(source);
      if (source.length > 0) {
        pre.setAttribute("data-ix-mermaid-source", source);
      }
    }

    if (!source) {
      markMermaidInvalid(pre, "empty source");
      return;
    }

    if (source.length > ixVisualMermaidState.maxSourceChars) {
      markMermaidInvalid(pre, "source too large");
      return;
    }

    pre.setAttribute("data-ix-mermaid-pending", "1");
    try {
      var mermaidRuntime = getMermaidRuntime();
      if (!mermaidRuntime) {
        throw new Error("renderer unavailable");
      }

      ensureMermaidThemeInitialized("preserve_ui_theme", "ui");

      if (typeof mermaidRuntime.parse === "function") {
        var parseResult = mermaidRuntime.parse(source);
        if (parseResult && typeof parseResult.then === "function") {
          await withVisualTimeout(parseResult, ixVisualMermaidState.renderTimeoutMs);
        }
      }

      var renderId = "ix-mermaid-" + String(ixVisualMermaidState.nextRenderId++);
      var renderResult = await withVisualTimeout(
        Promise.resolve(mermaidRuntime.render(renderId, source)),
        ixVisualMermaidState.renderTimeoutMs);

      var svg = "";
      var bindFunctions = null;
      if (typeof renderResult === "string") {
        svg = renderResult;
      } else if (renderResult && typeof renderResult === "object") {
        svg = typeof renderResult.svg === "string" ? renderResult.svg : "";
        bindFunctions = typeof renderResult.bindFunctions === "function" ? renderResult.bindFunctions : null;
      }

      if (!svg) {
        throw new Error("missing svg");
      }

      pre.innerHTML = svg;
      if (bindFunctions) {
        bindFunctions(pre);
      }

      pre.setAttribute("data-ix-mermaid-rendered", "1");
      pre.removeAttribute("data-ix-mermaid-invalid");
      clearVisualNotice(pre);
      ensureVisualActionBar(pre, "mermaid");
    } catch (_) {
      markMermaidInvalid(pre, "parse/render failed");
    } finally {
      pre.removeAttribute("data-ix-mermaid-pending");
    }
  }

  function ensureChartReady() {
    if (window.Chart && typeof window.Chart === "function") {
      return Promise.resolve(true);
    }

    if (ixVisualChartState.loadFailed) {
      return Promise.resolve(false);
    }

    if (ixVisualChartState.loadPromise) {
      return ixVisualChartState.loadPromise;
    }

    ixVisualChartState.loadPromise = loadVisualScriptOnce(
      ixVisualAssets.chartJsUrl,
      "data-ix-chartjs-js",
      function() {
        return !!(window.Chart && typeof window.Chart === "function");
      },
      "chart",
      "script")
      .then(function(ok) {
        if (!ok || !window.Chart || typeof window.Chart !== "function") {
          ixVisualChartState.loadFailed = true;
          recordVisualRuntimeReady("chart", false);
          return false;
        }

        recordVisualRuntimeReady("chart", true);
        return true;
      })
      .catch(function() {
        ixVisualChartState.loadFailed = true;
        recordVisualRuntimeReady("chart", false);
        return false;
      })
      .finally(function() {
        if (ixVisualChartState.loadFailed) {
          ixVisualChartState.loadPromise = null;
        }
      });

    return ixVisualChartState.loadPromise;
  }

  function sanitizeBoundedString(value, maxLength) {
    if (value == null) {
      return "";
    }

    var text = String(value).trim();
    if (!text || text.length > maxLength) {
      return "";
    }
    return text;
  }

  function sanitizeChartColor(value, maxEntries) {
    if (typeof value === "string") {
      return value.length <= 64 ? value : null;
    }

    if (!Array.isArray(value)) {
      return null;
    }

    var count = Math.min(value.length, maxEntries);
    var list = [];
    for (var i = 0; i < count; i++) {
      if (typeof value[i] !== "string") {
        continue;
      }

      var item = value[i].trim();
      if (!item || item.length > 64) {
        continue;
      }

      list.push(item);
    }

    return list.length > 0 ? list : null;
  }

  function sanitizeChartPointByType(value, chartType) {
    if (chartType === "scatter") {
      if (!isPlainObject(value) || !isFiniteNumber(value.x) || !isFiniteNumber(value.y)) {
        return null;
      }
      return { x: value.x, y: value.y };
    }

    if (chartType === "bubble") {
      if (!isPlainObject(value)
        || !isFiniteNumber(value.x)
        || !isFiniteNumber(value.y)
        || !isFiniteNumber(value.r)) {
        return null;
      }
      if (value.r < 0 || value.r > 100) {
        return null;
      }
      return { x: value.x, y: value.y, r: value.r };
    }

    if (value == null) {
      return null;
    }

    if (!isFiniteNumber(value)) {
      return null;
    }

    return value;
  }

  function sanitizeChartDataset(rawDataset, chartType) {
    if (!isPlainObject(rawDataset) || !Array.isArray(rawDataset.data)) {
      return null;
    }

    var points = [];
    var maxPoints = Math.min(rawDataset.data.length, ixVisualChartState.maxPointsPerDataset);
    for (var i = 0; i < maxPoints; i++) {
      var point = sanitizeChartPointByType(rawDataset.data[i], chartType);
      if (point === null && chartType !== "scatter" && chartType !== "bubble") {
        points.push(null);
        continue;
      }

      if (point === null) {
        return null;
      }

      points.push(point);
    }

    if (points.length === 0) {
      return null;
    }

    var result = { data: points };
    var label = sanitizeBoundedString(rawDataset.label, 120);
    if (label) {
      result.label = label;
    }

    var backgroundColor = sanitizeChartColor(rawDataset.backgroundColor, ixVisualChartState.maxLabels);
    if (backgroundColor !== null) {
      result.backgroundColor = backgroundColor;
    }

    var borderColor = sanitizeChartColor(rawDataset.borderColor, ixVisualChartState.maxLabels);
    if (borderColor !== null) {
      result.borderColor = borderColor;
    }

    if (isFiniteNumber(rawDataset.borderWidth) && rawDataset.borderWidth >= 0 && rawDataset.borderWidth <= 12) {
      result.borderWidth = rawDataset.borderWidth;
    }

    if (isFiniteNumber(rawDataset.tension) && rawDataset.tension >= 0 && rawDataset.tension <= 1) {
      result.tension = rawDataset.tension;
    }

    if (isFiniteNumber(rawDataset.pointRadius) && rawDataset.pointRadius >= 0 && rawDataset.pointRadius <= 20) {
      result.pointRadius = rawDataset.pointRadius;
    }

    if (typeof rawDataset.fill === "boolean") {
      result.fill = rawDataset.fill;
    } else {
      var fillValue = sanitizeBoundedString(rawDataset.fill, 24);
      if (fillValue) {
        result.fill = fillValue;
      }
    }

    if (typeof rawDataset.hidden === "boolean") {
      result.hidden = rawDataset.hidden;
    }

    var stack = sanitizeBoundedString(rawDataset.stack, 64);
    if (stack) {
      result.stack = stack;
    }

    var xAxisId = sanitizeBoundedString(rawDataset.xAxisID, 32);
    if (xAxisId) {
      result.xAxisID = xAxisId;
    }

    var yAxisId = sanitizeBoundedString(rawDataset.yAxisID, 32);
    if (yAxisId) {
      result.yAxisID = yAxisId;
    }

    return result;
  }

  function containsForbiddenOptionKeys(value, depth) {
    if (depth > 10 || value == null) {
      return false;
    }

    var forbidden = {
      onClick: true,
      onHover: true,
      parser: true,
      beforeInit: true,
      afterInit: true,
      beforeUpdate: true,
      afterUpdate: true,
      beforeLayout: true,
      afterLayout: true,
      beforeRender: true,
      afterRender: true,
      beforeDraw: true,
      afterDraw: true,
      beforeEvent: true,
      afterEvent: true,
      beforeDatasetDraw: true,
      afterDatasetDraw: true,
      beforeDatasetsDraw: true,
      afterDatasetsDraw: true
    };

    if (Array.isArray(value)) {
      for (var i = 0; i < value.length; i++) {
        if (containsForbiddenOptionKeys(value[i], depth + 1)) {
          return true;
        }
      }
      return false;
    }

    if (!isPlainObject(value)) {
      return false;
    }

    var keys = Object.keys(value);
    for (var j = 0; j < keys.length; j++) {
      var key = keys[j];
      if (forbidden[key]) {
        return true;
      }
      if (containsForbiddenOptionKeys(value[key], depth + 1)) {
        return true;
      }
    }

    return false;
  }

  function sanitizeOptionValue(value, depth) {
    if (depth > 8 || value == null) {
      return null;
    }

    if (typeof value === "string") {
      var text = value.trim();
      return text.length <= 120 ? text : text.slice(0, 120);
    }

    if (typeof value === "boolean" || isFiniteNumber(value)) {
      return value;
    }

    if (Array.isArray(value)) {
      var maxLength = Math.min(value.length, 100);
      var list = [];
      for (var i = 0; i < maxLength; i++) {
        var sanitizedItem = sanitizeOptionValue(value[i], depth + 1);
        if (sanitizedItem !== null) {
          list.push(sanitizedItem);
        }
      }
      return list;
    }

    if (!isPlainObject(value)) {
      return null;
    }

    var keys = Object.keys(value);
    var result = {};
    var maxKeys = Math.min(keys.length, 40);
    for (var j = 0; j < maxKeys; j++) {
      var key = keys[j];
      if (!key || key.length > 64) {
        continue;
      }

      var sanitized = sanitizeOptionValue(value[key], depth + 1);
      if (sanitized !== null) {
        result[key] = sanitized;
      }
    }

    return result;
  }

  function sanitizeChartOptions(rawOptions) {
    if (rawOptions == null) {
      return null;
    }

    if (!isPlainObject(rawOptions)) {
      return null;
    }

    if (containsForbiddenOptionKeys(rawOptions, 0)) {
      return null;
    }

    var allowedRoots = {
      responsive: true,
      maintainAspectRatio: true,
      indexAxis: true,
      interaction: true,
      plugins: true,
      scales: true,
      layout: true,
      animation: true,
      normalized: true,
      parsing: true
    };

    var result = {};
    var keys = Object.keys(rawOptions);
    for (var i = 0; i < keys.length; i++) {
      var key = keys[i];
      if (!allowedRoots[key]) {
        continue;
      }

      var sanitized = sanitizeOptionValue(rawOptions[key], 0);
      if (sanitized !== null) {
        result[key] = sanitized;
      }
    }

    return result;
  }

  function validateIxChartConfig(rawConfig) {
    if (!isPlainObject(rawConfig)) {
      return { ok: false, reason: "config must be a JSON object" };
    }

    var allowedTypes = {
      line: true,
      bar: true,
      pie: true,
      doughnut: true,
      radar: true,
      polarArea: true,
      scatter: true,
      bubble: true
    };

    var chartType = sanitizeBoundedString(rawConfig.type, 24);
    if (!chartType || !allowedTypes[chartType]) {
      return { ok: false, reason: "unsupported chart type" };
    }

    var data = rawConfig.data;
    if (!isPlainObject(data) || !Array.isArray(data.datasets)) {
      return { ok: false, reason: "data.datasets is required" };
    }

    if (data.datasets.length === 0 || data.datasets.length > ixVisualChartState.maxDatasets) {
      return { ok: false, reason: "dataset count exceeds limit" };
    }

    var labels = null;
    if (Array.isArray(data.labels)) {
      labels = [];
      var labelsCount = Math.min(data.labels.length, ixVisualChartState.maxLabels);
      for (var i = 0; i < labelsCount; i++) {
        var label = sanitizeBoundedString(data.labels[i], 80);
        labels.push(label);
      }
      if (labels.length === 0 && chartType !== "scatter" && chartType !== "bubble") {
        return { ok: false, reason: "labels cannot be empty" };
      }
    } else if (chartType !== "scatter" && chartType !== "bubble") {
      return { ok: false, reason: "labels are required for this chart type" };
    }

    var datasets = [];
    var largestDataset = 0;
    var totalPoints = 0;
    for (var j = 0; j < data.datasets.length; j++) {
      var sanitizedDataset = sanitizeChartDataset(data.datasets[j], chartType);
      if (!sanitizedDataset) {
        return { ok: false, reason: "dataset shape is invalid" };
      }

      var pointsCount = sanitizedDataset.data.length;
      if (pointsCount > ixVisualChartState.maxPointsPerDataset) {
        return { ok: false, reason: "dataset points exceed limit" };
      }

      largestDataset = Math.max(largestDataset, pointsCount);
      totalPoints += pointsCount;
      if (totalPoints > ixVisualChartState.maxTotalPoints) {
        return { ok: false, reason: "total points exceed limit" };
      }

      datasets.push(sanitizedDataset);
    }

    if (labels && labels.length < largestDataset && chartType !== "scatter" && chartType !== "bubble") {
      return { ok: false, reason: "labels count must match data points" };
    }

    var options = sanitizeChartOptions(rawConfig.options);
    if (rawConfig.options != null && options == null) {
      return { ok: false, reason: "options are invalid or not allowed" };
    }

    var normalized = {
      type: chartType,
      data: {
        datasets: datasets
      },
      options: options || {}
    };

    if (labels) {
      normalized.data.labels = labels;
    }

    if (typeof normalized.options.responsive !== "boolean") {
      normalized.options.responsive = true;
    }
    if (typeof normalized.options.maintainAspectRatio !== "boolean") {
      normalized.options.maintainAspectRatio = false;
    }

    return { ok: true, config: normalized };
  }

  function disposeChartBlock(pre) {
    if (!pre) {
      return;
    }

    removeVisualActionBar(pre);

    if (pre._ixChartInstance && typeof pre._ixChartInstance.destroy === "function") {
      try {
        pre._ixChartInstance.destroy();
      } catch (_) {
        // Ignore chart disposal errors.
      }
    }
    pre._ixChartInstance = null;
    pre.removeAttribute("data-visual-chart-rendered");
    pre.removeAttribute("data-visual-chart-pending");
    pre.removeAttribute("data-omd-visual-rendered");
    pre.removeAttribute("data-chart-rendered");
    pre.removeAttribute("data-chart-pending");
    pre.style.removeProperty("display");

    var host = pre.nextElementSibling;
    if (host && host.classList && host.classList.contains("visual-chart-host")) {
      host.remove();
    }
    clearVisualMessageWideWhenEmpty(pre);
  }

  function scheduleChartResize(instance) {
    if (!instance || typeof instance.resize !== "function") {
      return;
    }

    var resizeNow = function() {
      try {
        instance.resize();
      } catch (_) {
        // Ignore layout-time resize failures.
      }
    };

    if (typeof requestAnimationFrame === "function") {
      requestAnimationFrame(resizeNow);
      requestAnimationFrame(function() {
        setTimeout(resizeNow, 0);
      });
      return;
    }

    setTimeout(resizeNow, 0);
    setTimeout(resizeNow, 120);
  }

  function markChartInvalid(pre, reason) {
    if (!pre) {
      return;
    }

    disposeChartBlock(pre);
    pre.setAttribute("data-visual-chart-invalid", "1");
    var suffix = reason ? " (" + reason + ")" : "";
    ensureVisualNotice(pre, "invalid visual block: ix-chart" + suffix);
  }

  async function renderIxChartBlock(pre) {
    if (!pre || pre.getAttribute("data-visual-chart-pending") === "1") {
      return;
    }

    var code = pre.querySelector("code.language-chart");
    var source = pre.getAttribute("data-visual-chart-source");
    if ((!source || source.length === 0) && code) {
      source = normalizeText(code.textContent || "");
    }
    if (!source) {
      source = normalizeText(pre.textContent || "");
    }
    if (source) {
      pre.setAttribute("data-visual-chart-source", source);
    }

    if (!source) {
      markChartInvalid(pre, "empty source");
      return;
    }
    if (source.length > ixVisualChartState.maxSourceChars) {
      markChartInvalid(pre, "source too large");
      return;
    }

    var parsedConfig;
    try {
      parsedConfig = JSON.parse(source);
    } catch (_) {
      markChartInvalid(pre, "invalid JSON");
      return;
    }

    var validation = validateIxChartConfig(parsedConfig);
    if (!validation.ok) {
      markChartInvalid(pre, validation.reason || "schema validation failed");
      return;
    }

    pre.setAttribute("data-visual-chart-pending", "1");
    try {
      disposeChartBlock(pre);
      markVisualMessageWide(pre);

      var host = document.createElement("div");
      host.className = "visual-chart-host";
      var canvas = document.createElement("canvas");
      canvas.className = "visual-chart-canvas";
      host.appendChild(canvas);
      pre.insertAdjacentElement("afterend", host);

      var context = canvas.getContext("2d");
      if (!context) {
        throw new Error("canvas context unavailable");
      }

      var chartConfig = cloneVisualValue(validation.config) || validation.config;
      chartConfig = applyChartThemeDefaults(chartConfig, "preserve_ui_theme");
      var instance = new window.Chart(context, chartConfig);
      pre._ixChartInstance = instance;
      pre.style.display = "none";
      pre.removeAttribute("data-visual-chart-invalid");
      pre.setAttribute("data-visual-chart-rendered", "1");
      clearVisualNotice(pre);
      ensureVisualActionBar(pre, "ix-chart");
      scheduleChartResize(instance);
    } catch (_) {
      markChartInvalid(pre, "render failed");
    } finally {
      pre.removeAttribute("data-visual-chart-pending");
    }
  }

  async function renderOfficeImoChartBlock(canvas) {
    if (!canvas || canvas.getAttribute("data-chart-pending") === "1" || canvas.getAttribute("data-chart-rendered") === "1") {
      return;
    }

    var source = getOfficeImoVisualSourceByKind(canvas, "ix-chart");
    if (source) {
      canvas.setAttribute("data-visual-chart-source", source);
    }

    if (!source) {
      markChartInvalid(canvas, "empty source");
      return;
    }
    if (source.length > ixVisualChartState.maxSourceChars) {
      markChartInvalid(canvas, "source too large");
      return;
    }

    var parsedConfig;
    try {
      parsedConfig = JSON.parse(source);
    } catch (_) {
      markChartInvalid(canvas, "invalid JSON");
      return;
    }

    var validation = validateIxChartConfig(parsedConfig);
    if (!validation.ok) {
      markChartInvalid(canvas, validation.reason || "schema validation failed");
      return;
    }

    canvas.setAttribute("data-chart-pending", "1");
    try {
      disposeChartBlock(canvas);
      markVisualMessageWide(canvas);
      var context = canvas.getContext && canvas.getContext("2d");
      if (!context) {
        throw new Error("canvas context unavailable");
      }

      var chartConfig = cloneVisualValue(validation.config) || validation.config;
      chartConfig = applyChartThemeDefaults(chartConfig, "preserve_ui_theme");
      var instance = new window.Chart(context, chartConfig);
      canvas._ixChartInstance = instance;
      canvas.removeAttribute("data-visual-chart-invalid");
      canvas.setAttribute("data-omd-visual-rendered", "true");
      canvas.setAttribute("data-chart-rendered", "1");
      clearVisualNotice(canvas);
      ensureVisualActionBar(canvas, getOfficeImoVisualKind(canvas, "ix-chart"));
      scheduleChartResize(instance);
    } catch (_) {
      markChartInvalid(canvas, "render failed");
    } finally {
      canvas.removeAttribute("data-chart-pending");
    }
  }

  function ensureNetworkReady() {
    if (window.vis && typeof window.vis.Network === "function") {
      return Promise.resolve(true);
    }

    if (ixVisualNetworkState.loadFailed) {
      return Promise.resolve(false);
    }

    if (ixVisualNetworkState.loadPromise) {
      return ixVisualNetworkState.loadPromise;
    }

    ixVisualNetworkState.loadPromise = Promise.all([
      loadVisualStylesheetOnce(ixVisualAssets.visNetworkCssUrl, "data-ix-visnetwork-css", "network", "stylesheet"),
      loadVisualScriptOnce(
        ixVisualAssets.visNetworkJsUrl,
        "data-ix-visnetwork-js",
        function() {
          return !!(window.vis && typeof window.vis.Network === "function");
        },
        "network",
        "script")
    ])
      .then(function(parts) {
        var cssOk = !!parts[0];
        var jsOk = !!parts[1];
        var ready = jsOk && window.vis && typeof window.vis.Network === "function";
        if (!ready) {
          ixVisualNetworkState.loadFailed = true;
          recordVisualRuntimeReady("network", false);
          return false;
        }
        if (!cssOk) {
          recordVisualRuntimeAssetState("network", "stylesheet", "loaded_optional_failed", ixVisualAssets.visNetworkCssUrl, "stylesheet_optional");
        }
        recordVisualRuntimeReady("network", true);
        return true;
      })
      .catch(function() {
        ixVisualNetworkState.loadFailed = true;
        recordVisualRuntimeReady("network", false);
        return false;
      })
      .finally(function() {
        if (ixVisualNetworkState.loadFailed) {
          ixVisualNetworkState.loadPromise = null;
        }
      });

    return ixVisualNetworkState.loadPromise;
  }

  function collectOfficeImoNetworkBlocks(root) {
    return collectRegisteredOfficeImoVisualBlocks(root, "ix-network");
  }

  function sanitizeNetworkId(value) {
    if (isFiniteNumber(value)) {
      return value;
    }
    if (typeof value !== "string") {
      return null;
    }
    var text = value.trim();
    if (!text || text.length > 80) {
      return null;
    }
    return text;
  }

  function sanitizeNetworkLabel(value, maxLength) {
    if (typeof value !== "string") {
      return "";
    }
    var text = value.trim();
    if (!text) {
      return "";
    }
    return text.length <= maxLength ? text : text.slice(0, maxLength);
  }

  function sanitizeNetworkColor(value, allowInherit) {
    if (typeof value === "string") {
      var direct = value.trim();
      return direct.length <= 64 ? direct : null;
    }

    if (!isPlainObject(value)) {
      return null;
    }

    var result = {};
    if (typeof value.color === "string" && value.color.trim().length <= 64) {
      result.color = value.color.trim();
    }
    if (typeof value.background === "string" && value.background.trim().length <= 64) {
      result.background = value.background.trim();
    }
    if (typeof value.border === "string" && value.border.trim().length <= 64) {
      result.border = value.border.trim();
    }
    if (typeof value.highlight === "string" && value.highlight.trim().length <= 64) {
      result.highlight = value.highlight.trim();
    }
    if (typeof value.hover === "string" && value.hover.trim().length <= 64) {
      result.hover = value.hover.trim();
    }
    if (allowInherit) {
      if (typeof value.inherit === "boolean") {
        result.inherit = value.inherit;
      } else if (typeof value.inherit === "string") {
        var inheritValue = value.inherit.trim();
        if (inheritValue.length <= 24) {
          result.inherit = inheritValue;
        }
      }
    }
    if (isFiniteNumber(value.opacity) && value.opacity >= 0 && value.opacity <= 1) {
      result.opacity = value.opacity;
    }

    return Object.keys(result).length > 0 ? result : null;
  }

  function sanitizeNetworkNode(rawNode) {
    if (!isPlainObject(rawNode)) {
      return null;
    }

    var id = sanitizeNetworkId(rawNode.id);
    if (id === null) {
      return null;
    }

    var node = { id: id };
    var label = sanitizeNetworkLabel(rawNode.label, ixVisualNetworkState.maxNodeLabelChars);
    if (label) {
      node.label = label;
    }

    var groupId = sanitizeNetworkId(rawNode.group);
    if (groupId !== null) {
      node.group = groupId;
    }

    if (isFiniteNumber(rawNode.value) && rawNode.value >= 0 && rawNode.value <= 1000000) {
      node.value = rawNode.value;
    }
    if (isFiniteNumber(rawNode.size) && rawNode.size >= 1 && rawNode.size <= 120) {
      node.size = rawNode.size;
    }
    if (isFiniteNumber(rawNode.x) && Math.abs(rawNode.x) <= 200000) {
      node.x = rawNode.x;
    }
    if (isFiniteNumber(rawNode.y) && Math.abs(rawNode.y) <= 200000) {
      node.y = rawNode.y;
    }
    if (typeof rawNode.fixed === "boolean") {
      node.fixed = rawNode.fixed;
    }
    if (typeof rawNode.title === "string" && rawNode.title.trim().length <= 240) {
      node.title = rawNode.title.trim();
    }

    var shapes = {
      dot: true,
      ellipse: true,
      box: true,
      circle: true,
      diamond: true,
      star: true,
      triangle: true,
      triangleDown: true,
      square: true,
      text: true
    };
    var shape = sanitizeBoundedString(rawNode.shape, 24);
    if (shape && shapes[shape]) {
      node.shape = shape;
    }

    var color = sanitizeNetworkColor(rawNode.color, false);
    if (color !== null) {
      node.color = color;
    }

    return node;
  }

  function sanitizeNetworkArrows(value) {
    if (typeof value === "boolean") {
      return value;
    }
    if (typeof value === "string") {
      var text = value.trim();
      if (!text || text.length > 24) {
        return null;
      }
      return text;
    }
    if (!isPlainObject(value)) {
      return null;
    }

    var result = {};
    var entries = ["to", "from", "middle"];
    for (var i = 0; i < entries.length; i++) {
      var key = entries[i];
      var part = value[key];
      if (typeof part === "boolean") {
        result[key] = part;
      } else if (isPlainObject(part)) {
        var piece = {};
        if (typeof part.enabled === "boolean") {
          piece.enabled = part.enabled;
        }
        if (typeof part.type === "string" && part.type.trim().length <= 32) {
          piece.type = part.type.trim();
        }
        if (isFiniteNumber(part.scaleFactor) && part.scaleFactor >= 0.1 && part.scaleFactor <= 8) {
          piece.scaleFactor = part.scaleFactor;
        }
        if (Object.keys(piece).length > 0) {
          result[key] = piece;
        }
      }
    }

    return Object.keys(result).length > 0 ? result : null;
  }

  function sanitizeNetworkEdge(rawEdge) {
    if (!isPlainObject(rawEdge)) {
      return null;
    }

    var from = sanitizeNetworkId(rawEdge.from);
    var to = sanitizeNetworkId(rawEdge.to);
    if (from === null && Object.prototype.hasOwnProperty.call(rawEdge, "source")) {
      from = sanitizeNetworkId(rawEdge.source);
    }
    if (to === null && Object.prototype.hasOwnProperty.call(rawEdge, "target")) {
      to = sanitizeNetworkId(rawEdge.target);
    }
    if (from === null || to === null) {
      return null;
    }

    var edge = { from: from, to: to };
    var label = sanitizeNetworkLabel(rawEdge.label, ixVisualNetworkState.maxEdgeLabelChars);
    if (label) {
      edge.label = label;
    }

    if (typeof rawEdge.title === "string" && rawEdge.title.trim().length <= 240) {
      edge.title = rawEdge.title.trim();
    }
    if (isFiniteNumber(rawEdge.value) && rawEdge.value >= 0 && rawEdge.value <= 1000000) {
      edge.value = rawEdge.value;
    }
    if (isFiniteNumber(rawEdge.width) && rawEdge.width >= 0 && rawEdge.width <= 30) {
      edge.width = rawEdge.width;
    }
    if (typeof rawEdge.hidden === "boolean") {
      edge.hidden = rawEdge.hidden;
    }
    if (typeof rawEdge.physics === "boolean") {
      edge.physics = rawEdge.physics;
    }
    if (typeof rawEdge.smooth === "boolean") {
      edge.smooth = rawEdge.smooth;
    }
    if (typeof rawEdge.dashes === "boolean") {
      edge.dashes = rawEdge.dashes;
    } else if (Array.isArray(rawEdge.dashes) && rawEdge.dashes.length <= 8) {
      var dashes = [];
      for (var i = 0; i < rawEdge.dashes.length; i++) {
        if (isFiniteNumber(rawEdge.dashes[i]) && rawEdge.dashes[i] >= 0 && rawEdge.dashes[i] <= 60) {
          dashes.push(rawEdge.dashes[i]);
        }
      }
      if (dashes.length > 0) {
        edge.dashes = dashes;
      }
    }

    var color = sanitizeNetworkColor(rawEdge.color, true);
    if (color !== null) {
      edge.color = color;
    }

    var arrows = sanitizeNetworkArrows(rawEdge.arrows);
    if (arrows !== null) {
      edge.arrows = arrows;
    }

    return edge;
  }

  function containsForbiddenNetworkOptionKeys(value, depth) {
    if (depth > 10 || value == null) {
      return false;
    }

    var forbidden = {
      manipulation: true,
      configure: true,
      chosen: true,
      customScalingFunction: true,
      clickToUse: true,
      onAdd: true,
      onUpdate: true,
      onRemove: true
    };

    if (Array.isArray(value)) {
      for (var i = 0; i < value.length; i++) {
        if (containsForbiddenNetworkOptionKeys(value[i], depth + 1)) {
          return true;
        }
      }
      return false;
    }

    if (!isPlainObject(value)) {
      return false;
    }

    var keys = Object.keys(value);
    for (var j = 0; j < keys.length; j++) {
      var key = keys[j];
      if (forbidden[key]) {
        return true;
      }
      if (containsForbiddenNetworkOptionKeys(value[key], depth + 1)) {
        return true;
      }
    }

    return false;
  }

  function sanitizeIxNetworkOptions(rawOptions) {
    if (rawOptions == null) {
      return null;
    }
    if (!isPlainObject(rawOptions)) {
      return null;
    }
    if (containsForbiddenNetworkOptionKeys(rawOptions, 0)) {
      return null;
    }

    var allowedRoots = {
      autoResize: true,
      height: true,
      width: true,
      interaction: true,
      layout: true,
      physics: true,
      edges: true,
      nodes: true,
      groups: true
    };

    var result = {};
    var keys = Object.keys(rawOptions);
    for (var i = 0; i < keys.length; i++) {
      var key = keys[i];
      if (!allowedRoots[key]) {
        continue;
      }
      var sanitized = sanitizeOptionValue(rawOptions[key], 0);
      if (sanitized !== null) {
        result[key] = sanitized;
      }
    }

    return result;
  }

  function validateIxNetworkConfig(rawConfig) {
    if (!isPlainObject(rawConfig)) {
      return { ok: false, reason: "config must be a JSON object" };
    }
    if (!Array.isArray(rawConfig.nodes)) {
      return { ok: false, reason: "nodes must be an array" };
    }
    if (rawConfig.nodes.length === 0 || rawConfig.nodes.length > ixVisualNetworkState.maxNodes) {
      return { ok: false, reason: "node count exceeds limit" };
    }

    var rawEdges = Array.isArray(rawConfig.edges) ? rawConfig.edges : [];
    if (rawEdges.length > ixVisualNetworkState.maxEdges) {
      return { ok: false, reason: "edge count exceeds limit" };
    }

    var seenIds = {};
    var nodes = [];
    for (var i = 0; i < rawConfig.nodes.length; i++) {
      var node = sanitizeNetworkNode(rawConfig.nodes[i]);
      if (!node) {
        return { ok: false, reason: "node shape is invalid" };
      }

      var key = String(node.id);
      if (Object.prototype.hasOwnProperty.call(seenIds, key)) {
        return { ok: false, reason: "node ids must be unique" };
      }
      seenIds[key] = true;
      nodes.push(node);
    }

    var edges = [];
    for (var j = 0; j < rawEdges.length; j++) {
      var edge = sanitizeNetworkEdge(rawEdges[j]);
      if (!edge) {
        return { ok: false, reason: "edge shape is invalid" };
      }

      if (!Object.prototype.hasOwnProperty.call(seenIds, String(edge.from))
        || !Object.prototype.hasOwnProperty.call(seenIds, String(edge.to))) {
        return { ok: false, reason: "edge endpoints must reference existing nodes" };
      }
      edges.push(edge);
    }

    var options = sanitizeIxNetworkOptions(rawConfig.options);
    if (rawConfig.options != null && options == null) {
      return { ok: false, reason: "options are invalid or not allowed" };
    }

    return {
      ok: true,
      config: {
        nodes: nodes,
        edges: edges,
        options: options || {}
      }
    };
  }

  function disposeNetworkBlock(pre) {
    if (!pre) {
      return;
    }

    removeVisualActionBar(pre);

    if (typeof pre._ixNetworkResizeCleanup === "function") {
      try {
        pre._ixNetworkResizeCleanup();
      } catch (_) {
        // Ignore observer cleanup errors.
      }
    }
    pre._ixNetworkResizeCleanup = null;

    if (pre._ixNetworkInstance && typeof pre._ixNetworkInstance.destroy === "function") {
      try {
        pre._ixNetworkInstance.destroy();
      } catch (_) {
        // Ignore network disposal errors.
      }
    }
    pre._ixNetworkInstance = null;
    pre.removeAttribute("data-visual-network-rendered");
    pre.removeAttribute("data-visual-network-pending");
    pre.removeAttribute("data-omd-visual-rendered");
    pre.removeAttribute("data-network-rendered");
    pre.removeAttribute("data-network-pending");
    pre.style.removeProperty("display");

    var host = pre.nextElementSibling;
    if (host && host.classList && host.classList.contains("visual-network-host")) {
      host.remove();
    }
    if (pre.classList && pre.classList.contains("omd-network")) {
      var nativeCanvas = pre.querySelector(".omd-network-canvas");
      if (nativeCanvas) {
        nativeCanvas.remove();
      }
    }
    clearVisualMessageWideWhenEmpty(pre);
  }

  function markNetworkInvalid(pre, reason) {
    if (!pre) {
      return;
    }

    disposeNetworkBlock(pre);
    pre.setAttribute("data-visual-network-invalid", "1");
    var suffix = reason ? " (" + reason + ")" : "";
    ensureVisualNotice(pre, "invalid visual block: ix-network" + suffix);
  }

  async function renderIxNetworkBlock(pre) {
    if (!pre || pre.getAttribute("data-visual-network-pending") === "1") {
      return;
    }

    var code = pre.querySelector("code.language-network");
    var source = pre.getAttribute("data-visual-network-source");
    if ((!source || source.length === 0) && code) {
      source = normalizeText(code.textContent || "");
    }
    if (!source) {
      source = normalizeText(pre.textContent || "");
    }
    if (source) {
      pre.setAttribute("data-visual-network-source", source);
    }

    if (!source) {
      markNetworkInvalid(pre, "empty source");
      return;
    }
    if (source.length > ixVisualNetworkState.maxSourceChars) {
      markNetworkInvalid(pre, "source too large");
      return;
    }

    var parsedConfig;
    try {
      parsedConfig = JSON.parse(source);
    } catch (_) {
      markNetworkInvalid(pre, "invalid JSON");
      return;
    }

    var validation = validateIxNetworkConfig(parsedConfig);
    if (!validation.ok) {
      markNetworkInvalid(pre, validation.reason || "schema validation failed");
      return;
    }

    pre.setAttribute("data-visual-network-pending", "1");
    try {
      disposeNetworkBlock(pre);
      markVisualMessageWide(pre);

      var host = document.createElement("div");
      host.className = "visual-network-host";
      var canvas = document.createElement("div");
      canvas.className = "visual-network-canvas";
      host.appendChild(canvas);
      pre.insertAdjacentElement("afterend", host);

      var networkOptions = buildRuntimeNetworkOptions(
        "preserve_ui_theme",
        validation.config.options || {},
        "inline");

      var network = new window.vis.Network(
        canvas,
        {
          nodes: validation.config.nodes,
          edges: validation.config.edges
        },
        networkOptions);

      stabilizeNetwork(network, 280);
      safeNetworkFit(network, 120);
      try {
        network.once("stabilized", function() {
          safeNetworkFit(network, 0);
        });
      } catch (_) {
        // Ignore listener setup failures.
      }

      pre._ixNetworkInstance = network;
      pre._ixNetworkResizeCleanup = attachNetworkAutoFitObserver(network, canvas);
      pre.style.display = "none";
      pre.removeAttribute("data-visual-network-invalid");
      pre.setAttribute("data-visual-network-rendered", "1");
      clearVisualNotice(pre);
      ensureVisualActionBar(pre, "ix-network");
    } catch (_) {
      markNetworkInvalid(pre, "render failed");
    } finally {
      pre.removeAttribute("data-visual-network-pending");
    }
  }

  async function renderOfficeImoNetworkBlock(host) {
    if (!host || host.getAttribute("data-network-pending") === "1" || host.getAttribute("data-network-rendered") === "1") {
      return;
    }

    var source = getOfficeImoVisualSourceByKind(host, "ix-network");
    if (source) {
      host.setAttribute("data-visual-network-source", source);
    }

    if (!source) {
      markNetworkInvalid(host, "empty source");
      return;
    }
    if (source.length > ixVisualNetworkState.maxSourceChars) {
      markNetworkInvalid(host, "source too large");
      return;
    }

    var parsedConfig;
    try {
      parsedConfig = JSON.parse(source);
    } catch (_) {
      markNetworkInvalid(host, "invalid JSON");
      return;
    }

    var validation = validateIxNetworkConfig(parsedConfig);
    if (!validation.ok) {
      markNetworkInvalid(host, validation.reason || "schema validation failed");
      return;
    }

    host.setAttribute("data-network-pending", "1");
    try {
      disposeNetworkBlock(host);
      markVisualMessageWide(host);

      var canvas = document.createElement("div");
      canvas.className = "omd-network-canvas";
      host.appendChild(canvas);

      var networkOptions = buildRuntimeNetworkOptions(
        "preserve_ui_theme",
        validation.config.options || {},
        "inline");

      var network = new window.vis.Network(
        canvas,
        {
          nodes: validation.config.nodes,
          edges: validation.config.edges
        },
        networkOptions);

      stabilizeNetwork(network, 280);
      safeNetworkFit(network, 120);
      try {
        network.once("stabilized", function() {
          safeNetworkFit(network, 0);
        });
      } catch (_) {
        // Ignore listener setup failures.
      }

      host._ixNetworkInstance = network;
      host._ixNetworkResizeCleanup = attachNetworkAutoFitObserver(network, canvas);
      host.removeAttribute("data-visual-network-invalid");
      host.setAttribute("data-omd-visual-rendered", "true");
      host.setAttribute("data-network-rendered", "1");
      clearVisualNotice(host);
      ensureVisualActionBar(host, getOfficeImoVisualKind(host, "ix-network"));
    } catch (_) {
      markNetworkInvalid(host, "render failed");
    } finally {
      host.removeAttribute("data-network-pending");
    }
  }

  function collectIxNetworkBlocks(root) {
    var codeNodes = root.querySelectorAll(".bubble .markdown-body pre > code.language-network");
    if (!codeNodes || codeNodes.length === 0) {
      return [];
    }

    var seen = {};
    var blocks = [];
    for (var i = 0; i < codeNodes.length; i++) {
      var pre = codeNodes[i].parentElement;
      if (!pre || !pre.parentElement || pre.tagName.toLowerCase() !== "pre") {
        continue;
      }

      var marker = pre.dataset.visualNetworkBlockId;
      if (!marker) {
        marker = "ix-network-" + String(i + 1) + "-" + String(Math.floor(Math.random() * 1000000));
        pre.dataset.visualNetworkBlockId = marker;
      }

      if (seen[marker]) {
        continue;
      }
      seen[marker] = true;
      blocks.push(pre);
    }

    return blocks;
  }

  async function renderTranscriptVisualKind(root, kind, collectFenceBlocks, renderFenceBlock) {
    var entry = getNativeVisualRegistryEntry(kind);
    if (!entry) {
      return;
    }

    var fenceBlocks = collectFenceBlocks(root);
    var nativeBlocks = collectRegisteredOfficeImoVisualBlocks(root, entry.type);
    var entries = buildOrderedVisualEntries(fenceBlocks, nativeBlocks);
    if (entries.length === 0) {
      return;
    }

    var state = entry.getState();
    var maxBlocks = state && typeof state.maxBlocksPerMessage === "number"
      ? state.maxBlocksPerMessage
      : 0;
    for (var i = maxBlocks; i < entries.length; i++) {
      entry.markInvalid(entries[i].block, entry.overflowReason);
    }

    var ready = await entry.ensureReady();
    if (!ready) {
      for (var j = 0; j < Math.min(entries.length, maxBlocks); j++) {
        entry.markInvalid(entries[j].block, entry.unavailableReason);
      }
      return;
    }

    for (var m = 0; m < Math.min(entries.length, maxBlocks); m++) {
      if (entries[m].isNative) {
        await entry.renderNative(entries[m].block);
      } else {
        await renderFenceBlock(entries[m].block);
      }
    }
  }

  async function renderTranscriptNetworks(root) {
    await renderTranscriptVisualKind(root, "ix-network", collectIxNetworkBlocks, renderIxNetworkBlock);
  }

  function collectMermaidBlocks(root) {
    var codeNodes = root.querySelectorAll(".bubble .markdown-body pre.mermaid, .bubble .markdown-body pre.language-mermaid, .bubble .markdown-body pre > code.language-mermaid, .bubble .markdown-body pre > code.mermaid");
    if (!codeNodes || codeNodes.length === 0) {
      return [];
    }

    var seen = {};
    var blocks = [];
    for (var i = 0; i < codeNodes.length; i++) {
      var current = codeNodes[i];
      var pre = current.tagName && current.tagName.toLowerCase() === "pre"
        ? current
        : current.parentElement;
      if (!pre || !pre.parentElement || pre.tagName.toLowerCase() !== "pre") {
        continue;
      }

      var marker = pre.dataset.ixMermaidBlockId;
      if (!marker) {
        marker = "ix-mermaid-block-" + String(ixVisualMermaidBlockIdCounter++);
        pre.dataset.ixMermaidBlockId = marker;
      }

      if (seen[marker]) {
        continue;
      }

      seen[marker] = true;
      blocks.push(pre);
    }

    return blocks;
  }

  async function renderTranscriptMermaid(root) {
    var blocks = collectMermaidBlocks(root);
    if (blocks.length === 0) {
      return;
    }

    var maxBlocks = ixVisualMermaidState.maxBlocksPerMessage;
    var count = blocks.length;
    for (var i = maxBlocks; i < count; i++) {
      markMermaidInvalid(blocks[i], "too many diagrams");
    }

    var ready = await ensureMermaidReady();
    if (!ready) {
      for (var j = 0; j < Math.min(count, maxBlocks); j++) {
        markMermaidInvalid(blocks[j], "renderer unavailable");
      }
      return;
    }

    for (var k = 0; k < Math.min(count, maxBlocks); k++) {
      await renderMermaidBlock(blocks[k]);
    }
  }

  function collectIxChartBlocks(root) {
    var codeNodes = root.querySelectorAll(".bubble .markdown-body pre > code.language-chart");
    if (!codeNodes || codeNodes.length === 0) {
      return [];
    }

    var seen = {};
    var blocks = [];
    for (var i = 0; i < codeNodes.length; i++) {
      var pre = codeNodes[i].parentElement;
      if (!pre || !pre.parentElement || pre.tagName.toLowerCase() !== "pre") {
        continue;
      }

      var marker = pre.dataset.visualChartBlockId;
      if (!marker) {
        marker = "ix-chart-" + String(i + 1) + "-" + String(Math.floor(Math.random() * 1000000));
        pre.dataset.visualChartBlockId = marker;
      }

      if (seen[marker]) {
        continue;
      }
      seen[marker] = true;
      blocks.push(pre);
    }

    return blocks;
  }

  function collectOfficeImoChartBlocks(root) {
    return collectRegisteredOfficeImoVisualBlocks(root, "ix-chart");
  }

  async function renderTranscriptCharts(root) {
    await renderTranscriptVisualKind(root, "ix-chart", collectIxChartBlocks, renderIxChartBlock);
  }

  function disposeTranscriptVisuals(root) {
    if (!root || !root.querySelectorAll) {
      return;
    }

    var mermaidBlocks = root.querySelectorAll(".bubble .markdown-body pre[data-ix-mermaid-rendered='1']");
    for (var m = 0; m < mermaidBlocks.length; m++) {
      removeVisualActionBar(mermaidBlocks[m]);
      clearVisualMessageWideWhenEmpty(mermaidBlocks[m]);
    }

    var registryKeys = Object.keys(ixNativeVisualRegistry);
    for (var i = 0; i < registryKeys.length; i++) {
      var entry = ixNativeVisualRegistry[registryKeys[i]];
      if (!entry || !entry.renderedSelector) {
        continue;
      }

      var renderedBlocks = root.querySelectorAll(
        ".bubble .markdown-body pre[data-" + entry.type + "-rendered='1'], " + entry.renderedSelector);
      for (var j = 0; j < renderedBlocks.length; j++) {
        entry.dispose(renderedBlocks[j]);
      }
    }
  }

  window.ixDisposeTranscriptVisuals = function(root) {
    disposeTranscriptVisuals(root);
  };

  function runTranscriptVisualPhaseSafely(root, renderPhase) {
    return Promise.resolve()
      .then(function() {
        return renderPhase(root);
      })
      .catch(function(error) {
        if (typeof console !== "undefined" && console && typeof console.warn === "function") {
          console.warn("transcript visual phase failed", error);
        }
      });
  }

  window.ixRenderTranscriptVisuals = function(root) {
    if (!root || !root.querySelectorAll) {
      return Promise.resolve();
    }

    return runTranscriptVisualPhaseSafely(root, renderTranscriptCharts)
      .then(function() {
        return runTranscriptVisualPhaseSafely(root, renderTranscriptNetworks);
      })
      .then(function() {
        return runTranscriptVisualPhaseSafely(root, renderTranscriptMermaid);
      });
  };

  var visualViewPanel = byId("visualViewPanel");
  var visualViewBackdrop = byId("visualViewBackdrop");
  var visualViewTitle = byId("visualViewTitle");
  var visualViewMeta = byId("visualViewMeta");
  var visualViewFeedback = byId("visualViewFeedback");
  var visualViewBody = byId("visualViewBody");
  var visualViewCanvasWrap = byId("visualViewCanvasWrap");
  var visualViewExportActions = byId("visualViewExportActions");
  var visualViewExportPath = byId("visualViewExportPath");
  var btnVisualViewClose = byId("btnVisualViewClose");
  var btnVisualViewExportPng = byId("btnVisualViewExportPng");
  var btnVisualViewExportSvg = byId("btnVisualViewExportSvg");
  var btnVisualViewPopout = byId("btnVisualViewPopout");
  var btnVisualViewToggleSize = byId("btnVisualViewToggleSize");
  var btnVisualViewOpenExport = byId("btnVisualViewOpenExport");
  var btnVisualViewRevealExport = byId("btnVisualViewRevealExport");
  var btnVisualViewCopyExportPath = byId("btnVisualViewCopyExportPath");

  var visualViewState = {
    sessionId: 0,
    type: "",
    source: "",
    title: "",
    chartInstance: null,
    networkInstance: null,
    networkResizeCleanup: null,
    lastExportPath: "",
    lastExportFormat: "",
    isMaximized: false,
    popoutInFlight: false,
    lifecycleGuardInitialized: false
  };
  var visualViewFeedbackClearTimer = 0;
  var visualViewRelayoutTimer = 0;
  var pendingVisualExports = Object.create(null);

  function clearVisualViewFeedbackTimer() {
    if (visualViewFeedbackClearTimer) {
      window.clearTimeout(visualViewFeedbackClearTimer);
      visualViewFeedbackClearTimer = 0;
    }
  }

  function setVisualViewFeedback(text, tone, autoClearMs) {
    if (!visualViewFeedback) {
      return;
    }

    clearVisualViewFeedbackTimer();
    visualViewFeedback.classList.remove("show", "ok", "warn", "bad", "info");

    var content = String(text || "").trim();
    if (!content) {
      visualViewFeedback.textContent = "";
      return;
    }

    visualViewFeedback.textContent = content;
    visualViewFeedback.classList.add("show");

    var normalizedTone = String(tone || "").toLowerCase();
    if (normalizedTone === "ok" || normalizedTone === "warn" || normalizedTone === "bad" || normalizedTone === "info") {
      visualViewFeedback.classList.add(normalizedTone);
    } else {
      visualViewFeedback.classList.add("info");
    }

    var timeout = Number(autoClearMs);
    if (Number.isFinite(timeout) && timeout > 0) {
      visualViewFeedbackClearTimer = window.setTimeout(function() {
        setVisualViewFeedback("", "info", 0);
      }, timeout);
    }
  }

  function normalizeVisualType(type) {
    var normalized = String(type || "").trim().toLowerCase();
    if (normalized === "chart") {
      return "ix-chart";
    }
    if (normalized === "network") {
      return "ix-network";
    }
    return normalized;
  }

  function normalizeVisualExportFormat(value, visualType) {
    var normalized = String(value || "").trim().toLowerCase();
    if (normalized === "svg") {
      return normalizeVisualType(visualType) === "mermaid" ? "svg" : "png";
    }
    return "png";
  }

  function normalizeDocxVisualMaxWidthPx(value) {
    return normalizeDocxVisualMaxWidthPxContract(value);
  }

  function normalizeVisualRenderDimension(value, fallback, minValue, maxValue) {
    var parsed = Number.parseInt(String(value == null ? "" : value).trim(), 10);
    if (!Number.isFinite(parsed)) {
      return fallback;
    }

    if (parsed < minValue) {
      return minValue;
    }
    if (parsed > maxValue) {
      return maxValue;
    }

    return Math.floor(parsed);
  }

  function resolveDocxRenderSize(visualType, docxVisualMaxWidthPx) {
    var normalizedType = normalizeVisualType(visualType);
    var width = normalizeDocxVisualMaxWidthPx(docxVisualMaxWidthPx);
    var entry = getNativeVisualRegistryEntry(normalizedType);
    if (entry && typeof entry.resolveDocxRenderSize === "function") {
      return entry.resolveDocxRenderSize(width);
    }
    return {
      width: normalizeVisualRenderDimension(width, exportDocxVisualMaxWidthContract.defaultPx, exportDocxVisualMaxWidthContract.minPx, exportDocxVisualMaxWidthContract.maxPx),
      height: null
    };
  }

  function getVisualExportExtension(format) {
    return String(format || "").toLowerCase() === "svg" ? ".svg" : ".png";
  }

  function resolveVisualExportBuildFailureMessage(visualType, format) {
    var kindLabel = getVisualKindLabel(visualType);
    var normalizedFormat = normalizeVisualExportFormat(format, visualType).toUpperCase();
    return kindLabel + " export couldn't prepare a " + normalizedFormat + " image payload.";
  }

  function tryCaptureVisualViewCanvasPayload(visualType) {
    if (!visualViewCanvasWrap || !document.body || !document.body.classList.contains("visual-view-open")) {
      return null;
    }

    var activeType = normalizeVisualType(visualViewState.type);
    var normalizedType = normalizeVisualType(visualType);
    if (!normalizedType || normalizedType !== activeType) {
      return null;
    }

    var canvas = visualViewCanvasWrap.querySelector("canvas");
    if (!canvas || typeof canvas.toDataURL !== "function") {
      return null;
    }

    try {
      var dataUrl = canvas.toDataURL("image/png");
      var parsedData = parseDataUrlPayload(dataUrl);
      if (!parsedData || !parsedData.dataBase64) {
        return null;
      }

      return {
        id: "panel-canvas",
        alt: getVisualKindLabel(normalizedType) + " preview",
        mimeType: parsedData.mimeType || "image/png",
        dataBase64: parsedData.dataBase64
      };
    } catch (_) {
      return null;
    }
  }

  function getVisualExportPreferences() {
    var options = state && state.options ? state.options : {};
    var exportPrefs = options.export || {};
    return {
      saveMode: String(exportPrefs.saveMode || "").toLowerCase() === "remember" ? "remember" : "ask",
      lastDirectory: String(exportPrefs.lastDirectory || "").trim(),
      visualThemeMode: normalizeVisualExportThemeMode(exportPrefs.visualThemeMode),
      docxVisualMaxWidthPx: normalizeDocxVisualMaxWidthPx(exportPrefs.docxVisualMaxWidthPx)
    };
  }

  function buildVisualExportPath(format, title) {
    var prefs = getVisualExportPreferences();
    if (prefs.saveMode !== "remember" || !prefs.lastDirectory) {
      return "";
    }

    var stem = typeof sanitizeExportFileStem === "function"
      ? sanitizeExportFileStem(title || "visual")
      : String(title || "visual").replace(/[<>:\"\/\\|?*\u0000-\u001F]/g, "_");
    var timestamp = typeof buildExportTimestamp === "function"
      ? buildExportTimestamp()
      : Date.now().toString(36);
    var fileName = stem + "-" + timestamp + getVisualExportExtension(format);
    if (typeof joinExportPath === "function") {
      return joinExportPath(prefs.lastDirectory, fileName);
    }

    if (prefs.lastDirectory.endsWith("\\") || prefs.lastDirectory.endsWith("/")) {
      return prefs.lastDirectory + fileName;
    }

    return prefs.lastDirectory + "\\" + fileName;
  }

  function buildVisualExportId(format) {
    return "vexp-" + String(format || "png") + "-" + Date.now().toString(36) + "-" + Math.floor(Math.random() * 100000).toString(36);
  }

  function visualFileNameFromPath(path) {
    var raw = String(path || "").trim();
    if (!raw) {
      return "";
    }

    var slash = raw.lastIndexOf("/");
    var backslash = raw.lastIndexOf("\\");
    var idx = Math.max(slash, backslash);
    return idx >= 0 ? raw.substring(idx + 1) : raw;
  }

  function setVisualViewExportButtonsBusy() {
    var busy = false;
    for (var key in pendingVisualExports) {
      if (Object.prototype.hasOwnProperty.call(pendingVisualExports, key)) {
        busy = true;
        break;
      }
    }

    if (btnVisualViewExportPng) {
      btnVisualViewExportPng.disabled = busy;
      btnVisualViewExportPng.classList.toggle("busy", busy);
    }
    if (btnVisualViewExportSvg) {
      btnVisualViewExportSvg.disabled = busy;
      btnVisualViewExportSvg.classList.toggle("busy", busy);
    }
  }

  function setVisualViewPopoutBusy(busy) {
    var isBusy = busy === true;
    visualViewState.popoutInFlight = isBusy;
    if (btnVisualViewPopout) {
      btnVisualViewPopout.disabled = isBusy;
      btnVisualViewPopout.classList.toggle("busy", isBusy);
    }
  }

  function renderVisualViewSizeToggle() {
    if (!btnVisualViewToggleSize) {
      return;
    }

    var isMaximized = visualViewState.isMaximized === true;
    btnVisualViewToggleSize.textContent = isMaximized ? "Restore" : "Maximize";
    btnVisualViewToggleSize.setAttribute("aria-pressed", isMaximized ? "true" : "false");
    btnVisualViewToggleSize.title = isMaximized ? "Restore visual view size" : "Maximize visual view size";
  }

  function setVisualViewMaximized(enabled) {
    visualViewState.isMaximized = enabled === true;
    document.body.classList.toggle("visual-view-maximized", visualViewState.isMaximized);
    renderVisualViewSizeToggle();
    scheduleVisualViewRelayout();
  }

  function ensureVisualViewClosedState() {
    if (document.body.classList.contains("visual-view-open")) {
      return;
    }

    if (visualViewState.isMaximized || document.body.classList.contains("visual-view-maximized")) {
      visualViewState.isMaximized = false;
      document.body.classList.remove("visual-view-maximized");
      renderVisualViewSizeToggle();
    }

    if (visualViewState.popoutInFlight) {
      setVisualViewPopoutBusy(false);
    }
  }

  function initializeVisualViewLifecycleGuards() {
    if (visualViewState.lifecycleGuardInitialized) {
      return;
    }
    visualViewState.lifecycleGuardInitialized = true;

    if (typeof MutationObserver === "function" && document.body) {
      var visualViewBodyClassObserver = new MutationObserver(function() {
        ensureVisualViewClosedState();
      });
      try {
        visualViewBodyClassObserver.observe(document.body, {
          attributes: true,
          attributeFilter: ["class"]
        });
      } catch (_) {
        // Ignore observer initialization failures.
      }
    }

    ensureVisualViewClosedState();
  }

  function setVisualViewExportPath(path, format) {
    visualViewState.lastExportPath = String(path || "").trim();
    visualViewState.lastExportFormat = String(format || "").trim().toLowerCase();

    var hasPath = visualViewState.lastExportPath.length > 0;
    if (visualViewExportActions) {
      visualViewExportActions.classList.toggle("show", hasPath);
    }
    if (visualViewExportPath) {
      visualViewExportPath.textContent = hasPath ? visualViewState.lastExportPath : "";
      visualViewExportPath.title = hasPath ? visualViewState.lastExportPath : "";
    }
    if (btnVisualViewOpenExport) {
      btnVisualViewOpenExport.disabled = !hasPath;
    }
    if (btnVisualViewRevealExport) {
      btnVisualViewRevealExport.disabled = !hasPath;
    }
    if (btnVisualViewCopyExportPath) {
      btnVisualViewCopyExportPath.disabled = !hasPath;
    }
  }

  function triggerVisualExportAction(action) {
    var path = String(visualViewState.lastExportPath || "").trim();
    if (!path) {
      setVisualViewFeedback("No export path available yet.", "warn", 2500);
      return;
    }

    post("visual_export_action", {
      action: action,
      path: path
    });
  }

  function disposeVisualViewRuntime() {
    if (visualViewRelayoutTimer) {
      window.clearTimeout(visualViewRelayoutTimer);
      visualViewRelayoutTimer = 0;
    }

    if (visualViewState.chartInstance && typeof visualViewState.chartInstance.destroy === "function") {
      try {
        visualViewState.chartInstance.destroy();
      } catch (_) {
        // Ignore chart disposal errors.
      }
    }
    visualViewState.chartInstance = null;

    if (visualViewState.networkInstance && typeof visualViewState.networkInstance.destroy === "function") {
      try {
        visualViewState.networkInstance.destroy();
      } catch (_) {
        // Ignore network disposal errors.
      }
    }
    visualViewState.networkInstance = null;

    if (typeof visualViewState.networkResizeCleanup === "function") {
      try {
        visualViewState.networkResizeCleanup();
      } catch (_) {
        // Ignore cleanup errors.
      }
    }
    visualViewState.networkResizeCleanup = null;

    if (visualViewCanvasWrap) {
      visualViewCanvasWrap.innerHTML = "";
    }
  }

  function scheduleVisualViewRelayout() {
    if (!document.body.classList.contains("visual-view-open")) {
      return;
    }
    if (visualViewRelayoutTimer) {
      return;
    }

    visualViewRelayoutTimer = window.setTimeout(function() {
      visualViewRelayoutTimer = 0;
      if (!document.body.classList.contains("visual-view-open")) {
        return;
      }

      if (visualViewState.chartInstance && typeof visualViewState.chartInstance.resize === "function") {
        try {
          visualViewState.chartInstance.resize();
        } catch (_) {
          // Ignore chart resize errors.
        }
      }

      if (visualViewState.networkInstance) {
        safeNetworkFit(visualViewState.networkInstance, 0);
      }
    }, 130);
  }

  window.addEventListener("resize", function() {
    scheduleVisualViewRelayout();
  });

  async function renderMermaidInVisualView(source) {
    source = normalizeMermaidSourceForRender(source || "");
    var pre = document.createElement("pre");
    pre.className = "mermaid";
    pre.textContent = source;
    visualViewCanvasWrap.appendChild(pre);

    var ready = await ensureMermaidReady();
    if (!ready) {
      throw new Error("renderer unavailable");
    }
    ensureMermaidThemeInitialized("preserve_ui_theme");

    if (typeof window.mermaid.parse === "function") {
      var parseResult = window.mermaid.parse(source);
      if (parseResult && typeof parseResult.then === "function") {
        await withVisualTimeout(parseResult, ixVisualMermaidState.renderTimeoutMs);
      }
    }

    var renderId = "ix-visual-view-mermaid-" + String(ixVisualMermaidState.nextRenderId++);
    var renderResult = await withVisualTimeout(
      Promise.resolve(window.mermaid.render(renderId, source)),
      ixVisualMermaidState.renderTimeoutMs);

    var svg = "";
    if (typeof renderResult === "string") {
      svg = renderResult;
    } else if (renderResult && typeof renderResult === "object") {
      svg = typeof renderResult.svg === "string" ? renderResult.svg : "";
    }
    if (!svg) {
      throw new Error("missing svg");
    }
    pre.innerHTML = svg;
  }

  async function renderChartInVisualView(source) {
    var ready = await ensureChartReady();
    if (!ready) {
      throw new Error("renderer unavailable");
    }

    var parsedConfig = JSON.parse(source);
    var validation = validateIxChartConfig(parsedConfig);
    if (!validation.ok) {
      throw new Error(validation.reason || "schema validation failed");
    }

    var host = document.createElement("div");
    host.className = "visual-chart-host";
    var canvas = document.createElement("canvas");
    canvas.className = "visual-chart-canvas";
    host.appendChild(canvas);
    visualViewCanvasWrap.appendChild(host);

    var chartConfig = cloneVisualValue(validation.config) || validation.config;
    chartConfig = applyChartThemeDefaults(chartConfig, "preserve_ui_theme", {
      responsive: true,
      maintainAspectRatio: false
    });
    visualViewState.chartInstance = new window.Chart(canvas.getContext("2d"), chartConfig);
    try {
      visualViewState.chartInstance.resize();
    } catch (_) {
      // Ignore.
    }
  }

  async function renderNetworkInVisualView(source) {
    var ready = await ensureNetworkReady();
    if (!ready) {
      throw new Error("renderer unavailable");
    }

    var parsedConfig = JSON.parse(source);
    var validation = validateIxNetworkConfig(parsedConfig);
    if (!validation.ok) {
      throw new Error(validation.reason || "schema validation failed");
    }

    var host = document.createElement("div");
    host.className = "visual-network-host";
    var canvas = document.createElement("div");
    canvas.className = "visual-network-canvas";
    host.appendChild(canvas);
    visualViewCanvasWrap.appendChild(host);

    var options = buildRuntimeNetworkOptions(
      "preserve_ui_theme",
      validation.config.options || {},
      "panel");

    visualViewState.networkInstance = new window.vis.Network(canvas, {
      nodes: validation.config.nodes,
      edges: validation.config.edges
    }, options);
    visualViewState.networkResizeCleanup = attachNetworkAutoFitObserver(visualViewState.networkInstance, canvas);

    stabilizeNetwork(visualViewState.networkInstance, 360);
    safeNetworkFit(visualViewState.networkInstance, 160);
    try {
      visualViewState.networkInstance.once("stabilized", function() {
        safeNetworkFit(visualViewState.networkInstance, 0);
      });
    } catch (_) {
      // Ignore listener setup failures.
    }

    window.setTimeout(function() {
      if (!visualViewState.networkInstance || visualViewState.type !== "ix-network") {
        return;
      }
      try {
        safeNetworkFit(visualViewState.networkInstance, 180);
      } catch (_) {
        // Ignore.
      }
    }, 60);
  }

  async function renderVisualViewContent() {
    if (!visualViewCanvasWrap) {
      return;
    }

    disposeVisualViewRuntime();
    if (!visualViewMeta) {
      return;
    }

    var type = normalizeVisualType(visualViewState.type);
    var source = String(visualViewState.source || "").trim();
    if (!source) {
      setVisualViewFeedback("Missing visual source.", "bad", 0);
      return;
    }

    visualViewMeta.textContent = getVisualKindLabel(type) + " · " + String(source.length) + " chars";
    if (btnVisualViewExportSvg) {
      btnVisualViewExportSvg.hidden = type !== "mermaid";
    }

    try {
      var entry = getNativeVisualRegistryEntry(type);
      if (type === "mermaid") {
        await renderMermaidInVisualView(source);
      } else if (entry && typeof entry.renderVisualView === "function") {
        await entry.renderVisualView(source);
      } else {
        setVisualViewFeedback("Unsupported visual type.", "bad", 0);
        return;
      }

      setVisualViewFeedback("", "info", 0);
    } catch (err) {
      var message = err && err.message ? err.message : "render failed";
      setVisualViewFeedback("Visual render failed: " + message, "bad", 0);
    }
  }

  function closeVisualView() {
    document.body.classList.remove("visual-view-open");
    ensureVisualViewClosedState();
    if (visualViewPanel) {
      visualViewPanel.setAttribute("aria-hidden", "true");
    }
    disposeVisualViewRuntime();
    clearVisualViewFeedbackTimer();
  }

  function openVisualView(type, source, title) {
    initializeVisualViewLifecycleGuards();

    var normalizedType = normalizeVisualType(type);
    var normalizedSource = normalizeText(source || "");
    if (!normalizedSource) {
      return;
    }

    if (document.body.classList.contains("options-open") && typeof closeOptions === "function") {
      closeOptions();
    }
    if (document.body.classList.contains("data-view-open") && window.ixCloseDataView) {
      window.ixCloseDataView();
    }

    visualViewState.sessionId += 1;
    visualViewState.type = normalizedType;
    visualViewState.source = normalizedSource;
    visualViewState.title = String(title || getVisualKindLabel(normalizedType)).trim() || getVisualKindLabel(normalizedType);
    pendingVisualExports = Object.create(null);
    setVisualViewExportButtonsBusy();
    setVisualViewPopoutBusy(false);
    setVisualViewExportPath("", "");
    setVisualViewMaximized(false);

    if (visualViewTitle) {
      visualViewTitle.textContent = visualViewState.title;
    }

    document.body.classList.add("visual-view-open");
    if (visualViewPanel) {
      visualViewPanel.setAttribute("aria-hidden", "false");
    }

    renderVisualViewContent();
    scheduleVisualViewRelayout();
  }

  function inferVisualTitleFromBlock(pre, kind) {
    var label = getVisualKindLabel(kind);
    if (!pre || !pre.closest) {
      return label;
    }

    var msg = pre.closest(".msg");
    if (!msg) {
      return label;
    }

    var meta = msg.querySelector(".meta");
    var suffix = meta ? String(meta.textContent || "").trim() : "";
    if (!suffix) {
      return label;
    }
    return label + " · " + suffix;
  }

  function openVisualViewFromBlock(pre, kind) {
    if (!pre) {
      return;
    }

    var normalizedKind = getOfficeImoVisualKind(pre, kind);
    var source = "";
    if (normalizedKind === "mermaid") {
      source = String(pre.getAttribute("data-ix-mermaid-source") || "").trim();
    } else {
      source = getOfficeImoVisualSourceByKind(pre, normalizedKind);
    }
    if (!source) {
      source = normalizeText(pre.textContent || "");
    }
    if (!source) {
      return;
    }

    openVisualView(normalizedKind, source, inferVisualTitleFromBlock(pre, normalizedKind));
  }

  async function convertSvgPayloadToPng(payload, themeMode, targetSize) {
    if (!payload || payload.mimeType !== "image/svg+xml" || !payload.dataBase64) {
      return payload;
    }

    var dataUrl = "data:image/svg+xml;base64," + payload.dataBase64;
    return new Promise(function(resolve) {
      var image = new Image();
      image.onload = function() {
        try {
          var naturalWidth = Math.max(1, Math.round(image.naturalWidth || 1280));
          var naturalHeight = Math.max(1, Math.round(image.naturalHeight || 720));
          var requestedWidth = normalizeVisualRenderDimension(
            targetSize && targetSize.width,
            naturalWidth,
            320,
            2000);
          var requestedHeight = normalizeVisualRenderDimension(
            targetSize && targetSize.height,
            Math.round((naturalHeight / naturalWidth) * requestedWidth),
            180,
            2000);
          var width = Math.max(1, requestedWidth);
          var height = Math.max(1, requestedHeight);
          var canvas = document.createElement("canvas");
          canvas.width = width;
          canvas.height = height;
          var context = canvas.getContext("2d");
          if (!context) {
            resolve(null);
            return;
          }

          var palette = resolveVisualExportPalette(themeMode);
          context.fillStyle = palette.background || "#ffffff";
          context.fillRect(0, 0, width, height);
          context.drawImage(image, 0, 0, width, height);

          var pngData = parseDataUrlPayload(canvas.toDataURL("image/png"));
          if (!pngData || !pngData.dataBase64) {
            resolve(null);
            return;
          }

          resolve({
            id: payload.id,
            alt: payload.alt,
            mimeType: pngData.mimeType || "image/png",
            dataBase64: pngData.dataBase64
          });
        } catch (_) {
          resolve(null);
        }
      };
      image.onerror = function() {
        resolve(null);
      };
      image.src = dataUrl;
    });
  }

  async function buildVisualExportPayload(type, source, format) {
    var visualType = normalizeVisualType(type);
    var exportFormat = normalizeVisualExportFormat(format, visualType);
    var themeMode = getVisualExportPreferences().visualThemeMode;
    var entry = getNativeVisualRegistryEntry(visualType);
    if (entry && typeof source === "string") {
      var state = entry.getState ? entry.getState() : null;
      if (state && typeof state.maxSourceChars === "number" && source.length > state.maxSourceChars) {
        return {
          payload: null,
          error: resolveVisualExportBuildFailureMessage(visualType, exportFormat)
        };
      }
    }

    if (exportFormat === "svg") {
      if (visualType !== "mermaid") {
        return {
          payload: null,
          error: "SVG export is only available for Mermaid diagrams."
        };
      }
      var svgPayload = await renderMermaidForExport(source, "panel", themeMode);
      if (!svgPayload || !svgPayload.dataBase64) {
        return {
          payload: null,
          error: resolveVisualExportBuildFailureMessage(visualType, exportFormat)
        };
      }

      return {
        payload: svgPayload,
        error: ""
      };
    }

    var rendered = entry && typeof entry.renderExport === "function"
      ? await entry.renderExport(source, "panel", themeMode)
      : await renderVisualFenceForExport(visualType, source, "panel", themeMode);
    if (!rendered) {
      var panelFallback = tryCaptureVisualViewCanvasPayload(visualType);
      if (panelFallback && panelFallback.dataBase64) {
        return {
          payload: panelFallback,
          error: ""
        };
      }

      return {
        payload: null,
        error: resolveVisualExportBuildFailureMessage(visualType, exportFormat)
      };
    }

    if (rendered.mimeType === "image/svg+xml") {
      var pngPayload = await convertSvgPayloadToPng(rendered, themeMode);
      if (!pngPayload || !pngPayload.dataBase64) {
        return {
          payload: null,
          error: "Visual export couldn't convert SVG output to PNG."
        };
      }

      return {
        payload: pngPayload,
        error: ""
      };
    }

    if (!rendered.dataBase64) {
      return {
        payload: null,
        error: resolveVisualExportBuildFailureMessage(visualType, exportFormat)
      };
    }

    return {
      payload: rendered,
      error: ""
    };
  }

  async function executeVisualExport(exportId, outputPath) {
    var pending = pendingVisualExports[exportId];
    if (!pending) {
      return;
    }

    try {
      var exportBuild = await buildVisualExportPayload(pending.type, pending.source, pending.format);
      var payload = exportBuild && exportBuild.payload ? exportBuild.payload : null;
      if (!payload || !payload.dataBase64) {
        delete pendingVisualExports[exportId];
        setVisualViewExportButtonsBusy();
        var errorMessage = exportBuild && exportBuild.error
          ? exportBuild.error
          : "Visual export couldn't prepare the image payload before save.";
        setVisualViewFeedback(errorMessage, "bad", 0);
        return;
      }

      post("export_visual_artifact", {
        exportId: exportId,
        format: pending.format,
        title: pending.title || "visual",
        outputPath: outputPath,
        mimeType: payload.mimeType || "",
        dataBase64: payload.dataBase64
      });
    } catch (err) {
      delete pendingVisualExports[exportId];
      setVisualViewExportButtonsBusy();
      var message = err && err.message
        ? "Visual export preparation failed: " + err.message
        : "Visual export couldn't prepare the image payload before save.";
      setVisualViewFeedback(message, "bad", 0);
    }
  }

  function startVisualExport(format) {
    var source = String(visualViewState.source || "").trim();
    var type = normalizeVisualType(visualViewState.type);
    if (!source || !type) {
      setVisualViewFeedback("No active visual to export.", "warn", 2600);
      return;
    }

    var resolvedFormat = normalizeVisualExportFormat(format, type);
    var exportId = buildVisualExportId(resolvedFormat);
    pendingVisualExports[exportId] = {
      format: resolvedFormat,
      type: type,
      source: source,
      title: visualViewState.title || getVisualKindLabel(type),
      sessionId: visualViewState.sessionId
    };
    setVisualViewExportButtonsBusy();

    var autoPath = buildVisualExportPath(resolvedFormat, visualViewState.title || getVisualKindLabel(type));
    if (autoPath) {
      pendingVisualExports[exportId].path = autoPath;
      setVisualViewFeedback("Exporting to last folder...", "info", 0);
      executeVisualExport(exportId, autoPath);
      return;
    }

    setVisualViewFeedback("Choose where to save...", "info", 0);
    post("pick_visual_export_path", {
      requestId: exportId,
      format: resolvedFormat,
      title: visualViewState.title || getVisualKindLabel(type)
    });
  }

  function startVisualPopout() {
    if (visualViewState.popoutInFlight) {
      return;
    }

    var source = String(visualViewState.source || "").trim();
    var type = normalizeVisualType(visualViewState.type);
    if (!source || !type) {
      setVisualViewFeedback("No active visual to popout.", "warn", 2600);
      return;
    }

    var preferredFormat = type === "mermaid" ? "svg" : "png";
    setVisualViewPopoutBusy(true);
    setVisualViewFeedback("Opening visual in external viewer...", "info", 0);
    buildVisualExportPayload(type, source, preferredFormat)
      .then(function(exportBuild) {
        var payload = exportBuild && exportBuild.payload ? exportBuild.payload : null;
        if (!payload || !payload.dataBase64) {
          var detail = exportBuild && exportBuild.error
            ? exportBuild.error
            : "Visual popout couldn't prepare the image payload.";
          throw new Error(detail);
        }

        post("open_visual_popout", {
          title: visualViewState.title || getVisualKindLabel(type),
          mimeType: payload.mimeType || (preferredFormat === "svg" ? "image/svg+xml" : "image/png"),
          dataBase64: payload.dataBase64
        });
      })
      .catch(function(err) {
        var message = err && err.message ? err.message : "Visual popout couldn't prepare the image payload.";
        setVisualViewPopoutBusy(false);
        setVisualViewFeedback(message, "bad", 0);
      });
  }

  window.ixOnVisualExportPathSelected = function(payload) {
    payload = payload || {};
    var requestId = String(payload.requestId || "");
    if (!requestId) {
      return;
    }

    var pending = pendingVisualExports[requestId];
    if (!pending) {
      return;
    }

    var ok = payload.ok === true;
    var path = String(payload.path || "").trim();
    var message = String(payload.message || "").trim();
    var canceled = payload.canceled === true;

    if (!ok || !path) {
      delete pendingVisualExports[requestId];
      setVisualViewExportButtonsBusy();
      if (!message) {
        message = canceled ? "Export canceled." : "Failed to select save location.";
      }
      setVisualViewFeedback(message, canceled ? "warn" : "bad", canceled ? 3200 : 0);
      return;
    }

    pending.path = path;
    var selectedDir = typeof getDirectoryFromPath === "function" ? getDirectoryFromPath(path) : "";
    if (selectedDir) {
      state.options.export = state.options.export || {};
      state.options.export.lastDirectory = selectedDir;
      if (typeof renderExportPreferences === "function") {
        renderExportPreferences();
      }
    }

    setVisualViewFeedback("Exporting...", "info", 0);
    executeVisualExport(requestId, path);
  };

  window.ixOnVisualExportResult = function(payload) {
    payload = payload || {};
    var exportId = String(payload.exportId || "");
    if (!exportId || !Object.prototype.hasOwnProperty.call(pendingVisualExports, exportId)) {
      return;
    }

    var pending = pendingVisualExports[exportId];
    delete pendingVisualExports[exportId];
    if (pending && pending.sessionId && pending.sessionId !== visualViewState.sessionId) {
      setVisualViewExportButtonsBusy();
      return;
    }
    setVisualViewExportButtonsBusy();

    var ok = payload.ok === true;
    var tone = ok ? "ok" : "bad";
    var message = String(payload.message || "").trim();
    var path = String(payload.filePath || "").trim();
    if (ok && !path && pending && pending.path) {
      path = String(pending.path || "").trim();
    }

    if (!message) {
      if (ok) {
        var fileName = visualFileNameFromPath(path);
        message = fileName ? ("Exported: " + fileName) : "Export completed.";
      } else {
        message = "Export failed.";
      }
    }

    if (ok && path) {
      setVisualViewExportPath(path, payload.format || "");
      var dir = typeof getDirectoryFromPath === "function" ? getDirectoryFromPath(path) : "";
      if (dir) {
        state.options.export = state.options.export || {};
        state.options.export.lastDirectory = dir;
        if (typeof renderExportPreferences === "function") {
          renderExportPreferences();
        }
      }
    }

    setVisualViewFeedback(message, tone, ok ? 6000 : 0);
  };

  window.ixOnVisualExportActionResult = function(payload) {
    payload = payload || {};
    var ok = payload.ok === true;
    var message = String(payload.message || "").trim();
    if (!message) {
      message = ok ? "Done." : "Action failed.";
    }
    setVisualViewFeedback(message, ok ? "ok" : "bad", ok ? 3500 : 0);
  };

  window.ixOnVisualPopoutResult = function(payload) {
    payload = payload || {};
    var ok = payload.ok === true;
    var message = String(payload.message || "").trim();
    setVisualViewPopoutBusy(false);
    if (!message) {
      message = ok ? "Opened visual popout." : "Visual popout failed.";
    }
    setVisualViewFeedback(message, ok ? "ok" : "bad", ok ? 4200 : 0);
  };

  window.ixCloseVisualView = closeVisualView;
  window.ixOpenVisualView = function(type, source, title) {
    openVisualView(type, source, title);
  };
  initializeVisualViewLifecycleGuards();

  if (visualViewBackdrop) {
    visualViewBackdrop.addEventListener("click", closeVisualView);
  }
  if (btnVisualViewClose) {
    btnVisualViewClose.addEventListener("click", closeVisualView);
  }
  if (btnVisualViewExportPng) {
    btnVisualViewExportPng.addEventListener("click", function() {
      startVisualExport("png");
    });
  }
  if (btnVisualViewExportSvg) {
    btnVisualViewExportSvg.addEventListener("click", function() {
      startVisualExport("svg");
    });
  }
  if (btnVisualViewPopout) {
    btnVisualViewPopout.addEventListener("click", function() {
      startVisualPopout();
    });
  }
  if (btnVisualViewToggleSize) {
    btnVisualViewToggleSize.addEventListener("click", function() {
      setVisualViewMaximized(!visualViewState.isMaximized);
    });
  }
  if (btnVisualViewOpenExport) {
    btnVisualViewOpenExport.addEventListener("click", function() {
      triggerVisualExportAction("open");
    });
  }
  if (btnVisualViewRevealExport) {
    btnVisualViewRevealExport.addEventListener("click", function() {
      triggerVisualExportAction("reveal");
    });
  }
  if (btnVisualViewCopyExportPath) {
    btnVisualViewCopyExportPath.addEventListener("click", function() {
      triggerVisualExportAction("copy_path");
    });
  }
  renderVisualViewSizeToggle();

  var ixVisualExportState = {
    maxImages: 24,
    renderTimeoutMs: 3500,
    chartWidth: 1200,
    chartHeight: 760,
    networkWidth: 1280,
    networkHeight: 760
  };

  function normalizeVisualExportThemeMode(value) {
    var normalized = String(value || "").trim().toLowerCase();
    if (normalized === "print_friendly" || normalized === "print" || normalized === "light") {
      return "print_friendly";
    }
    return "preserve_ui_theme";
  }

  function normalizeColorToken(value) {
    return String(value || "")
      .trim()
      .toLowerCase()
      .replace(/\s+/g, "");
  }

  function normalizeVisualPalette(palette, themeMode) {
    var normalizedThemeMode = normalizeVisualExportThemeMode(themeMode);
    var background = String((palette && palette.background) || "").trim();
    var surface = String((palette && palette.surface) || "").trim();
    var text = String((palette && palette.text) || "").trim();
    var muted = String((palette && palette.muted) || "").trim();
    var grid = String((palette && palette.grid) || "").trim();
    var accentInput = palette && Array.isArray(palette.accents) ? palette.accents : [];

    var blocked = {};
    var bgKey = normalizeColorToken(background);
    var surfaceKey = normalizeColorToken(surface);
    if (bgKey) {
      blocked[bgKey] = true;
    }
    if (surfaceKey) {
      blocked[surfaceKey] = true;
    }

    var seen = {};
    var accents = [];
    for (var i = 0; i < accentInput.length; i++) {
      var color = String(accentInput[i] || "").trim();
      if (!color) {
        continue;
      }
      var key = normalizeColorToken(color);
      if (!key || blocked[key] || seen[key]) {
        continue;
      }
      seen[key] = true;
      accents.push(color);
    }

    var fallbackAccents = normalizedThemeMode === "print_friendly"
      ? ["#2563eb", "#059669", "#ea580c", "#db2777", "#7c3aed", "#0891b2"]
      : ["#4cc3ff", "#66d9a8", "#ffb86b", "#ff7ea7", "#9e9eff", "#9be7ff"];
    for (var j = 0; j < fallbackAccents.length && accents.length < 6; j++) {
      var fallback = fallbackAccents[j];
      var fallbackKey = normalizeColorToken(fallback);
      if (!fallbackKey || blocked[fallbackKey] || seen[fallbackKey]) {
        continue;
      }
      seen[fallbackKey] = true;
      accents.push(fallback);
    }

    if (accents.length === 0) {
      accents = fallbackAccents.slice(0, 4);
    }

    return {
      background: background || (normalizedThemeMode === "print_friendly" ? "#ffffff" : "#0f172a"),
      surface: surface || (normalizedThemeMode === "print_friendly" ? "#f8fbff" : "#111f35"),
      text: text || (normalizedThemeMode === "print_friendly" ? "#1f2933" : "#eaf3ff"),
      muted: muted || (normalizedThemeMode === "print_friendly" ? "#4b5b6b" : "#a3bad1"),
      grid: grid || (normalizedThemeMode === "print_friendly" ? "#d8e2ee" : "#35506a"),
      accents: accents
    };
  }

  function resolveVisualExportPalette(themeMode) {
    if (themeMode === "print_friendly") {
      return normalizeVisualPalette({
        background: "#ffffff",
        surface: "#f8fbff",
        text: "#1f2933",
        muted: "#4b5b6b",
        grid: "#d8e2ee",
        accents: ["#2563eb", "#059669", "#ea580c", "#db2777", "#7c3aed", "#0891b2"]
      }, themeMode);
    }

    var rootStyles = window.getComputedStyle ? window.getComputedStyle(document.documentElement) : null;
    var bg = rootStyles ? String(rootStyles.getPropertyValue("--ix-bg") || "").trim() : "";
    var panel = rootStyles ? String(rootStyles.getPropertyValue("--ix-panel") || "").trim() : "";
    var text = rootStyles ? String(rootStyles.getPropertyValue("--ix-text") || "").trim() : "";
    var muted = rootStyles ? String(rootStyles.getPropertyValue("--ix-muted") || "").trim() : "";
    var border = rootStyles ? String(rootStyles.getPropertyValue("--ix-border") || "").trim() : "";
    var accent = rootStyles ? String(rootStyles.getPropertyValue("--ix-accent") || "").trim() : "";
    return normalizeVisualPalette({
      background: bg || "#0f172a",
      surface: panel || "#111f35",
      text: text || "#eaf3ff",
      muted: muted || "#a3bad1",
      grid: border || "#35506a",
      accents: [accent || "#4cc3ff", "#66d9a8", "#ffb86b", "#ff7ea7", "#9e9eff", "#9be7ff"]
    }, themeMode);
  }

  function utf8ToBase64(value) {
    try {
      return btoa(unescape(encodeURIComponent(String(value || ""))));
    } catch (_) {
      return "";
    }
  }

  function parseDataUrlPayload(dataUrl) {
    if (typeof dataUrl !== "string") {
      return null;
    }

    var match = /^data:([^;,]+);base64,(.+)$/i.exec(dataUrl.trim());
    if (!match) {
      return null;
    }

    return {
      mimeType: String(match[1] || "").trim().toLowerCase(),
      dataBase64: String(match[2] || "").trim()
    };
  }

  function tryReadFenceRun(line) {
    var trimmed = String(line || "").replace(/^\s+/, "");
    if (!trimmed) {
      return null;
    }

    var marker = trimmed.charAt(0);
    if (marker !== "`" && marker !== "~") {
      return null;
    }

    var idx = 0;
    while (idx < trimmed.length && trimmed.charAt(idx) === marker) {
      idx += 1;
    }

    if (idx < 3) {
      return null;
    }

    return {
      marker: marker,
      runLength: idx,
      suffix: trimmed.slice(idx)
    };
  }

  function tryReadFenceStart(line) {
    var run = tryReadFenceRun(line);
    if (!run) {
      return null;
    }

    var language = "";
    var suffix = String(run.suffix || "").trim();
    if (suffix) {
      var pieces = suffix.split(/\s+/, 2);
      language = String(pieces[0] || "").trim().toLowerCase();
    }

    return {
      marker: run.marker,
      runLength: run.runLength,
      language: language
    };
  }

  function isClosingFenceLine(line, marker, runLength) {
    var run = tryReadFenceRun(line);
    if (!run || run.marker !== marker || run.runLength < runLength) {
      return false;
    }

    return String(run.suffix || "").trim().length === 0;
  }

  function appendFenceLines(lines, output, fromIndex, toIndex) {
    for (var idx = fromIndex; idx <= toIndex; idx++) {
      output.push(String(lines[idx] || ""));
    }
  }

  function findFenceClosingIndex(lines, startIndex, marker, runLength) {
    for (var i = startIndex; i < lines.length; i++) {
      if (isClosingFenceLine(lines[i], marker, runLength)) {
        return i;
      }
    }

    return -1;
  }

  function detectLineEnding(text) {
    if (String(text || "").indexOf("\r\n") >= 0) {
      return "\r\n";
    }
    if (String(text || "").indexOf("\r") >= 0) {
      return "\r";
    }
    return "\n";
  }

  function normalizeMermaidExportSvg(svg) {
    if (typeof svg !== "string" || !svg) {
      return "";
    }

    // Keep Mermaid SVG XML-compatible for strict parsers (Edge/file viewers, Office renderers).
    return svg.replace(/<br\s*\/?\s*>/gi, "<br/>");
  }

  function withVisualExportBackground(svg, palette) {
    if (typeof svg !== "string" || !svg) {
      return "";
    }

    var viewBoxMatch = /viewBox=["']([^"']+)["']/i.exec(svg);
    var widthMatch = /width=["']([^"']+)["']/i.exec(svg);
    var heightMatch = /height=["']([^"']+)["']/i.exec(svg);
    var width = widthMatch ? String(widthMatch[1]) : "100%";
    var height = heightMatch ? String(heightMatch[1]) : "100%";
    var fill = (palette && palette.background) ? palette.background : "#ffffff";
    var rect = "<rect width='" + width + "' height='" + height + "' fill='" + fill + "'/>";
    if (viewBoxMatch) {
      rect = "<rect width='100%' height='100%' fill='" + fill + "'/>";
    }

    if (svg.indexOf(">") < 0) {
      return svg;
    }
    var insertAt = svg.indexOf(">") + 1;
    return svg.slice(0, insertAt) + rect + svg.slice(insertAt);
  }

  function deepMergeForExport(target, source) {
    if (!isPlainObject(source)) {
      return target;
    }

    var keys = Object.keys(source);
    for (var i = 0; i < keys.length; i++) {
      var key = keys[i];
      var sourceValue = source[key];
      if (isPlainObject(sourceValue)) {
        if (!isPlainObject(target[key])) {
          target[key] = {};
        }
        deepMergeForExport(target[key], sourceValue);
        continue;
      }

      target[key] = sourceValue;
    }

    return target;
  }

  function cloneVisualValue(value) {
    try {
      return JSON.parse(JSON.stringify(value));
    } catch (_) {
      return value;
    }
  }

  function safeNetworkFit(network, durationMs) {
    if (!network || typeof network.fit !== "function") {
      return;
    }

    var duration = Number(durationMs);
    if (!Number.isFinite(duration) || duration < 0) {
      duration = 0;
    }

    try {
      if (duration > 0) {
        network.fit({
          animation: {
            duration: duration
          }
        });
      } else {
        network.fit({
          animation: false
        });
      }
    } catch (_) {
      // Ignore fit failures.
    }
  }

  function stabilizeNetwork(network, iterations) {
    if (!network || typeof network.stabilize !== "function") {
      return;
    }

    var count = Number(iterations);
    if (!Number.isFinite(count) || count <= 0) {
      count = 260;
    }

    try {
      network.stabilize(Math.round(count));
    } catch (_) {
      // Ignore stabilization failures.
    }
  }

  function attachNetworkAutoFitObserver(network, element) {
    if (!network || !element || typeof ResizeObserver !== "function") {
      return null;
    }

    var timer = 0;
    var lastWidth = 0;
    var lastHeight = 0;
    var observer = null;

    function scheduleFit() {
      if (timer) {
        window.clearTimeout(timer);
      }
      timer = window.setTimeout(function() {
        timer = 0;
        safeNetworkFit(network, 0);
      }, 120);
    }

    try {
      observer = new ResizeObserver(function(entries) {
        if (!entries || entries.length === 0) {
          return;
        }

        var rect = entries[0].contentRect;
        if (!rect) {
          return;
        }

        var width = Number(rect.width);
        var height = Number(rect.height);
        if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0) {
          return;
        }

        if (Math.abs(width - lastWidth) < 6 && Math.abs(height - lastHeight) < 6) {
          return;
        }

        lastWidth = width;
        lastHeight = height;
        scheduleFit();
      });
      observer.observe(element);
    } catch (_) {
      if (observer && typeof observer.disconnect === "function") {
        observer.disconnect();
      }
      return null;
    }

    return function() {
      if (timer) {
        window.clearTimeout(timer);
        timer = 0;
      }
      if (observer && typeof observer.disconnect === "function") {
        observer.disconnect();
      }
    };
  }

  function buildRuntimeNetworkOptions(themeMode, userOptions, context) {
    var palette = resolveVisualExportPalette(themeMode);
    var profile = String(context || "").trim().toLowerCase();
    var stabilizationIterations = 280;
    if (profile === "panel") {
      stabilizationIterations = 360;
    } else if (profile === "export") {
      stabilizationIterations = 320;
    }

    var normalizedThemeMode = normalizeVisualExportThemeMode(themeMode);
    var labelBackground = normalizedThemeMode === "print_friendly"
      ? "rgba(255, 255, 255, 0.88)"
      : "rgba(7, 25, 44, 0.72)";

    var defaults = {
      autoResize: true,
      layout: {
        improvedLayout: true,
        randomSeed: 42
      },
      physics: {
        enabled: true,
        solver: "forceAtlas2Based",
        forceAtlas2Based: {
          gravitationalConstant: -62,
          centralGravity: 0.015,
          springLength: 220,
          springConstant: 0.035,
          damping: 0.62,
          avoidOverlap: 0.9
        },
        stabilization: {
          enabled: true,
          fit: true,
          iterations: stabilizationIterations,
          updateInterval: 25
        },
        minVelocity: 0.75,
        adaptiveTimestep: true
      },
      interaction: {
        hover: true
      },
      nodes: {
        shape: "box",
        margin: 12,
        widthConstraint: {
          maximum: 320
        },
        font: {
          size: 14,
          color: palette.text,
          strokeWidth: 0,
          multi: true
        },
        color: {
          background: palette.surface,
          border: palette.grid
        }
      },
      edges: {
        color: palette.grid,
        width: 1.2,
        labelHighlightBold: false,
        font: {
          size: 12,
          color: palette.muted,
          strokeWidth: 0,
          background: labelBackground,
          align: "top"
        },
        smooth: {
          enabled: true,
          type: "continuous",
          roundness: 0.28
        }
      }
    };

    return deepMergeForExport(defaults, userOptions || {});
  }

  function applyChartPalette(config, palette) {
    if (!config || !config.data || !Array.isArray(config.data.datasets)) {
      return;
    }

    var datasets = config.data.datasets;
    for (var i = 0; i < datasets.length; i++) {
      var dataset = datasets[i];
      if (!dataset || typeof dataset !== "object") {
        continue;
      }

      var accent = palette.accents[i % palette.accents.length];
      if (dataset.borderColor == null || dataset.borderColor === "") {
        dataset.borderColor = accent;
      }
      if (dataset.backgroundColor == null || dataset.backgroundColor === "") {
        dataset.backgroundColor = accent;
      }
    }
  }

  function applyChartThemeDefaults(config, themeMode, optionsOverride) {
    if (!config || !config.data || !Array.isArray(config.data.datasets)) {
      return config;
    }

    var palette = resolveVisualExportPalette(themeMode);
    var defaults = {
      responsive: true,
      maintainAspectRatio: false,
      animation: false,
      plugins: {
        legend: { labels: { color: palette.text } },
        title: { color: palette.text },
        tooltip: { enabled: true }
      },
      scales: {
        x: { ticks: { color: palette.muted }, grid: { color: palette.grid } },
        y: { ticks: { color: palette.muted }, grid: { color: palette.grid } }
      }
    };
    if (isPlainObject(optionsOverride)) {
      defaults = deepMergeForExport(defaults, optionsOverride);
    }

    config.options = deepMergeForExport(defaults, config.options || {});
    applyChartPalette(config, palette);
    return config;
  }

  async function renderMermaidForExport(source, imageId, themeMode) {
    source = normalizeMermaidSourceForRender(source || "");
    var ready = await ensureMermaidReady();
    if (!ready) {
      return null;
    }

    if (!source || source.length > ixVisualMermaidState.maxSourceChars) {
      return null;
    }

    var normalizedThemeMode = normalizeVisualExportThemeMode(themeMode);
    var restoreUiTheme = normalizedThemeMode !== "preserve_ui_theme";
    try {
      ensureMermaidThemeInitialized(normalizedThemeMode, "export");

      if (typeof window.mermaid.parse === "function") {
        var parseResult = window.mermaid.parse(source);
        if (parseResult && typeof parseResult.then === "function") {
          await withVisualTimeout(parseResult, ixVisualExportState.renderTimeoutMs);
        }
      }

      var renderResult = await withVisualTimeout(
        Promise.resolve(window.mermaid.render("ix-export-mermaid-" + String(imageId), source)),
        ixVisualExportState.renderTimeoutMs);

      var svg = "";
      if (typeof renderResult === "string") {
        svg = renderResult;
      } else if (renderResult && typeof renderResult === "object") {
        svg = typeof renderResult.svg === "string" ? renderResult.svg : "";
      }

      if (!svg) {
        return null;
      }

      svg = normalizeMermaidExportSvg(svg);
      if (!svg) {
        return null;
      }

      var palette = resolveVisualExportPalette(themeMode);
      var themed = withVisualExportBackground(svg, palette);
      var encoded = utf8ToBase64(themed);
      if (!encoded) {
        return null;
      }

      return {
        id: String(imageId),
        alt: "Mermaid diagram",
        mimeType: "image/svg+xml",
        dataBase64: encoded
      };
    } catch (_) {
      return null;
    } finally {
      if (restoreUiTheme) {
        ensureMermaidThemeInitialized("preserve_ui_theme", "ui");
      }
    }
  }

  async function renderChartForExport(source, imageId, themeMode, renderSize) {
    var ready = await ensureChartReady();
    if (!ready) {
      return null;
    }

    var parsedConfig;
    try {
      parsedConfig = JSON.parse(source);
    } catch (_) {
      return null;
    }

    var validation = validateIxChartConfig(parsedConfig);
    if (!validation.ok) {
      return null;
    }

    var chartConfig = {
      type: validation.config.type,
      data: validation.config.data,
      options: validation.config.options || {}
    };
    chartConfig = cloneVisualValue(chartConfig) || chartConfig;
    chartConfig = applyChartThemeDefaults(chartConfig, themeMode, {
      responsive: false,
      maintainAspectRatio: false,
      animation: false
    });

    var palette = resolveVisualExportPalette(themeMode);
    var exportWidth = normalizeVisualRenderDimension(
      renderSize && renderSize.width,
      ixVisualExportState.chartWidth,
      320,
      2000);
    var exportHeight = normalizeVisualRenderDimension(
      renderSize && renderSize.height,
      ixVisualExportState.chartHeight,
      220,
      1800);
    var host = document.createElement("div");
    host.style.position = "fixed";
    host.style.left = "-10000px";
    host.style.top = "-10000px";
    host.style.width = String(exportWidth) + "px";
    host.style.height = String(exportHeight) + "px";
    host.style.background = palette.background;
    host.style.padding = "0";
    host.style.margin = "0";
    document.body.appendChild(host);

    var canvas = document.createElement("canvas");
    canvas.width = exportWidth;
    canvas.height = exportHeight;
    canvas.style.width = String(exportWidth) + "px";
    canvas.style.height = String(exportHeight) + "px";
    host.appendChild(canvas);

    var context = canvas.getContext("2d");
    if (!context) {
      host.remove();
      return null;
    }

    var chart = null;
    try {
      context.fillStyle = palette.background || "#ffffff";
      context.fillRect(0, 0, canvas.width, canvas.height);

      chart = new window.Chart(context, chartConfig);
      if (chart && typeof chart.update === "function") {
        chart.update("none");
      }
      await new Promise(function(resolve) {
        if (typeof window.requestAnimationFrame === "function") {
          window.requestAnimationFrame(function() {
            window.requestAnimationFrame(resolve);
          });
          return;
        }
        setTimeout(resolve, 48);
      });

      var dataUrl = "";
      if (chart && typeof chart.toBase64Image === "function") {
        dataUrl = chart.toBase64Image("image/png", 1) || "";
      }
      var parsedData = parseDataUrlPayload(dataUrl);
      if ((!parsedData || !parsedData.dataBase64) && canvas && typeof canvas.toDataURL === "function") {
        dataUrl = canvas.toDataURL("image/png");
        parsedData = parseDataUrlPayload(dataUrl);
      }
      if (!parsedData || !parsedData.dataBase64) {
        return null;
      }

      return {
        id: String(imageId),
        alt: "Chart preview",
        mimeType: parsedData.mimeType || "image/png",
        dataBase64: parsedData.dataBase64
      };
    } catch (_) {
      return null;
    } finally {
      if (chart && typeof chart.destroy === "function") {
        chart.destroy();
      }
      host.remove();
    }
  }

  async function renderNetworkForExport(source, imageId, themeMode, renderSize) {
    var ready = await ensureNetworkReady();
    if (!ready) {
      return null;
    }

    var parsedConfig;
    try {
      parsedConfig = JSON.parse(source);
    } catch (_) {
      return null;
    }

    var validation = validateIxNetworkConfig(parsedConfig);
    if (!validation.ok) {
      return null;
    }

    var palette = resolveVisualExportPalette(themeMode);
    var exportWidth = normalizeVisualRenderDimension(
      renderSize && renderSize.width,
      ixVisualExportState.networkWidth,
      320,
      2200);
    var exportHeight = normalizeVisualRenderDimension(
      renderSize && renderSize.height,
      ixVisualExportState.networkHeight,
      240,
      1800);
    var host = document.createElement("div");
    host.style.position = "fixed";
    host.style.left = "-10000px";
    host.style.top = "-10000px";
    host.style.width = String(exportWidth) + "px";
    host.style.height = String(exportHeight) + "px";
    host.style.background = palette.background;
    document.body.appendChild(host);

    var userDisabledPhysics = isPlainObject(validation.config.options) && validation.config.options.physics === false;
    var options = buildRuntimeNetworkOptions(themeMode, validation.config.options || {}, "export");
    options = deepMergeForExport(options, {
      autoResize: false,
      width: String(exportWidth) + "px",
      height: String(exportHeight) + "px",
      interaction: {
        dragNodes: false,
        dragView: false,
        zoomView: false
      },
      layout: {
        improvedLayout: true,
        randomSeed: 42
      }
    });
    if (userDisabledPhysics) {
      options.physics = false;
    } else {
      options = deepMergeForExport(options, {
        physics: {
          stabilization: {
            enabled: true,
            fit: true,
            iterations: 360
          },
          adaptiveTimestep: false
        },
        nodes: {
          font: {
            color: palette.text
          },
          color: {
            background: palette.surface,
            border: palette.grid
          }
        },
        edges: {
          color: {
            color: palette.grid
          },
          font: {
            color: palette.muted
          }
        }
      });
    }

    var network = null;
    try {
      network = new window.vis.Network(host, {
        nodes: validation.config.nodes,
        edges: validation.config.edges
      }, options);
      if (!userDisabledPhysics) {
        stabilizeNetwork(network, 360);
      }

      await new Promise(function(resolve) {
        var settled = false;
        var timeout = setTimeout(function() {
          if (settled) {
            return;
          }
          settled = true;
          resolve();
        }, userDisabledPhysics ? 280 : 1100);

        var finish = function() {
          if (settled) {
            return;
          }
          settled = true;
          clearTimeout(timeout);
          resolve();
        };

        try {
          network.once("afterDrawing", finish);
          if (!userDisabledPhysics) {
            network.once("stabilized", finish);
          }
        } catch (_) {
          // Fallback to timeout.
        }
      });

      safeNetworkFit(network, 0);
      try {
        network.redraw();
      } catch (_) {
        // Ignore redraw errors.
      }

      var canvas = host.querySelector("canvas");
      if (!canvas || typeof canvas.toDataURL !== "function") {
        return null;
      }

      var dataUrl = canvas.toDataURL("image/png");
      var parsedData = parseDataUrlPayload(dataUrl);
      if (!parsedData || !parsedData.dataBase64) {
        return null;
      }

      return {
        id: String(imageId),
        alt: "Network preview",
        mimeType: parsedData.mimeType || "image/png",
        dataBase64: parsedData.dataBase64
      };
    } catch (_) {
      return null;
    } finally {
      if (network && typeof network.destroy === "function") {
        try {
          network.destroy();
        } catch (_) {
          // Ignore.
        }
      }
      host.remove();
    }
  }

  async function renderVisualFenceForExport(language, source, imageId, themeMode, renderSize) {
    var normalized = normalizeVisualType(language);
    var content = normalizeText(source || "");
    if (!content) {
      return null;
    }

    if (normalized === "mermaid") {
      return renderMermaidForExport(content, imageId, themeMode);
    }

    if (normalized === "ix-chart") {
      if (content.length > ixVisualChartState.maxSourceChars) {
        return null;
      }
      return renderChartForExport(content, imageId, themeMode, renderSize);
    }

    if (normalized === "ix-network") {
      if (content.length > ixVisualNetworkState.maxSourceChars) {
        return null;
      }
      return renderNetworkForExport(content, imageId, themeMode, renderSize);
    }

    return null;
  }

  window.ixMaterializeVisualFencesForDocx = async function(request) {
    var sourceMarkdown = request && typeof request.markdown === "string"
      ? request.markdown
      : "";
    if (!sourceMarkdown) {
      return {
        markdown: "",
        images: []
      };
    }

    var themeMode = normalizeVisualExportThemeMode(request && request.themeMode);
    var docxVisualMaxWidthPx = normalizeDocxVisualMaxWidthPx(request && request.docxVisualMaxWidthPx);
    var newline = detectLineEnding(sourceMarkdown);
    var normalized = sourceMarkdown.replace(/\r\n/g, "\n").replace(/\r/g, "\n");
    var lines = normalized.split("\n");
    var output = [];
    var images = [];
    var imageCounter = 0;
    for (var i = 0; i < lines.length;) {
      var line = String(lines[i] || "");
      var fence = tryReadFenceStart(line);
      if (!fence) {
        output.push(line);
        i += 1;
        continue;
      }

      var normalizedFenceLanguage = normalizeVisualType(fence.language);
      var supportedLanguage = normalizedFenceLanguage === "mermaid"
        || normalizedFenceLanguage === "ix-chart"
        || normalizedFenceLanguage === "ix-network";
      if (!supportedLanguage) {
        output.push(line);
        i += 1;
        continue;
      }

      var closingIndex = findFenceClosingIndex(lines, i + 1, fence.marker, fence.runLength);
      if (closingIndex < 0) {
        output.push(line);
        i += 1;
        continue;
      }

      if (imageCounter >= ixVisualExportState.maxImages) {
        appendFenceLines(lines, output, i, closingIndex);
        i = closingIndex + 1;
        continue;
      }

      var source = lines.slice(i + 1, closingIndex).join("\n");
      var renderSize = resolveDocxRenderSize(normalizedFenceLanguage, docxVisualMaxWidthPx);
      var rendered = await renderVisualFenceForExport(normalizedFenceLanguage, source, imageCounter + 1, themeMode, renderSize);
      if (rendered && rendered.mimeType === "image/svg+xml") {
        rendered = await convertSvgPayloadToPng(rendered, themeMode, renderSize);
      }
      if (!rendered || !rendered.dataBase64 || !rendered.mimeType) {
        appendFenceLines(lines, output, i, closingIndex);
        i = closingIndex + 1;
        continue;
      }

      imageCounter += 1;
      var imageId = "img" + String(imageCounter);
      images.push({
        id: imageId,
        alt: rendered.alt || "Visual preview",
        mimeType: rendered.mimeType,
        dataBase64: rendered.dataBase64
      });
      output.push("![" + String(rendered.alt || "Visual preview") + "](<ix-export-image://" + imageId + ">)" + "{width=" + String(docxVisualMaxWidthPx) + "}");
      i = closingIndex + 1;
    }

    var rewritten = output.join("\n");
    if (newline !== "\n") {
      rewritten = rewritten.replace(/\n/g, newline);
    }

    return {
      markdown: rewritten,
      images: images
    };
  };
