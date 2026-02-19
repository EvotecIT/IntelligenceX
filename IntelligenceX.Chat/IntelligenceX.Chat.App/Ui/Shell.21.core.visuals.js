  var ixVisualMermaidState = {
    loadPromise: null,
    loadFailed: false,
    initialized: false,
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

  // Local assets are mapped via WebView2 virtual host mapping in MainWindow.
  var ixVisualAssets = {
    mermaidUrl: "https://ixchat.local/vendor/mermaid/mermaid.min.js",
    chartJsUrl: "https://ixchat.local/vendor/chartjs/chart.umd.min.js",
    visNetworkJsUrl: "https://ixchat.local/vendor/vis-network/vis-network.min.js",
    visNetworkCssUrl: "https://ixchat.local/vendor/vis-network/vis-network.min.css"
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

  function loadVisualScriptOnce(url, attrName, readyCheck) {
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
        return Promise.resolve(false);
      }
      if (existingState === "loaded") {
        return Promise.resolve(isReady());
      }

      return new Promise(function(resolve) {
        existing.addEventListener("load", function() { resolve(isReady()); }, { once: true });
        existing.addEventListener("error", function() { resolve(false); }, { once: true });
      });
    }

    return new Promise(function(resolve) {
      var script = document.createElement("script");
      script.src = url;
      script.async = true;
      script.setAttribute(attrName, "1");
      script.setAttribute("data-ix-load-state", "loading");
      script.addEventListener("load", function() {
        script.setAttribute("data-ix-load-state", "loaded");
        resolve(isReady());
      }, { once: true });
      script.addEventListener("error", function() {
        script.setAttribute("data-ix-load-state", "error");
        resolve(false);
      }, { once: true });
      document.head.appendChild(script);
    });
  }

  function loadVisualStylesheetOnce(url, attrName) {
    var existing = document.querySelector("link[" + attrName + "='1']");
    if (existing) {
      var existingState = existing.getAttribute("data-ix-load-state") || "";
      if (existingState === "error") {
        return Promise.resolve(false);
      }
      if (existingState === "loaded") {
        return Promise.resolve(true);
      }

      return new Promise(function(resolve) {
        existing.addEventListener("load", function() { resolve(true); }, { once: true });
        existing.addEventListener("error", function() { resolve(false); }, { once: true });
      });
    }

    return new Promise(function(resolve) {
      var link = document.createElement("link");
      link.rel = "stylesheet";
      link.href = url;
      link.setAttribute(attrName, "1");
      link.setAttribute("data-ix-load-state", "loading");
      link.addEventListener("load", function() {
        link.setAttribute("data-ix-load-state", "loaded");
        resolve(true);
      }, { once: true });
      link.addEventListener("error", function() {
        link.setAttribute("data-ix-load-state", "error");
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

  function ensureMermaidReady() {
    if (window.mermaid && typeof window.mermaid.render === "function") {
      if (!ixVisualMermaidState.initialized && typeof window.mermaid.initialize === "function") {
        window.mermaid.initialize({
          startOnLoad: false,
          securityLevel: "strict"
        });
        ixVisualMermaidState.initialized = true;
      }
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
        return !!(window.mermaid && typeof window.mermaid.render === "function");
      })
      .then(function(ok) {
        if (!ok || !window.mermaid || typeof window.mermaid.render !== "function") {
          ixVisualMermaidState.loadFailed = true;
          return false;
        }

        if (!ixVisualMermaidState.initialized && typeof window.mermaid.initialize === "function") {
          window.mermaid.initialize({
            startOnLoad: false,
            securityLevel: "strict"
          });
          ixVisualMermaidState.initialized = true;
        }

        return true;
      })
      .catch(function() {
        ixVisualMermaidState.loadFailed = true;
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

    var source = pre.getAttribute("data-ix-mermaid-source");
    if (typeof source === "string" && source.length > 0) {
      pre.textContent = source;
    }

    pre.setAttribute("data-ix-mermaid-rendered", "0");
    pre.setAttribute("data-ix-mermaid-invalid", "1");
    var suffix = reason ? " (" + reason + ")" : "";
    ensureVisualNotice(pre, "invalid visual block: mermaid" + suffix);
  }

  async function renderMermaidBlock(pre) {
    if (!pre || pre.getAttribute("data-ix-mermaid-pending") === "1") {
      return;
    }

    var source = pre.getAttribute("data-ix-mermaid-source");
    if (typeof source !== "string" || source.length === 0) {
      source = normalizeText(pre.textContent || "");
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
      if (typeof window.mermaid.parse === "function") {
        var parseResult = window.mermaid.parse(source);
        if (parseResult && typeof parseResult.then === "function") {
          await withVisualTimeout(parseResult, ixVisualMermaidState.renderTimeoutMs);
        }
      }

      var renderId = "ix-mermaid-" + String(ixVisualMermaidState.nextRenderId++);
      var renderResult = await withVisualTimeout(
        Promise.resolve(window.mermaid.render(renderId, source)),
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
      })
      .then(function(ok) {
        if (!ok || !window.Chart || typeof window.Chart !== "function") {
          ixVisualChartState.loadFailed = true;
          return false;
        }

        return true;
      })
      .catch(function() {
        ixVisualChartState.loadFailed = true;
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

    if (pre._ixChartInstance && typeof pre._ixChartInstance.destroy === "function") {
      try {
        pre._ixChartInstance.destroy();
      } catch (_) {
        // Ignore chart disposal errors.
      }
    }
    pre._ixChartInstance = null;
    pre.removeAttribute("data-ix-chart-rendered");
    pre.removeAttribute("data-ix-chart-pending");
    pre.style.removeProperty("display");

    var host = pre.nextElementSibling;
    if (host && host.classList && host.classList.contains("ix-chart-host")) {
      host.remove();
    }
  }

  function markChartInvalid(pre, reason) {
    if (!pre) {
      return;
    }

    disposeChartBlock(pre);
    pre.setAttribute("data-ix-chart-invalid", "1");
    var suffix = reason ? " (" + reason + ")" : "";
    ensureVisualNotice(pre, "invalid visual block: ix-chart" + suffix);
  }

  async function renderIxChartBlock(pre) {
    if (!pre || pre.getAttribute("data-ix-chart-pending") === "1") {
      return;
    }

    var code = pre.querySelector("code.language-ix-chart, code.language-chart");
    var source = pre.getAttribute("data-ix-chart-source");
    if ((!source || source.length === 0) && code) {
      source = normalizeText(code.textContent || "");
    }
    if (!source) {
      source = normalizeText(pre.textContent || "");
    }
    if (source) {
      pre.setAttribute("data-ix-chart-source", source);
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

    pre.setAttribute("data-ix-chart-pending", "1");
    try {
      disposeChartBlock(pre);

      var host = document.createElement("div");
      host.className = "ix-chart-host";
      var canvas = document.createElement("canvas");
      canvas.className = "ix-chart-canvas";
      host.appendChild(canvas);
      pre.insertAdjacentElement("afterend", host);

      var context = canvas.getContext("2d");
      if (!context) {
        throw new Error("canvas context unavailable");
      }

      var instance = new window.Chart(context, validation.config);
      pre._ixChartInstance = instance;
      pre.style.display = "none";
      pre.removeAttribute("data-ix-chart-invalid");
      pre.setAttribute("data-ix-chart-rendered", "1");
      clearVisualNotice(pre);
    } catch (_) {
      markChartInvalid(pre, "render failed");
    } finally {
      pre.removeAttribute("data-ix-chart-pending");
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
      loadVisualStylesheetOnce(ixVisualAssets.visNetworkCssUrl, "data-ix-visnetwork-css"),
      loadVisualScriptOnce(
        ixVisualAssets.visNetworkJsUrl,
        "data-ix-visnetwork-js",
        function() {
          return !!(window.vis && typeof window.vis.Network === "function");
        })
    ])
      .then(function(parts) {
        var cssOk = !!parts[0];
        var jsOk = !!parts[1];
        var ready = cssOk && jsOk && window.vis && typeof window.vis.Network === "function";
        if (!ready) {
          ixVisualNetworkState.loadFailed = true;
          return false;
        }
        return true;
      })
      .catch(function() {
        ixVisualNetworkState.loadFailed = true;
        return false;
      })
      .finally(function() {
        if (ixVisualNetworkState.loadFailed) {
          ixVisualNetworkState.loadPromise = null;
        }
      });

    return ixVisualNetworkState.loadPromise;
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

    if (pre._ixNetworkInstance && typeof pre._ixNetworkInstance.destroy === "function") {
      try {
        pre._ixNetworkInstance.destroy();
      } catch (_) {
        // Ignore network disposal errors.
      }
    }
    pre._ixNetworkInstance = null;
    pre.removeAttribute("data-ix-network-rendered");
    pre.removeAttribute("data-ix-network-pending");
    pre.style.removeProperty("display");

    var host = pre.nextElementSibling;
    if (host && host.classList && host.classList.contains("ix-network-host")) {
      host.remove();
    }
  }

  function markNetworkInvalid(pre, reason) {
    if (!pre) {
      return;
    }

    disposeNetworkBlock(pre);
    pre.setAttribute("data-ix-network-invalid", "1");
    var suffix = reason ? " (" + reason + ")" : "";
    ensureVisualNotice(pre, "invalid visual block: ix-network" + suffix);
  }

  async function renderIxNetworkBlock(pre) {
    if (!pre || pre.getAttribute("data-ix-network-pending") === "1") {
      return;
    }

    var code = pre.querySelector("code.language-ix-network");
    var source = pre.getAttribute("data-ix-network-source");
    if ((!source || source.length === 0) && code) {
      source = normalizeText(code.textContent || "");
    }
    if (!source) {
      source = normalizeText(pre.textContent || "");
    }
    if (source) {
      pre.setAttribute("data-ix-network-source", source);
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

    pre.setAttribute("data-ix-network-pending", "1");
    try {
      disposeNetworkBlock(pre);

      var host = document.createElement("div");
      host.className = "ix-network-host";
      var canvas = document.createElement("div");
      canvas.className = "ix-network-canvas";
      host.appendChild(canvas);
      pre.insertAdjacentElement("afterend", host);

      var network = new window.vis.Network(
        canvas,
        {
          nodes: validation.config.nodes,
          edges: validation.config.edges
        },
        validation.config.options || {});

      pre._ixNetworkInstance = network;
      pre.style.display = "none";
      pre.removeAttribute("data-ix-network-invalid");
      pre.setAttribute("data-ix-network-rendered", "1");
      clearVisualNotice(pre);
    } catch (_) {
      markNetworkInvalid(pre, "render failed");
    } finally {
      pre.removeAttribute("data-ix-network-pending");
    }
  }

  function collectIxNetworkBlocks(root) {
    var codeNodes = root.querySelectorAll(".bubble .markdown-body pre > code.language-ix-network");
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

      var marker = pre.dataset.ixNetworkBlockId;
      if (!marker) {
        marker = "ix-network-" + String(i + 1) + "-" + String(Math.floor(Math.random() * 1000000));
        pre.dataset.ixNetworkBlockId = marker;
      }

      if (seen[marker]) {
        continue;
      }
      seen[marker] = true;
      blocks.push(pre);
    }

    return blocks;
  }

  async function renderTranscriptNetworks(root) {
    var blocks = collectIxNetworkBlocks(root);
    if (blocks.length === 0) {
      return;
    }

    var maxBlocks = ixVisualNetworkState.maxBlocksPerMessage;
    for (var i = maxBlocks; i < blocks.length; i++) {
      markNetworkInvalid(blocks[i], "too many networks");
    }

    var ready = await ensureNetworkReady();
    if (!ready) {
      for (var j = 0; j < Math.min(blocks.length, maxBlocks); j++) {
        markNetworkInvalid(blocks[j], "renderer unavailable");
      }
      return;
    }

    for (var k = 0; k < Math.min(blocks.length, maxBlocks); k++) {
      await renderIxNetworkBlock(blocks[k]);
    }
  }

  async function renderTranscriptMermaid(root) {
    var blocks = root.querySelectorAll(".bubble .markdown-body pre.mermaid");
    if (!blocks || blocks.length === 0) {
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
    var codeNodes = root.querySelectorAll(".bubble .markdown-body pre > code.language-ix-chart, .bubble .markdown-body pre > code.language-chart");
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

      var marker = pre.dataset.ixChartBlockId;
      if (!marker) {
        marker = "ix-chart-" + String(i + 1) + "-" + String(Math.floor(Math.random() * 1000000));
        pre.dataset.ixChartBlockId = marker;
      }

      if (seen[marker]) {
        continue;
      }
      seen[marker] = true;
      blocks.push(pre);
    }

    return blocks;
  }

  async function renderTranscriptCharts(root) {
    var blocks = collectIxChartBlocks(root);
    if (blocks.length === 0) {
      return;
    }

    var maxBlocks = ixVisualChartState.maxBlocksPerMessage;
    for (var i = maxBlocks; i < blocks.length; i++) {
      markChartInvalid(blocks[i], "too many charts");
    }

    var ready = await ensureChartReady();
    if (!ready) {
      for (var j = 0; j < Math.min(blocks.length, maxBlocks); j++) {
        markChartInvalid(blocks[j], "renderer unavailable");
      }
      return;
    }

    for (var k = 0; k < Math.min(blocks.length, maxBlocks); k++) {
      await renderIxChartBlock(blocks[k]);
    }
  }

  function disposeTranscriptVisuals(root) {
    if (!root || !root.querySelectorAll) {
      return;
    }

    var chartBlocks = root.querySelectorAll(".bubble .markdown-body pre[data-ix-chart-rendered='1']");
    for (var i = 0; i < chartBlocks.length; i++) {
      disposeChartBlock(chartBlocks[i]);
    }

    var networkBlocks = root.querySelectorAll(".bubble .markdown-body pre[data-ix-network-rendered='1']");
    for (var j = 0; j < networkBlocks.length; j++) {
      disposeNetworkBlock(networkBlocks[j]);
    }
  }

  window.ixDisposeTranscriptVisuals = function(root) {
    disposeTranscriptVisuals(root);
  };

  window.ixRenderTranscriptVisuals = function(root) {
    if (!root || !root.querySelectorAll) {
      return;
    }

    renderTranscriptCharts(root)
      .then(function() {
        return renderTranscriptNetworks(root);
      })
      .then(function() {
        return renderTranscriptMermaid(root);
      })
      .catch(function() {
        // Keep transcript rendering resilient even when visual runtime fails.
      });
  };
