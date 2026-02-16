  function truncateJsonValue(value, maxLen) {
    var text = String(value || "");
    if (text.length <= maxLen) {
      return text;
    }
    return text.slice(0, maxLen - 3) + "...";
  }

  function buildToolParameterCard(parameter) {
    var card = document.createElement("div");
    card.className = "options-tool-param";

    var title = document.createElement("div");
    title.className = "options-tool-param-title";

    var name = document.createElement("span");
    name.className = "options-tool-param-name";
    name.textContent = parameter.name || "parameter";
    title.appendChild(name);

    var type = document.createElement("span");
    type.className = "options-pill options-pill-category";
    type.textContent = parameter.type || "any";
    title.appendChild(type);

    if (parameter.required === true) {
      var required = document.createElement("span");
      required.className = "options-pill options-pill-required";
      required.textContent = "required";
      title.appendChild(required);
    }

    card.appendChild(title);

    if (parameter.description) {
      var desc = document.createElement("div");
      desc.className = "options-tool-param-desc";
      desc.textContent = parameter.description;
      card.appendChild(desc);
    }

    var metaBits = [];
    if (Array.isArray(parameter.enumValues) && parameter.enumValues.length > 0) {
      metaBits.push("enum: " + parameter.enumValues.join(", "));
    }
    if (parameter.defaultJson) {
      metaBits.push("default: " + truncateJsonValue(parameter.defaultJson, 120));
    }
    if (parameter.exampleJson) {
      metaBits.push("example: " + truncateJsonValue(parameter.exampleJson, 120));
    }

    if (metaBits.length > 0) {
      var meta = document.createElement("div");
      meta.className = "options-tool-param-meta";
      meta.textContent = metaBits.join(" · ");
      card.appendChild(meta);
    }

    return card;
  }

  function createToolCard(tool) {
    var item = document.createElement("div");
    item.className = "options-item";

    var header = document.createElement("div");
    header.className = "options-item-header";

    var left = document.createElement("div");
    left.className = "options-item-title";
    left.textContent = tool.displayName || tool.name;
    header.appendChild(left);

    if (tool.category) {
      var category = document.createElement("span");
      category.className = "options-pill options-pill-category";
      category.textContent = tool.category;
      header.appendChild(category);
    }

    var toggle = document.createElement("input");
    toggle.className = "options-toggle";
    toggle.type = "checkbox";
    toggle.checked = normalizeBool(tool.enabled);
    toggle.setAttribute("aria-label", "Enable " + (tool.displayName || tool.name));
    toggle.dataset.toolName = tool.name;
    toggle.addEventListener("change", function(e) {
      var target = e.target;
      post("set_tool_enabled", { name: target.dataset.toolName, enabled: target.checked });
    });

    header.appendChild(toggle);
    item.appendChild(header);

    var machine = document.createElement("div");
    machine.className = "options-item-code";
    machine.textContent = tool.packName
      ? (tool.name + " · " + tool.packName)
      : tool.name;
    item.appendChild(machine);

    if (tool.description) {
      var sub = document.createElement("div");
      sub.className = "options-item-sub";
      sub.textContent = tool.description;
      item.appendChild(sub);
    }

    if (tool.tags && tool.tags.length > 0) {
      var tagsRow = document.createElement("div");
      tagsRow.className = "options-tag-row";
      for (var t = 0; t < tool.tags.length; t++) {
        var tag = document.createElement("span");
        tag.className = "options-tag";
        tag.textContent = tool.tags[t];
        tagsRow.appendChild(tag);
      }
      item.appendChild(tagsRow);
    }

    if (tool.routingConfidence || tool.routingReason || typeof tool.routingScore === "number") {
      var routing = document.createElement("div");
      routing.className = "options-tool-routing";

      if (tool.routingConfidence) {
        var confidence = document.createElement("span");
        var normalized = String(tool.routingConfidence || "").toLowerCase();
        var level = normalized === "high" || normalized === "low" ? normalized : "medium";
        confidence.className = "options-pill options-pill-routing options-pill-routing-" + level;
        confidence.textContent = "Routing " + level;
        routing.appendChild(confidence);
      }

      if (typeof tool.routingScore === "number" && isFinite(tool.routingScore)) {
        var score = document.createElement("span");
        score.className = "options-pill options-pill-category";
        score.textContent = "Score " + Number(tool.routingScore).toFixed(2);
        routing.appendChild(score);
      }

      if (tool.routingReason) {
        var reason = document.createElement("span");
        reason.className = "options-tool-routing-reason";
        reason.textContent = String(tool.routingReason);
        routing.appendChild(reason);
      }

      item.appendChild(routing);
    }

    if (Array.isArray(tool.parameters) && tool.parameters.length > 0) {
      var parametersDetails = document.createElement("details");
      parametersDetails.className = "options-tool-params";

      var summary = document.createElement("summary");
      summary.textContent = "Parameters (" + String(tool.parameters.length) + ")";
      parametersDetails.appendChild(summary);

      var body = document.createElement("div");
      body.className = "options-tool-params-body";
      for (var p = 0; p < tool.parameters.length; p++) {
        body.appendChild(buildToolParameterCard(tool.parameters[p] || {}));
      }
      parametersDetails.appendChild(body);
      item.appendChild(parametersDetails);
    }

    return item;
  }

  function setPackEnabled(packId, groupTools, enabled) {
    var changed = false;
    for (var i = 0; i < groupTools.length; i++) {
      var tool = groupTools[i];
      if (normalizeBool(tool.enabled) === enabled) {
        continue;
      }

      tool.enabled = enabled;
      changed = true;
    }

    if (!changed) {
      return;
    }

    if (packId) {
      post("set_pack_enabled", { packId: packId, enabled: enabled });
      return;
    }

    for (var j = 0; j < groupTools.length; j++) {
      post("set_tool_enabled", { name: groupTools[j].name, enabled: enabled });
    }
  }

  function normalizeToolFilter(value) {
    return (value || "").trim().toLowerCase();
  }

  function toolMatchesFilter(tool, filter) {
    if (!filter) {
      return true;
    }

    var haystack = [
      tool.displayName || "",
      tool.name || "",
      tool.description || "",
      tool.category || "",
      tool.packId || "",
      tool.packName || "",
      tool.routingConfidence || "",
      tool.routingReason || "",
      typeof tool.routingScore === "number" ? String(tool.routingScore) : "",
      packSourceLabel(packSourceKind(inferPackIdFromTool(tool))),
      (tool.tags || []).join(" "),
      Array.isArray(tool.parameters)
        ? tool.parameters.map(function(p) { return (p && p.name ? p.name : "") + " " + (p && p.description ? p.description : ""); }).join(" ")
        : ""
    ].join(" ").toLowerCase();

    return haystack.indexOf(filter) >= 0;
  }

  function renderTools() {
    var toolsEl = byId("toolsList");
    toolsEl.innerHTML = "";

    var tools = state.options.tools || [];
    if (tools.length === 0) {
      toolsEl.innerHTML = "<div class='options-item'><div class='options-item-title'>No tools registered</div></div>";
      return;
    }

    var filter = normalizeToolFilter(state.options.toolFilter);
    if (filter) {
      tools = tools.filter(function(tool) { return toolMatchesFilter(tool, filter); });
      if (tools.length === 0) {
        toolsEl.innerHTML = "<div class='options-item'><div class='options-item-title'>No tools match filter</div></div>";
        return;
      }
    }

    var groups = {};
    for (var i = 0; i < tools.length; i++) {
      var tool = tools[i];
      var packId = inferPackIdFromTool(tool);
      if (!groups[packId]) {
        groups[packId] = [];
      }
      groups[packId].push(tool);
    }

    var order = Object.keys(groups);
    order.sort(function(a, b) {
      var aUncategorized = a === "uncategorized";
      var bUncategorized = b === "uncategorized";
      if (aUncategorized !== bUncategorized) {
        return aUncategorized ? 1 : -1;
      }

      var byName = packDisplayName(a).localeCompare(packDisplayName(b), undefined, { sensitivity: "base" });
      if (byName !== 0) {
        return byName;
      }

      return a.localeCompare(b, undefined, { sensitivity: "base" });
    });

    for (var g = 0; g < order.length; g++) {
      var currentPackId = order[g];
      var groupTools = groups[currentPackId] || [];
      groupTools.sort(function(a, b) {
        return String(a.displayName || a.name).localeCompare(String(b.displayName || b.name));
      });

      var details = document.createElement("details");
      details.className = "options-accordion";
      details.open = ensureAccordionState(currentPackId);
      details.dataset.packId = currentPackId;
      details.addEventListener("toggle", function(e) {
        var pid = e.currentTarget.dataset.packId;
        state.expandedToolPacks[pid] = e.currentTarget.open === true;
      });

      var summary = document.createElement("summary");
      summary.className = "options-accordion-summary";

      var heading = document.createElement("div");
      heading.className = "options-accordion-heading";

      var title = document.createElement("span");
      title.className = "options-accordion-title";
      title.textContent = packDisplayName(currentPackId);
      heading.appendChild(title);

      var packDesc = packDescription(currentPackId);
      if (packDesc) {
        var subtitle = document.createElement("span");
        subtitle.className = "options-accordion-subtitle";
        subtitle.textContent = packDesc;
        heading.appendChild(subtitle);
      }
      summary.appendChild(heading);

      var summaryRight = document.createElement("div");
      summaryRight.className = "options-accordion-summary-right";

      var sourceKind = packSourceKind(currentPackId);
      var sourceBadge = document.createElement("span");
      sourceBadge.className = "options-pill options-pill-source options-pill-source-" + sourceKind;
      sourceBadge.textContent = packSourceLabel(sourceKind);
      sourceBadge.title = packSourceHint(sourceKind);
      summaryRight.appendChild(sourceBadge);

      var meta = document.createElement("span");
      meta.className = "options-accordion-meta";
      meta.textContent = String(groupTools.length) + (groupTools.length === 1 ? " tool" : " tools");
      summaryRight.appendChild(meta);

      var enabledCount = 0;
      for (var c = 0; c < groupTools.length; c++) {
        if (normalizeBool(groupTools[c].enabled)) {
          enabledCount++;
        }
      }
      var allEnabled = enabledCount === groupTools.length;
      var someEnabled = enabledCount > 0;

      var pill = document.createElement("span");
      pill.className = "options-pill" + (allEnabled ? "" : " off");
      pill.textContent = allEnabled ? "Loaded" : (someEnabled ? "Partial" : "Disabled");
      summaryRight.appendChild(pill);

      var packToggle = document.createElement("input");
      packToggle.className = "options-toggle options-toggle-pack";
      packToggle.type = "checkbox";
      packToggle.checked = allEnabled;
      packToggle.indeterminate = !allEnabled && someEnabled;
      packToggle.setAttribute("aria-label", "Enable pack " + packDisplayName(currentPackId));
      packToggle.addEventListener("click", function(e) {
        e.preventDefault();
        e.stopPropagation();
      });
      (function(packIdForToggle, groupToolsForToggle) {
        packToggle.addEventListener("change", function(e) {
          var enabled = e.target.checked === true;
          setPackEnabled(packIdForToggle, groupToolsForToggle, enabled);
          renderTools();
        });
      })(currentPackId, groupTools);
      summaryRight.appendChild(packToggle);
      summary.appendChild(summaryRight);

      details.appendChild(summary);

      var body = document.createElement("div");
      body.className = "options-accordion-body";
      for (var t = 0; t < groupTools.length; t++) {
        body.appendChild(createToolCard(groupTools[t]));
      }
      details.appendChild(body);
      toolsEl.appendChild(details);
    }
  }

  function renderProfileSelector() {
    var select = byId("optProfileSelect");
    var names = state.options.profileNames || [];
    var active = state.options.activeProfileName || "default";

    select.innerHTML = "";
    if (names.length === 0) {
      names = ["default"];
    }

    for (var i = 0; i < names.length; i++) {
      var name = names[i];
      var option = document.createElement("option");
      option.value = name;
      option.textContent = name;
      select.appendChild(option);
    }

    select.value = active;
    if (select.value !== active && names.length > 0) {
      select.value = names[0];
    }

    ensureCustomSelect("optProfileSelect");
  }

  function renderProfileScopeHint() {
    var hint = byId("optProfileScopeHint");
    if (!hint) {
      return;
    }

    var mode = normalizeProfileApplyMode(state.options.profileApplyMode);
    var profile = state.options.profile || {};
    var overrides = profile.sessionOverrides || {};
    var overrideParts = [];

    if (overrides.userName) {
      overrideParts.push("name");
    }
    if (overrides.persona) {
      overrideParts.push("persona");
    }
    if (overrides.theme) {
      overrideParts.push("theme");
    }

    var baseText = mode === "profile"
      ? "Changes are saved to your default profile."
      : "Changes apply to this session only.";

    if (overrideParts.length > 0) {
      baseText += " Active session overrides: " + overrideParts.join(", ") + ".";
    }

    hint.textContent = baseText;
  }

  function normalizeLocalTransport(value) {
    return String(value || "").toLowerCase() === "compatible-http" ? "compatible-http" : "native";
  }

  function normalizeModelText(value) {
    return String(value == null ? "" : value).trim();
  }

  function runtimeAdvancedStorageKey() {
    return "ixchat.runtime.advanced";
  }

  function runtimeModelFilterStorageKey() {
    return "ixchat.runtime.model.filter";
  }

  function isRuntimeAdvancedOpen() {
    return readStorage(runtimeAdvancedStorageKey()) === "1";
  }

  function setRuntimeAdvancedOpen(open) {
    var next = open === true;
    var advancedArea = byId("optLocalAdvancedArea");
    if (advancedArea) {
      advancedArea.hidden = !next;
    }

    var toggle = byId("btnToggleLocalAdvancedRuntime");
    if (toggle) {
      toggle.textContent = next ? "Hide Advanced Runtime" : "Show Advanced Runtime";
    }

    writeStorage(runtimeAdvancedStorageKey(), next ? "1" : "0");
  }

  function isLmStudioBaseUrl(value) {
    var normalized = String(value || "").trim().toLowerCase();
    if (!normalized) {
      return false;
    }
    return normalized.indexOf("127.0.0.1:1234") >= 0 || normalized.indexOf("localhost:1234") >= 0;
  }

  function looksCloudHostedModelName(value) {
    var normalized = normalizeModelText(value).toLowerCase();
    if (!normalized) {
      return false;
    }

    return normalized.indexOf("gpt-") === 0
      || normalized === "gpt5"
      || normalized.indexOf("chatgpt") === 0
      || normalized.indexOf("o1") === 0
      || normalized.indexOf("o3") === 0
      || normalized.indexOf("o4") === 0;
  }

  function countCloudHostedModelNames(models) {
    if (!Array.isArray(models) || models.length === 0) {
      return 0;
    }

    var count = 0;
    for (var i = 0; i < models.length; i++) {
      var item = models[i] || {};
      var modelName = normalizeModelText(item.model || item.Model || item.id || item.Id);
      if (looksCloudHostedModelName(modelName)) {
        count++;
      }
    }
    return count;
  }

  function isOllamaBaseUrl(value) {
    var normalized = String(value || "").trim().toLowerCase();
    if (!normalized) {
      return false;
    }
    return normalized.indexOf("127.0.0.1:11434") >= 0 || normalized.indexOf("localhost:11434") >= 0;
  }

  function normalizeModelFilter(value) {
    return String(value || "").trim().toLowerCase();
  }

  function renderLocalModelOptions() {
    var local = state.options.localModel || {};
    var transport = normalizeLocalTransport(local.transport);
    var isCompatible = transport === "compatible-http";
    var baseUrl = String(local.baseUrl || "");
    var modelsEndpoint = normalizeModelText(local.modelsEndpoint || "");
    var model = normalizeModelText(local.model || "");
    var models = Array.isArray(local.models) ? local.models : [];
    var favorites = toStringArray(local.favoriteModels);
    var recents = toStringArray(local.recentModels);
    var warning = normalizeModelText(local.warning || "");
    var isStale = local.isStale === true;
    var profileSaved = local.profileSaved === true;
    var profileApplyMode = normalizeProfileApplyMode(state.options.profileApplyMode || "session");
    var runtimeDetection = local.runtimeDetection || {};
    var runtimeDetectionHasRun = runtimeDetection.hasRun === true;
    var lmStudioAvailable = runtimeDetection.lmStudioAvailable === true;
    var ollamaAvailable = runtimeDetection.ollamaAvailable === true;
    var runtimeDetectedName = normalizeModelText(runtimeDetection.detectedName || "");
    var runtimeDetectionWarning = normalizeModelText(runtimeDetection.warning || "");
    var lmStudioConnected = isCompatible && isLmStudioBaseUrl(baseUrl);
    var runtimeSummary = byId("optRuntimeSummary");
    if (runtimeSummary) {
      if (isCompatible) {
        var endpoint = baseUrl ? baseUrl : "(base URL not set)";
        runtimeSummary.textContent = "Current: Local runtime (Compatible HTTP) via " + endpoint + ".";
      } else {
        runtimeSummary.textContent = "Current: ChatGPT runtime (OpenAI native).";
      }
    }

    var runtimeAuthHint = byId("optRuntimeAuthHint");
    if (runtimeAuthHint) {
      if (isCompatible) {
        runtimeAuthHint.textContent = "You can stay signed in to ChatGPT while running local models.";
      } else {
        runtimeAuthHint.textContent = "ChatGPT sign-in and runtime provider are separate. You can switch to LM Studio any time.";
      }
    }

    var ollamaConnected = isCompatible && isOllamaBaseUrl(baseUrl);
    var runtimeBadge = byId("optLocalRuntimeBadge");
    if (runtimeBadge) {
      var runtimeName = "ChatGPT Native";
      if (lmStudioConnected) {
        runtimeName = "LM Studio";
      } else if (ollamaConnected) {
        runtimeName = "Ollama";
      } else if (isCompatible) {
        runtimeName = "Compatible HTTP";
      }
      var activeModel = model ? model : "(auto)";
      runtimeBadge.textContent = "Active runtime: " + runtimeName + " | Active model: " + activeModel;
    }

    var simpleHint = byId("optLocalSimpleHint");
    if (simpleHint) {
      if (!isCompatible) {
        simpleHint.textContent = "ChatGPT runtime is active. Switch to LM Studio runtime to use local models.";
      } else if (lmStudioConnected) {
        simpleHint.textContent = "LM Studio runtime is active for this profile.";
      } else if (runtimeDetectionHasRun && !lmStudioAvailable) {
        simpleHint.textContent = "LM Studio not detected on http://127.0.0.1:1234/v1. Start LM Studio and reconnect, or configure Advanced Runtime.";
      } else {
        simpleHint.textContent = "Local runtime is active. Use LM Studio Runtime for the default LM Studio endpoint.";
      }
    }

    var useOpenAiRuntimeButton = byId("btnUseOpenAiRuntime");
    if (useOpenAiRuntimeButton) {
      useOpenAiRuntimeButton.textContent = isCompatible ? "Use ChatGPT Runtime" : "ChatGPT Runtime Active";
      useOpenAiRuntimeButton.classList.toggle("options-btn-active", !isCompatible);
    }

    var connectLmStudioButton = byId("btnConnectLmStudio");
    if (connectLmStudioButton) {
      connectLmStudioButton.textContent = lmStudioConnected ? "LM Studio Runtime Active" : "Use LM Studio Runtime";
      connectLmStudioButton.classList.toggle("options-btn-active", lmStudioConnected);
      connectLmStudioButton.title = runtimeDetectionHasRun && !lmStudioAvailable && !lmStudioConnected
        ? "LM Studio was not detected. Start LM Studio and click Auto Detect Runtime in Advanced Runtime."
        : "";
    }

    var refreshModelsButton = byId("btnRefreshModels");
    if (refreshModelsButton) {
      refreshModelsButton.disabled = !isCompatible;
      refreshModelsButton.title = isCompatible ? "" : "Switch to local runtime to refresh local models.";
    }

    var advancedShouldBeOpen = isRuntimeAdvancedOpen();
    if (!isCompatible && !advancedShouldBeOpen) {
      setRuntimeAdvancedOpen(false);
    } else {
      setRuntimeAdvancedOpen(advancedShouldBeOpen);
    }

    var transportSelect = byId("optLocalTransport");
    if (transportSelect) {
      transportSelect.value = transport;
      syncCustomSelect(transportSelect);
    }

    var btnAutoDetect = byId("btnAutoDetectLocalRuntime");
    if (btnAutoDetect) {
      btnAutoDetect.hidden = !isCompatible;
    }

    var btnPresetLmStudio = byId("btnLocalPresetLmStudio");
    if (btnPresetLmStudio) {
      btnPresetLmStudio.hidden = runtimeDetectionHasRun && !lmStudioAvailable;
    }

    var btnPresetOllama = byId("btnLocalPresetOllama");
    if (btnPresetOllama) {
      btnPresetOllama.hidden = runtimeDetectionHasRun && !ollamaAvailable;
    }

    var baseUrlRow = byId("optLocalBaseUrlRow");
    if (baseUrlRow) {
      baseUrlRow.hidden = !isCompatible;
    }

    var baseUrlInput = byId("optLocalBaseUrl");
    if (baseUrlInput) {
      baseUrlInput.value = baseUrl;
      baseUrlInput.disabled = !isCompatible;
    }

    var apiKeyRow = byId("optLocalApiKeyRow");
    if (apiKeyRow) {
      apiKeyRow.hidden = !isCompatible;
    }

    var apiKeyHint = byId("optLocalApiKeyHint");
    if (apiKeyHint) {
      apiKeyHint.hidden = !isCompatible;
    }

    var apiKeyInput = byId("optLocalApiKey");
    if (apiKeyInput) {
      apiKeyInput.disabled = !isCompatible;
      if (!isCompatible) {
        apiKeyInput.value = "";
      }
    }

    var modelInput = byId("optLocalModelInput");
    if (modelInput) {
      modelInput.value = model;
    }

    var modelFilterInput = byId("optLocalModelFilter");
    var storedModelFilter = String(readStorage(runtimeModelFilterStorageKey()) || "");
    if (modelFilterInput && modelFilterInput.value.length === 0 && storedModelFilter.length > 0) {
      modelFilterInput.value = storedModelFilter;
    }
    var modelFilterQuery = normalizeModelFilter(modelFilterInput ? modelFilterInput.value : storedModelFilter);

    var modelSelect = byId("optLocalModelSelect");
    var selectableOptionCount = 0;
    if (modelSelect) {
      modelSelect.innerHTML = "";

      var manualOption = document.createElement("option");
      manualOption.value = "";
      manualOption.textContent = "Manual model input";
      modelSelect.appendChild(manualOption);

      var seen = {};
      function pushModelOption(modelName, labelPrefix, matchLabel) {
        var normalized = normalizeModelText(modelName);
        if (!normalized) {
          return;
        }
        var normalizedMatchLabel = normalizeModelText(matchLabel || labelPrefix || "");
        if (modelFilterQuery.length > 0
            && normalized.toLowerCase().indexOf(modelFilterQuery) < 0
            && normalizedMatchLabel.toLowerCase().indexOf(modelFilterQuery) < 0) {
          return;
        }
        var key = normalized.toLowerCase();
        if (seen[key]) {
          return;
        }
        seen[key] = true;
        var option = document.createElement("option");
        option.value = normalized;
        option.textContent = labelPrefix ? (labelPrefix + " " + normalized) : normalized;
        modelSelect.appendChild(option);
        selectableOptionCount++;
      }

      for (var r = 0; r < recents.length; r++) {
        pushModelOption(recents[r], "Recent:", recents[r]);
      }
      for (var f = 0; f < favorites.length; f++) {
        pushModelOption(favorites[f], "Favorite:", favorites[f]);
      }
      for (var i = 0; i < models.length; i++) {
        var item = models[i] || {};
        var modelName = normalizeModelText(item.model || item.Model || item.id || item.Id);
        if (!modelName) {
          continue;
        }
        var displayName = normalizeModelText(item.displayName || item.DisplayName);
        if (displayName && displayName.toLowerCase() !== modelName.toLowerCase()) {
          pushModelOption(modelName, displayName + ":", displayName);
        } else {
          pushModelOption(modelName, "", modelName);
        }
      }

      modelSelect.value = model;
      if (modelSelect.value !== model) {
        modelSelect.value = "";
      }
      syncCustomSelect(modelSelect);
    }

    var modelInputRow = byId("optLocalModelInputRow");
    var modelSelectRow = byId("optLocalModelSelectRow");
    var modelFilterRow = byId("optLocalModelFilterRow");
    var hasSelectableModels = selectableOptionCount > 0;
    var usingManualInput = !modelSelect || !hasSelectableModels || modelSelect.value === "";
    var showModelSelect = isCompatible && (hasSelectableModels || modelFilterQuery.length > 0);
    if (modelSelectRow) {
      modelSelectRow.hidden = !showModelSelect;
    }
    if (modelFilterRow) {
      modelFilterRow.hidden = !showModelSelect;
    }
    if (modelInputRow) {
      modelInputRow.hidden = !isCompatible || (showModelSelect && !usingManualInput);
    }
    if (modelInput) {
      modelInput.disabled = !isCompatible || (showModelSelect && !usingManualInput);
    }

    var stateNote = byId("optLocalModelsState");
    if (stateNote) {
      var parts = [];
      if (!isCompatible) {
        parts.push("ChatGPT runtime active");
        if (models.length > 0) {
          parts.push(String(models.length) + " local models cached");
        }
      } else {
        if (modelsEndpoint) {
          parts.push("model source: " + modelsEndpoint);
        }
        if (runtimeDetectedName) {
          parts.push("runtime probe: " + runtimeDetectedName + " reachable");
        } else if (runtimeDetectionHasRun && runtimeDetectionWarning) {
          parts.push(runtimeDetectionWarning);
        }
        if (models.length > 0) {
          parts.push(String(models.length) + " models returned");
          var cloudHostedCount = countCloudHostedModelNames(models);
          if (cloudHostedCount > 0 && cloudHostedCount >= Math.ceil(models.length * 0.6)) {
            parts.push("catalog looks cloud-hosted; load a local model in LM Studio to see local IDs");
          }
        } else {
          parts.push("No discovered models yet");
          if (lmStudioConnected) {
            parts.push("Load a model in LM Studio, then click Refresh Models");
          }
        }
      }
      if (profileApplyMode === "session" || !profileSaved) {
        parts.push("scope: current session only");
      } else {
        parts.push("scope: saved as default profile");
      }
      if (isStale) {
        parts.push("showing cached results");
      }
      if (warning) {
        parts.push(warning);
      } else if (transport === "compatible-http" && !baseUrl) {
        parts.push("Set a base URL, then refresh models.");
      }
      stateNote.textContent = parts.join(" | ");
    }
  }

  function normalizeExportSaveMode(value) {
    var normalized = String(value || "").toLowerCase();
    return normalized === "remember" ? "remember" : "ask";
  }

  function normalizeExportFormat(value) {
    var normalized = String(value || "").toLowerCase();
    if (normalized === "excel") return "xlsx";
    if (normalized === "word") return "docx";
    if (normalized === "csv" || normalized === "xlsx" || normalized === "docx") return normalized;
    return "xlsx";
  }

  function exportFormatDisplayName(format) {
    var normalized = normalizeExportFormat(format);
    if (normalized === "docx") return "Word";
    if (normalized === "csv") return "CSV";
    return "Excel";
  }

  function renderExportPreferences() {
    var exportPrefs = state.options.export || {};
    var saveMode = normalizeExportSaveMode(exportPrefs.saveMode);
    var format = normalizeExportFormat(exportPrefs.defaultFormat);
    var lastDirectory = String(exportPrefs.lastDirectory || "");

    var saveModeSelect = byId("optExportSaveMode");
    if (saveModeSelect) {
      saveModeSelect.value = saveMode;
      syncCustomSelect(saveModeSelect);
    }

    var formatSelect = byId("optExportDefaultFormat");
    if (formatSelect) {
      formatSelect.value = format;
      syncCustomSelect(formatSelect);
    }

    var lastDirectoryInput = byId("optExportLastDirectory");
    if (lastDirectoryInput) {
      lastDirectoryInput.value = lastDirectory || "(not set)";
      lastDirectoryInput.title = lastDirectory;
    }

    var clearButton = byId("btnClearExportLastDirectory");
    if (clearButton) {
      clearButton.disabled = lastDirectory.length === 0;
    }

    var quickExportButton = byId("btnDataViewQuickExport");
    if (quickExportButton) {
      var label = exportFormatDisplayName(format);
      quickExportButton.textContent = "Quick " + label;
      quickExportButton.title = "Quick export using " + label + " and current save behavior";
    }
  }

  function renderAutonomy() {
    var autonomy = state.options.autonomy || {};
    var maxRoundsInput = byId("optAutonomyMaxRounds");
    var parallelSelect = byId("optAutonomyParallel");
    var planReviewSelect = byId("optAutonomyPlanReview");
    var maxReviewPassesInput = byId("optAutonomyMaxReviewPasses");
    var modelHeartbeatInput = byId("optAutonomyModelHeartbeat");
    var proactiveModeToggle = byId("optProactiveMode");
    var queueAutoDispatchToggle = byId("optQueueAutoDispatch");
    var turnTimeoutInput = byId("optAutonomyTurnTimeout");
    var toolTimeoutInput = byId("optAutonomyToolTimeout");
    var weightedRoutingSelect = byId("optAutonomyWeightedRouting");
    var maxCandidatesInput = byId("optAutonomyMaxCandidates");
    var runNextQueuedButton = byId("btnRunNextQueuedTurn");
    var clearQueuedButton = byId("btnClearQueuedTurns");
    var queueStateLabel = byId("optQueueDispatchState");

    if (maxRoundsInput) {
      maxRoundsInput.value = autonomy.maxToolRounds == null ? "" : String(autonomy.maxToolRounds);
    }
    if (turnTimeoutInput) {
      turnTimeoutInput.value = autonomy.turnTimeoutSeconds == null ? "" : String(autonomy.turnTimeoutSeconds);
    }
    if (toolTimeoutInput) {
      toolTimeoutInput.value = autonomy.toolTimeoutSeconds == null ? "" : String(autonomy.toolTimeoutSeconds);
    }
    if (parallelSelect) {
      var parallelMode = String(autonomy.parallelToolMode || "").toLowerCase();
      if (parallelMode === "allow_parallel" || parallelMode === "allow-parallel" || parallelMode === "allowparallel" || parallelMode === "on") {
        parallelSelect.value = "allow_parallel";
      } else if (parallelMode === "force_serial" || parallelMode === "force-serial" || parallelMode === "forceserial" || parallelMode === "off") {
        parallelSelect.value = "force_serial";
      } else if (autonomy.parallelTools === true) {
        parallelSelect.value = "allow_parallel";
      } else if (autonomy.parallelTools === false) {
        parallelSelect.value = "force_serial";
      } else {
        parallelSelect.value = "auto";
      }
      syncCustomSelect(parallelSelect);
    }
    if (planReviewSelect) {
      if (autonomy.planExecuteReviewLoop === true) {
        planReviewSelect.value = "on";
      } else if (autonomy.planExecuteReviewLoop === false) {
        planReviewSelect.value = "off";
      } else {
        planReviewSelect.value = "default";
      }
      syncCustomSelect(planReviewSelect);
    }
    if (maxReviewPassesInput) {
      maxReviewPassesInput.value = autonomy.maxReviewPasses == null ? "" : String(autonomy.maxReviewPasses);
    }
    if (modelHeartbeatInput) {
      modelHeartbeatInput.value = autonomy.modelHeartbeatSeconds == null ? "" : String(autonomy.modelHeartbeatSeconds);
    }
    if (proactiveModeToggle) {
      proactiveModeToggle.checked = autonomy.proactiveMode !== false;
    }
    if (queueAutoDispatchToggle) {
      queueAutoDispatchToggle.checked = autonomy.queueAutoDispatch !== false;
    }
    if (weightedRoutingSelect) {
      if (autonomy.weightedToolRouting === true) {
        weightedRoutingSelect.value = "on";
      } else if (autonomy.weightedToolRouting === false) {
        weightedRoutingSelect.value = "off";
      } else {
        weightedRoutingSelect.value = "default";
      }
      syncCustomSelect(weightedRoutingSelect);
    }
    if (maxCandidatesInput) {
      maxCandidatesInput.value = autonomy.maxCandidateTools == null ? "" : String(autonomy.maxCandidateTools);
    }

    var queuedTurns = Number(state.queuedTurnCount);
    if (!Number.isFinite(queuedTurns) || queuedTurns < 0) {
      queuedTurns = 0;
    } else {
      queuedTurns = Math.floor(queuedTurns);
    }
    var queuedSignIn = Number(state.queuedPromptCount);
    if (!Number.isFinite(queuedSignIn) || queuedSignIn < 0) {
      queuedSignIn = 0;
    } else {
      queuedSignIn = Math.floor(queuedSignIn);
    }
    var queuedTotal = queuedTurns + queuedSignIn;
    var autoDispatchEnabled = autonomy.queueAutoDispatch !== false;

    if (runNextQueuedButton) {
      runNextQueuedButton.disabled = normalizeBool(state.sending) || queuedTotal <= 0;
      runNextQueuedButton.textContent = queuedTotal > 0
        ? ("Run Next Queued (" + queuedTotal + ")")
        : "Run Next Queued";
    }

    if (clearQueuedButton) {
      clearQueuedButton.disabled = queuedTotal <= 0;
      clearQueuedButton.textContent = queuedTotal > 0
        ? ("Clear Queues (" + queuedTotal + ")")
        : "Clear Queues";
    }

    if (queueStateLabel) {
      if (queuedTotal <= 0) {
        queueStateLabel.textContent = "Queue empty.";
      } else if (autoDispatchEnabled) {
        queueStateLabel.textContent = "Queued: " + queuedTurns + " turn(s), " + queuedSignIn + " sign-in item(s). Auto-dispatch is enabled.";
      } else {
        queueStateLabel.textContent = "Queued: " + queuedTurns + " turn(s), " + queuedSignIn + " sign-in item(s). Queue is paused.";
      }
    }
  }

  function renderMemory() {
    var memory = state.options.memory || {};
    var enabled = memory.enabled !== false;
    var facts = Array.isArray(memory.facts) ? memory.facts : [];

    var toggle = byId("optMemoryEnabled");
    if (toggle) {
      toggle.checked = enabled;
    }

    var addButton = byId("btnAddMemoryNote");
    if (addButton) {
      addButton.disabled = !enabled;
    }

    var noteInput = byId("optMemoryNote");
    if (noteInput) {
      noteInput.disabled = !enabled;
    }

    var list = byId("memoryFactsList");
    if (!list) {
      return;
    }

    list.innerHTML = "";
    if (facts.length === 0) {
      list.innerHTML = "<div class='options-item'><div class='options-item-title'>No memory facts saved</div></div>";
      return;
    }

    for (var i = 0; i < facts.length; i++) {
      var fact = facts[i] || {};
      var card = document.createElement("div");
      card.className = "options-item";

      var header = document.createElement("div");
      header.className = "options-item-header";

      var title = document.createElement("div");
      title.className = "options-item-title";
      title.textContent = fact.fact || "(empty)";
      header.appendChild(title);

      var weight = document.createElement("span");
      weight.className = "options-pill options-pill-category";
      weight.textContent = "w" + String(fact.weight || 3);
      header.appendChild(weight);

      var remove = document.createElement("button");
      remove.className = "options-btn options-btn-ghost options-btn-sm";
      remove.textContent = "Remove";
      remove.disabled = !enabled;
      remove.dataset.memoryId = fact.id || "";
      remove.addEventListener("click", function(e) {
        var id = e.target.dataset.memoryId || "";
        if (!id) {
          return;
        }
        post("remove_memory_fact", { id: id });
      });
      header.appendChild(remove);
      card.appendChild(header);

      var metaParts = [];
      if (Array.isArray(fact.tags) && fact.tags.length > 0) {
        metaParts.push("tags: " + fact.tags.join(", "));
      }
      if (fact.updatedLocal) {
        metaParts.push("updated: " + fact.updatedLocal);
      }
      if (metaParts.length > 0) {
        var sub = document.createElement("div");
        sub.className = "options-item-sub";
        sub.textContent = metaParts.join(" · ");
        card.appendChild(sub);
      }

      list.appendChild(card);
    }
  }

  function renderDebugPanel() {
    var enabled = normalizeBool(state.options.debugToolsEnabled);
    var toggle = byId("optEnableDebugTools");
    if (toggle) {
      toggle.checked = enabled;
    }

    var profileBadge = byId("optDebugProfileBadge");
    if (profileBadge) {
      var profileName = (state.options.activeProfileName || "default");
      profileBadge.textContent = profileName;
      profileBadge.classList.toggle("off", !enabled);
      profileBadge.classList.add("options-pill-action");
      profileBadge.setAttribute("role", "button");
      profileBadge.setAttribute("tabindex", "0");
      profileBadge.title = enabled
        ? "Debug tools enabled for profile '" + profileName + "'. Click to open Profile tab."
        : "Debug tools disabled for profile '" + profileName + "'. Click to open Profile tab.";
    }

    var stateLabel = byId("optDebugModeState");
    if (stateLabel) {
      var parts = [];
      parts.push(normalizeBool(state.debugMode)
        ? "Engine debug is enabled."
        : "Engine debug is disabled.");

      var metrics = state.lastTurnMetrics;
      if (metrics && typeof metrics === "object") {
        var durationMs = Number(metrics.durationMs);
        var durationText = Number.isFinite(durationMs)
          ? (durationMs >= 1000 ? (durationMs / 1000).toFixed(1) + "s" : Math.max(0, Math.floor(durationMs)) + "ms")
          : "n/a";
        var outcome = metrics.outcome ? String(metrics.outcome) : "unknown";
        var toolCalls = Number(metrics.toolCalls);
        var queueWait = Number(metrics.queueWaitMs);
        var queueWaitText = Number.isFinite(queueWait) && queueWait > 0
          ? " queue " + Math.floor(queueWait) + "ms"
          : "";
        var callsText = Number.isFinite(toolCalls) ? String(Math.max(0, Math.floor(toolCalls))) : "0";
        parts.push("Last turn: " + outcome + " in " + durationText + ", tools " + callsText + queueWaitText + ".");
      }

      var queuedTurnCount = Number(state.queuedTurnCount);
      if (Number.isFinite(queuedTurnCount) && queuedTurnCount > 0) {
        parts.push("Turn queue: " + Math.floor(queuedTurnCount) + ".");
      }

      var queuedPromptCount = Number(state.queuedPromptCount);
      if (Number.isFinite(queuedPromptCount) && queuedPromptCount > 0) {
        parts.push("Sign-in queue: " + Math.floor(queuedPromptCount) + ".");
      }

      if (Array.isArray(state.activityTimeline) && state.activityTimeline.length > 0) {
        parts.push("Live timeline: " + state.activityTimeline.join(" > "));
      }

      stateLabel.textContent = parts.join(" ");
    }

    var toggleEngine = byId("btnDebugToggleEngine");
    if (toggleEngine) {
      toggleEngine.disabled = !enabled;
      toggleEngine.textContent = normalizeBool(state.debugMode) ? "Disable Engine Debug" : "Enable Engine Debug";
    }

    var wheel = byId("btnDebugCopyWheel");
    if (wheel) {
      wheel.disabled = !enabled;
    }

    var startup = byId("btnDebugCopyStartupLog");
    if (startup) {
      startup.disabled = !enabled;
    }

    var restart = byId("btnDebugRestartSidecar");
    if (restart) {
      restart.disabled = !enabled;
    }

    var memState = byId("optMemoryDebugState");
    var memKv = byId("memDebugKv");
    var memCopy = byId("btnDebugCopyMemory");
    var memRecompute = byId("btnDebugRecomputeMemory");
    var memQuality = byId("memDebugQuality");
    var memHistory = byId("memDebugHistory");
    if (memCopy) {
      memCopy.disabled = !enabled;
    }
    if (memRecompute) {
      memRecompute.disabled = !enabled;
    }
    if (memKv) {
      memKv.textContent = "";
    }
    if (memHistory) {
      memHistory.textContent = "";
    }
    if (memQuality) {
      memQuality.textContent = "none";
      memQuality.classList.remove("options-pill-routing-high", "options-pill-routing-medium", "options-pill-routing-low");
      memQuality.classList.add("options-pill-routing-low");
    }

    var snapshot = state.options.memoryDebug;
    if (!snapshot || typeof snapshot !== "object") {
      if (memState) {
        memState.textContent = "No memory diagnostics yet. Send a message to compute a snapshot.";
      }
      if (memCopy) {
        memCopy.disabled = true;
      }
      return;
    }

    if (memState) {
      var updated = snapshot.updatedLocal ? String(snapshot.updatedLocal) : "";
      memState.textContent = updated ? ("Last updated: " + updated) : "Memory diagnostics available.";
    }

    function fmtInt(value) {
      var n = Number(value);
      if (!Number.isFinite(n)) {
        return "0";
      }
      return String(Math.max(0, Math.floor(n)));
    }

    function fmtFloat(value) {
      var n = Number(value);
      if (!Number.isFinite(n)) {
        return "0.000";
      }
      return n.toFixed(3);
    }

    function appendKv(label, value) {
      if (!memKv) {
        return;
      }
      var k = document.createElement("div");
      k.className = "options-k";
      k.textContent = label;
      var v = document.createElement("div");
      v.className = "options-v";
      v.textContent = value;
      memKv.appendChild(k);
      memKv.appendChild(v);
    }

    appendKv("facts", fmtInt(snapshot.availableFacts));
    appendKv("candidates", fmtInt(snapshot.candidateFacts));
    appendKv("selected", fmtInt(snapshot.selectedFacts));
    appendKv("user tokens", fmtInt(snapshot.userTokenCount));
    appendKv("top score", fmtFloat(snapshot.topScore));
    appendKv("top similarity", fmtFloat(snapshot.topSemanticSimilarity));
    appendKv("avg selected similarity", fmtFloat(snapshot.averageSelectedSimilarity));
    appendKv("avg selected relevance", fmtFloat(snapshot.averageSelectedRelevance));
    appendKv("cache entries", fmtInt(snapshot.cacheEntries));

    if (memQuality) {
      var q = (snapshot.quality || "none");
      memQuality.textContent = q;
      memQuality.classList.remove("options-pill-routing-high", "options-pill-routing-medium", "options-pill-routing-low");
      if (q === "good") {
        memQuality.classList.add("options-pill-routing-high");
      } else if (q === "ok") {
        memQuality.classList.add("options-pill-routing-medium");
      } else {
        memQuality.classList.add("options-pill-routing-low");
      }
    }

    if (memHistory && Array.isArray(snapshot.history) && snapshot.history.length > 0) {
      for (var h = snapshot.history.length - 1; h >= 0; h--) {
        var item = snapshot.history[h] || {};
        var row = document.createElement("div");
        row.className = "options-note";
        var t = item.updatedLocal ? String(item.updatedLocal) : "";
        var qh = item.quality ? String(item.quality) : "";
        var sel = fmtInt(item.selectedFacts);
        var rel = fmtFloat(item.averageSelectedRelevance);
        var sim = fmtFloat(item.averageSelectedSimilarity);
        row.textContent = (t ? (t + " | ") : "") + "q=" + qh + " selected=" + sel + " rel=" + rel + " sim=" + sim;
        memHistory.appendChild(row);
      }
    }

    if (memCopy) {
      memCopy.disabled = !enabled;
    }
    if (memRecompute) {
      memRecompute.disabled = !enabled;
    }
  }
