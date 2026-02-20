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
    var packId = inferPackIdFromTool(tool);
    var packUnavailable = !packIsAvailable(packId);
    var packUnavailableReason = packDisabledReason(packId);
    if (packUnavailable) {
      item.classList.add("options-item-unavailable");
    }

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
    toggle.disabled = packUnavailable;
    toggle.setAttribute("aria-label", "Enable " + (tool.displayName || tool.name));
    if (packUnavailable && packUnavailableReason) {
      toggle.title = packUnavailableReason;
    }
    toggle.dataset.toolName = tool.name;
    toggle.addEventListener("change", function(e) {
      if (packUnavailable) {
        return;
      }
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

    if (packUnavailable) {
      var unavailable = document.createElement("div");
      unavailable.className = "options-item-warning";
      unavailable.textContent = packUnavailableReason
        ? ("Unavailable: " + packUnavailableReason)
        : "Unavailable in current runtime.";
      item.appendChild(unavailable);
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
    var runtimeDisabledByConfig = !!packId && packDisabledByRuntimeConfiguration(packId);
    if (packId && !packIsAvailable(packId) && !runtimeDisabledByConfig) {
      return;
    }

    if ((!Array.isArray(groupTools) || groupTools.length === 0) && runtimeDisabledByConfig && packId) {
      post("set_pack_enabled", { packId: packId, enabled: enabled });
      return;
    }

    if (!Array.isArray(groupTools) || groupTools.length === 0) {
      return;
    }

    var changed = false;
    for (var i = 0; i < groupTools.length; i++) {
      var tool = groupTools[i];
      var toolPackId = inferPackIdFromTool(tool);
      if (!packIsAvailable(toolPackId)) {
        continue;
      }
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

    var toolPackId = inferPackIdFromTool(tool);
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
      packSourceLabel(packSourceKind(toolPackId)),
      packDisabledReason(toolPackId),
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

    var allTools = state.options.tools || [];
    var tools = allTools.slice();
    var filter = normalizeToolFilter(state.options.toolFilter);
    if (filter) {
      tools = allTools.filter(function(tool) { return toolMatchesFilter(tool, filter); });
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

    function packMatchesFilter(packId, query) {
      if (!query) {
        return true;
      }

      var haystack = [
        packId || "",
        packDisplayName(packId),
        packDescription(packId),
        packSourceLabel(packSourceKind(packId)),
        packDisabledReason(packId)
      ].join(" ").toLowerCase();

      return haystack.indexOf(query) >= 0;
    }

    var packs = state.options.packs || [];
    for (var p = 0; p < packs.length; p++) {
      var policyPack = packs[p] || {};
      var policyPackId = normalizePackId(policyPack.id);
      if (!policyPackId) {
        continue;
      }

      if (filter && !groups[policyPackId] && !packMatchesFilter(policyPackId, filter)) {
        continue;
      }

      if (!groups[policyPackId]) {
        groups[policyPackId] = [];
      }
    }

    var order = Object.keys(groups);
    if (order.length === 0) {
      if (!filter && state.options.toolsLoading === true) {
        toolsEl.innerHTML = "<div class='options-item'><div class='options-item-title'>Loading tool packs...</div><div class='options-item-sub'>Runtime is still publishing pack metadata.</div></div>";
        return;
      }

      if (filter) {
        toolsEl.innerHTML = "<div class='options-item'><div class='options-item-title'>No tools match filter</div></div>";
      } else {
        toolsEl.innerHTML = "<div class='options-item'><div class='options-item-title'>No tools registered</div></div>";
      }
      return;
    }

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
      var packAvailable = packIsAvailable(currentPackId);
      var packRuntimeDisabledByConfig = packDisabledByRuntimeConfiguration(currentPackId);
      var packUnavailable = !packAvailable && !packRuntimeDisabledByConfig;
      var packUnavailableReason = packDisabledReason(currentPackId);

      var details = document.createElement("details");
      details.className = "options-accordion";
      if (packUnavailable) {
        details.classList.add("unavailable");
      }
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

      if (packUnavailableReason) {
        var reason = document.createElement("span");
        reason.className = "options-accordion-reason";
        reason.textContent = packUnavailable
          ? ("Unavailable: " + packUnavailableReason)
          : packUnavailableReason;
        reason.title = packUnavailableReason;
        heading.appendChild(reason);
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

      var actionableTools = [];
      for (var a = 0; a < groupTools.length; a++) {
        if (packIsAvailable(inferPackIdFromTool(groupTools[a]))) {
          actionableTools.push(groupTools[a]);
        }
      }

      var enabledCount = 0;
      for (var c = 0; c < actionableTools.length; c++) {
        if (normalizeBool(actionableTools[c].enabled)) {
          enabledCount++;
        }
      }
      var allEnabled = actionableTools.length > 0 && enabledCount === actionableTools.length;
      var someEnabled = enabledCount > 0;

      var pill = document.createElement("span");
      var packHasTools = actionableTools.length > 0;
      var isPackLoaded = packHasTools && allEnabled;
      pill.className = "options-pill" + (isPackLoaded && packAvailable ? "" : " off");
      if (packUnavailable) {
        pill.textContent = "Unavailable";
      } else if (!packHasTools && packRuntimeDisabledByConfig) {
        pill.textContent = "Disabled";
      } else if (!packHasTools) {
        pill.textContent = "No tools";
      } else {
        pill.textContent = allEnabled ? "Loaded" : (someEnabled ? "Partial" : "Disabled");
      }
      summaryRight.appendChild(pill);

      var packToggle = document.createElement("input");
      packToggle.className = "options-toggle options-toggle-pack";
      packToggle.type = "checkbox";
      packToggle.checked = packHasTools && allEnabled;
      packToggle.indeterminate = packHasTools && !allEnabled && someEnabled;
      packToggle.disabled = packUnavailable || (!packHasTools && !packRuntimeDisabledByConfig);
      packToggle.setAttribute("aria-label", "Enable pack " + packDisplayName(currentPackId));
      if (packUnavailable && packUnavailableReason) {
        packToggle.title = packUnavailableReason;
      } else if (packRuntimeDisabledByConfig) {
        packToggle.title = "Disabled by runtime configuration. Toggle to apply this pack setting live.";
      } else if (!packHasTools) {
        packToggle.title = "No tools are currently registered for this pack.";
      }
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
      if (groupTools.length === 0) {
        var emptyCard = document.createElement("div");
        emptyCard.className = "options-item";

        var emptyTitle = document.createElement("div");
        emptyTitle.className = "options-item-title";
        emptyTitle.textContent = packUnavailable
          ? "Pack unavailable"
          : (!packHasTools && packRuntimeDisabledByConfig)
            ? "Pack disabled"
          : "No tools registered in this pack";
        emptyCard.appendChild(emptyTitle);

        var emptyBody = document.createElement("div");
        emptyBody.className = "options-item-sub";
        emptyBody.textContent = packRuntimeDisabledByConfig
          ? "Pack is disabled by runtime configuration. Enable the toggle to apply this pack setting live."
          : packUnavailableReason
          ? packUnavailableReason
          : "This pack is present in policy metadata but did not register any tool definitions.";
        emptyCard.appendChild(emptyBody);

        body.appendChild(emptyCard);
      } else {
        for (var t = 0; t < groupTools.length; t++) {
          body.appendChild(createToolCard(groupTools[t]));
        }
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
    var normalized = String(value || "").trim().toLowerCase();
    if (normalized === "compatible-http" || normalized === "compatiblehttp" || normalized === "http" || normalized === "local" || normalized === "ollama" || normalized === "lmstudio" || normalized === "lm-studio") {
      return "compatible-http";
    }
    if (normalized === "copilot-cli" || normalized === "copilot" || normalized === "github-copilot" || normalized === "githubcopilot") {
      return "copilot-cli";
    }
    return "native";
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

  function normalizePositiveInt(value) {
    if (value == null || value === "") {
      return 0;
    }
    var parsed = Number(value);
    if (!isFinite(parsed) || parsed <= 0) {
      return 0;
    }
    return Math.floor(parsed);
  }

  function formatModelContextLabel(loadedContextLength, maxContextLength) {
    var loaded = normalizePositiveInt(loadedContextLength);
    var max = normalizePositiveInt(maxContextLength);
    if (loaded > 0 && max > 0) {
      return "ctx " + loaded + "/" + max;
    }
    if (max > 0) {
      return "ctx " + max;
    }
    return "";
  }

  function buildModelMetadataTokens(modelItem) {
    var item = modelItem || {};
    var runtimeState = normalizeModelText(item.runtimeState || item.RuntimeState || item.state || item.State).toLowerCase();
    var quantization = normalizeModelText(item.quantization || item.Quantization);
    var architecture = normalizeModelText(item.architecture || item.Architecture || item.arch || item.Arch);
    var modelType = normalizeModelText(item.modelType || item.ModelType || item.type || item.Type);
    var contextLabel = formatModelContextLabel(
      item.loadedContextLength || item.LoadedContextLength || item.loaded_context_length,
      item.maxContextLength || item.MaxContextLength || item.max_context_length);
    var capabilities = Array.isArray(item.capabilities)
      ? item.capabilities
      : (Array.isArray(item.Capabilities) ? item.Capabilities : []);

    var tokens = [];
    if (runtimeState) {
      tokens.push(runtimeState);
    }
    if (quantization) {
      tokens.push(quantization);
    }
    if (architecture) {
      tokens.push(architecture);
    }
    if (modelType) {
      tokens.push(modelType);
    }
    if (contextLabel) {
      tokens.push(contextLabel);
    }
    if (capabilities.length > 0) {
      var normalizedCaps = [];
      for (var i = 0; i < capabilities.length; i++) {
        var cap = normalizeModelText(capabilities[i]);
        if (!cap) {
          continue;
        }
        normalizedCaps.push(cap);
        if (normalizedCaps.length >= 2) {
          break;
        }
      }
      if (normalizedCaps.length > 0) {
        tokens.push(normalizedCaps.join(","));
      }
    }

    return tokens;
  }

  function buildModelMetadataLabel(modelItem) {
    var tokens = buildModelMetadataTokens(modelItem);
    return tokens.length > 0 ? (" [" + tokens.join(" · ") + "]") : "";
  }

  function summarizeModelRuntimeMetadata(models) {
    var summary = {
      hasMetadata: false,
      loadedCount: 0,
      notLoadedCount: 0,
      maxLoadedContext: 0
    };

    if (!Array.isArray(models) || models.length === 0) {
      return summary;
    }

    for (var i = 0; i < models.length; i++) {
      var item = models[i] || {};
      var runtimeState = normalizeModelText(item.runtimeState || item.RuntimeState || item.state || item.State).toLowerCase();
      if (runtimeState === "loaded") {
        summary.loadedCount++;
      } else if (runtimeState.indexOf("not") >= 0 || runtimeState === "unloaded") {
        summary.notLoadedCount++;
      }

      var loadedContext = normalizePositiveInt(item.loadedContextLength || item.LoadedContextLength || item.loaded_context_length);
      if (loadedContext > summary.maxLoadedContext) {
        summary.maxLoadedContext = loadedContext;
      }

      if (runtimeState || buildModelMetadataTokens(item).length > 0) {
        summary.hasMetadata = true;
      }
    }

    return summary;
  }

  function isOllamaBaseUrl(value) {
    var normalized = String(value || "").trim().toLowerCase();
    if (!normalized) {
      return false;
    }
    return normalized.indexOf("127.0.0.1:11434") >= 0 || normalized.indexOf("localhost:11434") >= 0;
  }

  function isCopilotBaseUrl(value) {
    var normalized = String(value || "").trim().toLowerCase();
    if (!normalized) {
      return false;
    }
    return normalized.indexOf("api.githubcopilot.com") >= 0;
  }

  function normalizeModelFilter(value) {
    return String(value || "").trim().toLowerCase();
  }

  function normalizeReasoningEffortValue(value) {
    var normalized = String(value || "").trim().toLowerCase();
    if (normalized === "x-high" || normalized === "x_high") {
      normalized = "xhigh";
    }
    if (normalized === "minimal" || normalized === "low" || normalized === "medium" || normalized === "high" || normalized === "xhigh") {
      return normalized;
    }
    return "";
  }

  function normalizeReasoningSummaryValue(value) {
    var normalized = String(value || "").trim().toLowerCase();
    if (normalized === "auto" || normalized === "concise" || normalized === "detailed" || normalized === "off") {
      return normalized;
    }
    return "";
  }

  function normalizeTextVerbosityValue(value) {
    var normalized = String(value || "").trim().toLowerCase();
    if (normalized === "low" || normalized === "medium" || normalized === "high") {
      return normalized;
    }
    return "";
  }

  function resolveReasoningSupport(transport, compatiblePreset) {
    var normalizedTransport = normalizeLocalTransport(transport);
    var preset = String(compatiblePreset || "manual").trim().toLowerCase();
    if (normalizedTransport === "copilot-cli") {
      return {
        supported: false,
        reason: "GitHub Copilot subscription runtime currently does not expose reasoning controls."
      };
    }
    if (normalizedTransport === "compatible-http"
      && (preset === "anthropic-bridge" || preset === "gemini-bridge")) {
      return {
        supported: false,
        reason: "Experimental Anthropic/Gemini bridge presets currently use provider-default reasoning."
      };
    }
    return {
      supported: true,
      reason: ""
    };
  }

  function resolveRuntimeProviderLabel(transport, compatiblePreset, copilotConnected) {
    var normalizedTransport = normalizeLocalTransport(transport);
    var preset = String(compatiblePreset || "manual").trim().toLowerCase();
    if (normalizedTransport === "copilot-cli") {
      return "GitHub Copilot subscription runtime";
    }
    if (normalizedTransport !== "compatible-http") {
      return "ChatGPT runtime (OpenAI native)";
    }
    if (preset === "lmstudio") {
      return "LM Studio runtime";
    }
    if (preset === "ollama") {
      return "Ollama runtime";
    }
    if (preset === "openai") {
      return "OpenAI API runtime";
    }
    if (preset === "azure-openai") {
      return "Azure OpenAI runtime";
    }
    if (preset === "anthropic-bridge") {
      return "Anthropic bridge runtime";
    }
    if (preset === "gemini-bridge") {
      return "Gemini bridge runtime";
    }
    if (copilotConnected) {
      return "GitHub Copilot runtime";
    }
    return "Compatible HTTP runtime";
  }

  function appendRuntimeCapabilityRow(listEl, name, status, value, note) {
    if (!listEl) {
      return;
    }
    var normalizedStatus = String(status || "limited").trim().toLowerCase();
    if (normalizedStatus !== "supported" && normalizedStatus !== "limited" && normalizedStatus !== "unavailable") {
      normalizedStatus = "limited";
    }

    var row = document.createElement("div");
    row.className = "options-runtime-capability";

    var head = document.createElement("div");
    head.className = "options-runtime-capability-head";

    var nameEl = document.createElement("div");
    nameEl.className = "options-runtime-capability-name";
    nameEl.textContent = String(name || "Capability");
    head.appendChild(nameEl);

    var valueEl = document.createElement("span");
    valueEl.className = "options-runtime-capability-value options-runtime-capability-value-" + normalizedStatus;
    valueEl.textContent = String(value || "");
    head.appendChild(valueEl);

    row.appendChild(head);

    var noteText = normalizeModelText(note || "");
    if (noteText) {
      var noteEl = document.createElement("div");
      noteEl.className = "options-runtime-capability-note";
      noteEl.textContent = noteText;
      row.appendChild(noteEl);
    }

    listEl.appendChild(row);
  }

  function renderRuntimeCapabilities(options) {
    var listEl = byId("optRuntimeCapabilities");
    var titleEl = byId("optRuntimeCapabilitiesTitle");
    if (!listEl) {
      if (titleEl) {
        titleEl.hidden = true;
      }
      return;
    }

    var data = options && typeof options === "object" ? options : {};
    var usage = Array.isArray(data.accountUsage) ? data.accountUsage : [];
    var usageWithRetry = 0;
    for (var u = 0; u < usage.length; u++) {
      var usageItem = usage[u] || {};
      var retryAfterMinutes = Number(usageItem.retryAfterMinutes);
      var windowResetMinutes = Number(usageItem.rateLimitWindowResetMinutes);
      var retryAfterUtc = normalizeModelText(usageItem.usageLimitRetryAfterUtc || "");
      if ((Number.isFinite(retryAfterMinutes) && retryAfterMinutes >= 0)
          || (Number.isFinite(windowResetMinutes) && windowResetMinutes >= 0)
          || retryAfterUtc.length > 0) {
        usageWithRetry++;
      }
    }

    var trackedAccounts = Number(data.trackedAccounts);
    if (!Number.isFinite(trackedAccounts) || trackedAccounts < 0) {
      trackedAccounts = usage.length;
    }
    var usageWithRetrySignals = Number(data.accountsWithRetrySignals);
    if (!Number.isFinite(usageWithRetrySignals) || usageWithRetrySignals < 0) {
      usageWithRetrySignals = usageWithRetry;
    }
    var providerLabel = normalizeModelText(data.providerLabel || "");
    if (!providerLabel) {
      providerLabel = resolveRuntimeProviderLabel(
        data.transport || "native",
        data.compatiblePreset || "manual",
        data.copilotConnected === true);
    }
    var supportsReasoning = data.supportsReasoningControls === true;
    var supportedReasoningEfforts = Array.isArray(data.supportedReasoningEfforts)
      ? data.supportedReasoningEfforts
      : [];
    var reasoningSupportReason = normalizeModelText(data.reasoningSupportReason || "");
    var openAIAuthMode = normalizeOpenAIAuthModeValue(data.openAIAuthMode || "bearer");
    var basicUsername = normalizeModelText(data.openAIBasicUsername || "");

    listEl.innerHTML = "";

    var supportsLiveApply = data.supportsLiveApply !== false;
    var requiresProcessRestart = data.requiresProcessRestart === true;
    var activeRuntimeNote = data.isApplying === true
      ? "Applying runtime settings now. The current session remains active while settings update."
      : (supportsLiveApply && !requiresProcessRestart
          ? "Switching runtime updates the active provider profile without forcing a process restart."
          : "Runtime changes may require reconnecting the runtime process.");

    appendRuntimeCapabilityRow(
      listEl,
      "Active runtime",
      "supported",
      providerLabel,
      activeRuntimeNote);

    appendRuntimeCapabilityRow(
      listEl,
      "Model selection",
      data.supportsModelCatalog ? "supported" : "limited",
      data.supportsModelCatalog ? "Catalog + manual model ID" : "Manual model ID",
      data.supportsModelCatalog
        ? "Use discovered models from the list, or keep \"Manual model input\" selected and type an exact model ID."
        : "Type an exact model ID in the Model field and click Apply Runtime.");

    appendRuntimeCapabilityRow(
      listEl,
      "Reasoning controls",
      supportsReasoning ? "supported" : "limited",
      supportsReasoning ? "Effort, summary, and verbosity" : "Provider defaults only",
      supportsReasoning
        ? (supportedReasoningEfforts.length > 0
            ? ("Reported efforts: " + supportedReasoningEfforts.join(", ") + ".")
            : (reasoningSupportReason
                ? reasoningSupportReason
                : "Current provider supports reasoning fields; model metadata may refine available efforts."))
        : (reasoningSupportReason || "Current provider profile does not expose reasoning controls."));

    if (data.isNativeTransport === true) {
      appendRuntimeCapabilityRow(
        listEl,
        "Authentication",
        "supported",
        "ChatGPT sign-in",
        "Use native account slot switching to move between ChatGPT accounts quickly.");
    } else if (data.isCopilotCli === true) {
      appendRuntimeCapabilityRow(
        listEl,
        "Authentication",
        "supported",
        "GitHub Copilot sign-in",
        "Copilot subscription runtime uses GitHub authentication and does not require an API key.");
    } else {
      var compatiblePreset = String(data.compatiblePreset || "manual").trim().toLowerCase();
      var authStatus = openAIAuthMode === "none" ? "limited" : "supported";
      var authValue = openAIAuthMode === "basic"
        ? "Basic auth"
        : (openAIAuthMode === "none" ? "No auth header" : "Bearer token");
      var authNote = openAIAuthMode === "basic"
        ? (basicUsername ? ("Username: " + basicUsername + ". Password is stored securely.") : "Username/password are used for requests.")
        : (openAIAuthMode === "none"
            ? "No Authorization header will be sent."
            : "Use API key for Bearer authentication.");
      if (compatiblePreset === "anthropic-bridge" || compatiblePreset === "gemini-bridge") {
        authStatus = "limited";
        if (openAIAuthMode === "basic") {
          authValue = "Bridge credentials (Basic)";
        }
        authNote = "Bridge presets use endpoint credentials managed by the bridge service. Browser subscription session reuse is not wired yet.";
      }
      appendRuntimeCapabilityRow(listEl, "Authentication", authStatus, authValue, authNote);
    }

    var nativeAccountSlots = Number(data.nativeAccountSlots);
    if (!Number.isFinite(nativeAccountSlots) || nativeAccountSlots <= 0) {
      nativeAccountSlots = 3;
    }

    appendRuntimeCapabilityRow(
      listEl,
      "Native account slots",
      data.isNativeTransport === true ? "supported" : "unavailable",
      data.isNativeTransport === true ? (String(nativeAccountSlots) + " slots available") : "Native runtime only",
      data.isNativeTransport === true
        ? ("Active slot: " + String(data.activeNativeAccountSlot || 1) + ". Slot IDs are profile-scoped.")
        : "Account slot switching is available only in ChatGPT native runtime.");

    appendRuntimeCapabilityRow(
      listEl,
      "Usage + limits",
      trackedAccounts > 0 ? "supported" : "limited",
      trackedAccounts > 0
        ? (String(trackedAccounts) + (trackedAccounts === 1 ? " account tracked" : " accounts tracked"))
        : "Waiting for usage signal",
      trackedAccounts > 0
        ? (usageWithRetrySignals > 0
            ? "Token counters and retry/renew timing are visible for accounts that expose limit metadata."
            : "Token counters are active. Retry/renew timing appears once providers return limit metadata.")
        : "Usage appears after the provider returns token metrics.");

    listEl.hidden = listEl.children.length === 0;
    if (titleEl) {
      titleEl.hidden = listEl.hidden;
    }
  }

  function normalizeTemperatureText(value) {
    if (value == null) {
      return "";
    }

    if (typeof value === "number" && isFinite(value)) {
      return String(value);
    }

    var normalized = String(value || "").trim();
    if (!normalized) {
      return "";
    }

    var parsed = Number(normalized);
    if (!isFinite(parsed) || parsed < 0 || parsed > 2) {
      return "";
    }

    return normalized;
  }

  function normalizeOpenAIAuthModeValue(value) {
    var normalized = String(value || "").trim().toLowerCase();
    if (normalized === "basic") {
      return "basic";
    }
    if (normalized === "none" || normalized === "off") {
      return "none";
    }
    return "bearer";
  }

  function detectCompatibleProviderPreset(baseUrl) {
    var normalized = String(baseUrl || "").trim().toLowerCase();
    if (!normalized) {
      return "manual";
    }
    if (normalized.indexOf("127.0.0.1:1234") >= 0 || normalized.indexOf("localhost:1234") >= 0) {
      return "lmstudio";
    }
    if (normalized.indexOf("127.0.0.1:11434") >= 0 || normalized.indexOf("localhost:11434") >= 0) {
      return "ollama";
    }
    if (normalized.indexOf("api.openai.com") >= 0) {
      return "openai";
    }
    if (normalized.indexOf(".openai.azure.com") >= 0) {
      return "azure-openai";
    }
    if (normalized.indexOf("anthropic") >= 0 || normalized.indexOf("claude") >= 0) {
      return "anthropic-bridge";
    }
    if (normalized.indexOf("gemini") >= 0 || normalized.indexOf("googleapis.com") >= 0) {
      return "gemini-bridge";
    }
    return "manual";
  }

  function formatUtcDateTime(value) {
    var text = String(value || "").trim();
    if (!text) {
      return "";
    }
    var parsed = new Date(text);
    if (isNaN(parsed.getTime())) {
      return "";
    }
    return parsed.toLocaleString();
  }

  function formatTokenCount(value) {
    var numeric = Number(value);
    if (!Number.isFinite(numeric) || numeric <= 0) {
      return "0";
    }
    return Math.floor(numeric).toLocaleString();
  }

  function formatRetryMinutes(minutes) {
    var numeric = Number(minutes);
    if (!Number.isFinite(numeric) || numeric <= 0) {
      return "now";
    }
    if (numeric < 60) {
      return Math.ceil(numeric) + "m";
    }
    var hours = Math.floor(numeric / 60);
    var remainder = Math.ceil(numeric % 60);
    if (remainder <= 0) {
      return hours + "h";
    }
    return hours + "h " + remainder + "m";
  }

  function refreshAccountUsageRetryCountdowns() {
    var usageList = byId("optAccountUsageList");
    if (!usageList) {
      return;
    }

    var retryRows = usageList.querySelectorAll(".options-usage-retry[data-retry-after-utc]");
    if (!retryRows || retryRows.length === 0) {
      return;
    }

    var nowUtc = Date.now();
    for (var i = 0; i < retryRows.length; i++) {
      var row = retryRows[i];
      var retryAfterText = String(row.getAttribute("data-retry-after-utc") || "").trim();
      if (!retryAfterText) {
        continue;
      }
      var retryAfter = new Date(retryAfterText);
      if (isNaN(retryAfter.getTime())) {
        continue;
      }

      var remainingMinutes = Math.ceil((retryAfter.getTime() - nowUtc) / 60000);
      if (!Number.isFinite(remainingMinutes) || remainingMinutes < 0) {
        remainingMinutes = 0;
      }
      var renewAt = String(row.getAttribute("data-renew-at-text") || "").trim();
      row.textContent = "limit retry: " + formatRetryMinutes(remainingMinutes) + (renewAt ? " (" + renewAt + ")" : "");
    }
  }

  function renderLocalModelOptions() {
    var local = state.options.localModel || {};
    var runtimeCapabilities = local.runtimeCapabilities && typeof local.runtimeCapabilities === "object"
      ? local.runtimeCapabilities
      : {};
    var isApplying = local.isApplying === true;
    var transport = normalizeLocalTransport(local.transport);
    var isCompatible = transport === "compatible-http";
    var isCopilotCli = transport === "copilot-cli";
    var supportsModelCatalog = isCompatible || isCopilotCli || transport === "native";
    if (typeof runtimeCapabilities.supportsModelCatalog === "boolean") {
      supportsModelCatalog = runtimeCapabilities.supportsModelCatalog;
    }
    var baseUrl = String(local.baseUrl || "");
    var openAIAuthMode = normalizeOpenAIAuthModeValue(local.openAIAuthMode || "bearer");
    var openAIBasicUsername = normalizeModelText(local.openAIBasicUsername || "");
    var compatiblePreset = isCompatible ? detectCompatibleProviderPreset(baseUrl) : "manual";
    var runtimePreset = normalizeModelText(runtimeCapabilities.compatiblePreset || "").toLowerCase();
    if (isCompatible && runtimePreset) {
      compatiblePreset = runtimePreset;
    }
    var runtimeProviderLabel = normalizeModelText(runtimeCapabilities.providerLabel || "");
    var modelsEndpoint = normalizeModelText(local.modelsEndpoint || "");
    var model = normalizeModelText(local.model || "");
    var openAIAccountId = normalizeModelText(local.openAIAccountId || "");
    var activeNativeAccountSlot = Number(local.activeNativeAccountSlot);
    if (!Number.isFinite(activeNativeAccountSlot) || activeNativeAccountSlot < 1 || activeNativeAccountSlot > 3) {
      activeNativeAccountSlot = 1;
    } else {
      activeNativeAccountSlot = Math.floor(activeNativeAccountSlot);
    }
    var nativeAccountSlots = Array.isArray(local.nativeAccountSlots) ? local.nativeAccountSlots : [];
    var reasoningEffort = normalizeReasoningEffortValue(local.reasoningEffort || "");
    var reasoningSummary = normalizeReasoningSummaryValue(local.reasoningSummary || "");
    var textVerbosity = normalizeTextVerbosityValue(local.textVerbosity || "");
    var temperatureText = normalizeTemperatureText(local.temperature);
    var models = Array.isArray(local.models) ? local.models : [];
    var favorites = toStringArray(local.favoriteModels);
    var recents = toStringArray(local.recentModels);
    var authenticatedAccountId = normalizeModelText(local.authenticatedAccountId || "");
    var accountUsage = Array.isArray(local.accountUsage) ? local.accountUsage : [];
    var activeAccountUsage = local.activeAccountUsage && typeof local.activeAccountUsage === "object"
      ? local.activeAccountUsage
      : null;
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
    var ollamaConnected = isCompatible && isOllamaBaseUrl(baseUrl);
    var copilotConnected = isCompatible && isCopilotBaseUrl(baseUrl);
    var runtimeSummary = byId("optRuntimeSummary");
    if (runtimeSummary) {
      if (isCopilotCli) {
        runtimeSummary.textContent = "Current: GitHub Copilot subscription runtime (CLI transport).";
      } else if (isCompatible) {
        var endpoint = baseUrl ? baseUrl : "(base URL not set)";
        var providerLabel = runtimeProviderLabel
          ? runtimeProviderLabel
          : resolveRuntimeProviderLabel(transport, compatiblePreset, copilotConnected);
        runtimeSummary.textContent = "Current: " + providerLabel + " via " + endpoint + ".";
      } else {
        runtimeSummary.textContent = "Current: ChatGPT runtime (OpenAI native).";
      }
    }

    var runtimeAuthHint = byId("optRuntimeAuthHint");
    if (runtimeAuthHint) {
      if (isCopilotCli) {
        runtimeAuthHint.textContent = "Copilot subscription runtime uses GitHub Copilot sign-in. API key is not used in this mode.";
      } else if (copilotConnected) {
        runtimeAuthHint.textContent = "Copilot runtime uses a GitHub token in API key. ChatGPT sign-in remains separate.";
      } else if (isCompatible) {
        runtimeAuthHint.textContent = "Compatible HTTP auth mode: " + openAIAuthMode + ". You can stay signed in to ChatGPT while running other providers.";
      } else {
        runtimeAuthHint.textContent = "ChatGPT sign-in and runtime provider are separate. You can switch to LM Studio any time.";
      }
    }

    var runtimeBadge = byId("optLocalRuntimeBadge");
    if (runtimeBadge) {
      var runtimeName = "ChatGPT Native";
      if (isCopilotCli) {
        runtimeName = "GitHub Copilot Subscription";
      } else if (lmStudioConnected) {
        runtimeName = "LM Studio";
      } else if (ollamaConnected) {
        runtimeName = "Ollama";
      } else if (copilotConnected) {
        runtimeName = "GitHub Copilot";
      } else if (isCompatible) {
        runtimeName = "Compatible HTTP";
      }
      var activeModel = model ? model : "(auto)";
      var runtimeText = "Active runtime: " + runtimeName + " | Active model: " + activeModel;
      if (transport === "native" && authenticatedAccountId) {
        runtimeText += " | Account: " + authenticatedAccountId;
      }
      if (transport === "native") {
        runtimeText += " | Slot " + String(activeNativeAccountSlot);
      }
      runtimeBadge.textContent = runtimeText;
    }

    var simpleHint = byId("optLocalSimpleHint");
    if (simpleHint) {
      if (isApplying) {
        simpleHint.textContent = "Applying runtime settings. Please wait while the runtime updates.";
      } else if (transport === "native") {
        simpleHint.textContent = "ChatGPT runtime is active. Switch to LM Studio runtime to use local models.";
      } else if (isCopilotCli) {
        simpleHint.textContent = "Copilot subscription runtime is active. Use Sign In to authenticate your GitHub Copilot account.";
      } else if (copilotConnected) {
        simpleHint.textContent = "GitHub Copilot runtime is active. Use a GitHub token in API key and refresh models.";
      } else if (lmStudioConnected) {
        simpleHint.textContent = "LM Studio runtime is active for this profile.";
      } else if (runtimeDetectionHasRun && !lmStudioAvailable) {
        simpleHint.textContent = "LM Studio not detected on http://127.0.0.1:1234/v1. Start LM Studio and click Apply Runtime, or configure Advanced Runtime.";
      } else {
        simpleHint.textContent = "Local runtime is active. Use LM Studio Runtime for the default LM Studio endpoint.";
      }
    }

    function resolveNativeSlotState(slotNumber) {
      for (var s = 0; s < nativeAccountSlots.length; s++) {
        var slot = nativeAccountSlots[s] || {};
        var currentSlot = Number(slot.slot);
        if (Number.isFinite(currentSlot) && Math.floor(currentSlot) === slotNumber) {
          return slot;
        }
      }
      return null;
    }

    var nativeSlotSelectRow = byId("optNativeAccountSlotRow");
    var nativeAccountIdRow = byId("optNativeAccountIdRow");
    var nativeAccountHint = byId("optNativeAccountHint");
    var nativeSlotSelect = byId("optNativeAccountSlot");
    var nativeAccountIdInput = byId("optNativeAccountId");
    var isNativeTransport = transport === "native";
    if (nativeSlotSelectRow) {
      nativeSlotSelectRow.hidden = !isNativeTransport;
    }
    if (nativeAccountIdRow) {
      nativeAccountIdRow.hidden = !isNativeTransport;
    }
    if (nativeAccountHint) {
      nativeAccountHint.hidden = !isNativeTransport;
    }
    if (nativeSlotSelect) {
      nativeSlotSelect.innerHTML = "";
      for (var slotIndex = 1; slotIndex <= 3; slotIndex++) {
        var slotState = resolveNativeSlotState(slotIndex) || {};
        var slotAccountId = normalizeModelText(slotState.accountId || "");
        var slotLabel = "Slot " + String(slotIndex);
        if (slotAccountId) {
          slotLabel += " | " + slotAccountId;
          var slotUsageTokens = Number(slotState.usageTotalTokens);
          if (Number.isFinite(slotUsageTokens) && slotUsageTokens > 0) {
            slotLabel += " | " + formatTokenCount(slotUsageTokens) + " tok";
          }
        } else {
          slotLabel += " | unassigned";
        }
        var slotOption = document.createElement("option");
        slotOption.value = String(slotIndex);
        slotOption.textContent = slotLabel;
        nativeSlotSelect.appendChild(slotOption);
      }
      nativeSlotSelect.value = String(activeNativeAccountSlot);
      syncCustomSelect(nativeSlotSelect);
      nativeSlotSelect.disabled = !isNativeTransport || isApplying;
    }
    var selectedSlotState = resolveNativeSlotState(activeNativeAccountSlot) || null;
    var selectedSlotAccountId = selectedSlotState
      ? normalizeModelText(selectedSlotState.accountId || "")
      : "";
    if (!selectedSlotAccountId) {
      selectedSlotAccountId = openAIAccountId;
    }
    if (nativeAccountIdInput) {
      nativeAccountIdInput.value = selectedSlotAccountId;
      nativeAccountIdInput.disabled = !isNativeTransport || isApplying;
    }
    if (nativeAccountHint) {
      var hintParts = ["Select slot 1/2/3 to switch accounts quickly."];
      if (selectedSlotAccountId) {
        hintParts.push("Selected slot account: " + selectedSlotAccountId + ".");
      }
      if (authenticatedAccountId) {
        hintParts.push("Authenticated now: " + authenticatedAccountId + ".");
      }
      if (selectedSlotState) {
        var slotPlanType = normalizeModelText(selectedSlotState.planType || "");
        if (slotPlanType) {
          hintParts.push("Plan: " + slotPlanType + ".");
        }
        var slotUsedPercent = Number(selectedSlotState.usedPercent);
        if (Number.isFinite(slotUsedPercent) && slotUsedPercent >= 0) {
          hintParts.push("Window used: " + Math.round(slotUsedPercent) + "%.");
        }
        var slotWindowResetMinutes = Number(selectedSlotState.windowResetMinutes);
        if (Number.isFinite(slotWindowResetMinutes) && slotWindowResetMinutes >= 0) {
          hintParts.push("Window reset: " + formatRetryMinutes(slotWindowResetMinutes) + ".");
        }
        var retryAfterMinutes = Number(selectedSlotState.retryAfterMinutes);
        if (Number.isFinite(retryAfterMinutes) && retryAfterMinutes >= 0) {
          hintParts.push("Limit retry: " + formatRetryMinutes(retryAfterMinutes) + ".");
        }
        if (selectedSlotState.limitReached === true) {
          hintParts.push("Limit currently reached.");
        }
      }
      nativeAccountHint.textContent = hintParts.join(" ");
    }

    var useOpenAiRuntimeButton = byId("btnUseOpenAiRuntime");
    if (useOpenAiRuntimeButton) {
      var isNative = transport === "native";
      useOpenAiRuntimeButton.textContent = isNative ? "ChatGPT Runtime Active" : "Use ChatGPT Runtime";
      useOpenAiRuntimeButton.classList.toggle("options-btn-active", isNative);
      useOpenAiRuntimeButton.disabled = isApplying;
    }

    var connectLmStudioButton = byId("btnConnectLmStudio");
    if (connectLmStudioButton) {
      connectLmStudioButton.textContent = lmStudioConnected ? "LM Studio Runtime Active" : "Use LM Studio Runtime";
      connectLmStudioButton.classList.toggle("options-btn-active", lmStudioConnected);
      connectLmStudioButton.disabled = isApplying;
      connectLmStudioButton.title = runtimeDetectionHasRun && !lmStudioAvailable && !lmStudioConnected
        ? "LM Studio was not detected. Start LM Studio and click Auto Detect Runtime in Advanced Runtime."
        : "";
    }

    var useCopilotRuntimeButton = byId("btnUseCopilotRuntime");
    if (useCopilotRuntimeButton) {
      useCopilotRuntimeButton.textContent = isCopilotCli ? "Copilot Subscription Active" : "Use Copilot Subscription";
      useCopilotRuntimeButton.classList.toggle("options-btn-active", isCopilotCli);
      useCopilotRuntimeButton.disabled = isApplying;
      useCopilotRuntimeButton.title = isCopilotCli
        ? ""
        : "Uses GitHub Copilot subscription sign-in (no API key required).";
    }

    var refreshModelsButton = byId("btnRefreshModels");
    if (refreshModelsButton) {
      refreshModelsButton.disabled = isApplying || transport === "native";
      refreshModelsButton.title = isApplying
        ? "Runtime switch in progress."
        : (transport === "native" ? "Switch to Copilot or compatible runtime to refresh models." : "");
    }

    var applyRuntimeButton = byId("btnApplyLocalProvider");
    if (applyRuntimeButton) {
      applyRuntimeButton.disabled = isApplying;
      applyRuntimeButton.textContent = isApplying ? "Applying Runtime..." : "Apply Runtime";
    }

    var advancedShouldBeOpen = isRuntimeAdvancedOpen();
    if (transport === "native" && !advancedShouldBeOpen) {
      setRuntimeAdvancedOpen(false);
    } else {
      setRuntimeAdvancedOpen(advancedShouldBeOpen);
    }

    var transportSelect = byId("optLocalTransport");
    if (transportSelect) {
      transportSelect.value = transport;
      syncCustomSelect(transportSelect);
    }

    var providerPresetSelect = byId("optLocalProviderPreset");
    if (providerPresetSelect) {
      providerPresetSelect.value = compatiblePreset;
      if (providerPresetSelect.value !== compatiblePreset) {
        providerPresetSelect.value = "manual";
      }
      syncCustomSelect(providerPresetSelect);
      providerPresetSelect.disabled = !isCompatible || isApplying;
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

    var btnPresetOpenAI = byId("btnLocalPresetOpenAI");
    if (btnPresetOpenAI) {
      btnPresetOpenAI.hidden = !isCompatible;
      btnPresetOpenAI.disabled = isApplying;
    }

    var btnPresetAzureOpenAI = byId("btnLocalPresetAzureOpenAI");
    if (btnPresetAzureOpenAI) {
      btnPresetAzureOpenAI.hidden = !isCompatible;
      btnPresetAzureOpenAI.disabled = isApplying;
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

    var authModeRow = byId("optLocalAuthModeRow");
    if (authModeRow) {
      authModeRow.hidden = !isCompatible;
    }
    var authModeSelect = byId("optLocalAuthMode");
    if (authModeSelect) {
      authModeSelect.value = openAIAuthMode;
      if (authModeSelect.value !== openAIAuthMode) {
        authModeSelect.value = "bearer";
      }
      syncCustomSelect(authModeSelect);
      authModeSelect.disabled = !isCompatible || isApplying;
    }

    var basicUsernameRow = byId("optLocalBasicUsernameRow");
    if (basicUsernameRow) {
      basicUsernameRow.hidden = !isCompatible || openAIAuthMode !== "basic";
    }
    var basicPasswordRow = byId("optLocalBasicPasswordRow");
    if (basicPasswordRow) {
      basicPasswordRow.hidden = !isCompatible || openAIAuthMode !== "basic";
    }
    var basicUsernameInput = byId("optLocalBasicUsername");
    if (basicUsernameInput) {
      basicUsernameInput.value = openAIBasicUsername;
      basicUsernameInput.disabled = !isCompatible || openAIAuthMode !== "basic" || isApplying;
    }
    var basicPasswordInput = byId("optLocalBasicPassword");
    if (basicPasswordInput) {
      basicPasswordInput.disabled = !isCompatible || openAIAuthMode !== "basic" || isApplying;
      if (!isCompatible || openAIAuthMode !== "basic") {
        basicPasswordInput.value = "";
      }
    }

    var apiKeyHint = byId("optLocalApiKeyHint");
    if (apiKeyHint) {
      apiKeyHint.hidden = !isCompatible;
      if (isCompatible && openAIAuthMode === "basic") {
        apiKeyHint.textContent = "Bearer API key is ignored while auth mode is Basic.";
      } else if (isCompatible && copilotConnected) {
        apiKeyHint.textContent = "Required for Copilot endpoint. Use a GitHub OAuth app token or fine-grained PAT with Copilot Chat access.";
      } else {
        apiKeyHint.textContent = "Use Clear Saved API Key to remove the currently stored key.";
      }
    }

    var apiKeyInput = byId("optLocalApiKey");
    if (apiKeyInput) {
      apiKeyInput.disabled = !isCompatible || openAIAuthMode !== "bearer";
      if (!isCompatible) {
        apiKeyInput.value = "";
      }
    }

    var clearBasicAuthButton = byId("btnClearLocalBasicAuth");
    if (clearBasicAuthButton) {
      clearBasicAuthButton.disabled = !isCompatible || isApplying;
      clearBasicAuthButton.hidden = !isCompatible;
    }

    var authHint = byId("optLocalAuthHint");
    if (authHint) {
      authHint.hidden = !isCompatible;
      if (!isCompatible) {
        authHint.textContent = "";
      } else if (compatiblePreset === "anthropic-bridge" || compatiblePreset === "gemini-bridge") {
        authHint.textContent = "Subscription bridges are experimental. Configure the bridge endpoint and usually set auth mode to Basic.";
      } else if (compatiblePreset === "azure-openai") {
        authHint.textContent = "Azure OpenAI typically uses Bearer auth and deployment-specific base URLs.";
      } else if (compatiblePreset === "openai") {
        authHint.textContent = "OpenAI API uses Bearer auth. Enter API key and choose a model.";
      } else {
        authHint.textContent = "Compatible HTTP supports Bearer/API key or Basic auth.";
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
      function pushModelOption(modelName, labelPrefix, matchLabel, metadataLabel) {
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
        var suffix = normalizeModelText(metadataLabel || "");
        option.textContent = (labelPrefix ? (labelPrefix + " " + normalized) : normalized) + suffix;
        modelSelect.appendChild(option);
        selectableOptionCount++;
      }

      for (var r = 0; r < recents.length; r++) {
        pushModelOption(recents[r], "Recent:", recents[r], "");
      }
      for (var f = 0; f < favorites.length; f++) {
        pushModelOption(favorites[f], "Favorite:", favorites[f], "");
      }
      for (var i = 0; i < models.length; i++) {
        var item = models[i] || {};
        var modelName = normalizeModelText(item.model || item.Model || item.id || item.Id);
        if (!modelName) {
          continue;
        }
        var displayName = normalizeModelText(item.displayName || item.DisplayName);
        var metadataLabel = buildModelMetadataLabel(item);
        if (displayName && displayName.toLowerCase() !== modelName.toLowerCase()) {
          pushModelOption(modelName, displayName + ":", displayName, metadataLabel);
        } else {
          pushModelOption(modelName, "", modelName, metadataLabel);
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
    var showModelSelect = supportsModelCatalog && (hasSelectableModels || modelFilterQuery.length > 0);
    if (modelSelectRow) {
      modelSelectRow.hidden = !showModelSelect;
    }
    if (modelFilterRow) {
      modelFilterRow.hidden = !showModelSelect;
    }
    if (modelInputRow) {
      modelInputRow.hidden = false;
    }
    if (modelInput) {
      modelInput.disabled = isApplying;
    }

    var selectedModelInfo = null;
    if (model) {
      for (var sm = 0; sm < models.length; sm++) {
        var modelItem = models[sm] || {};
        var modelNameCandidate = normalizeModelText(modelItem.model || modelItem.Model || modelItem.id || modelItem.Id);
        if (modelNameCandidate.toLowerCase() === model.toLowerCase()) {
          selectedModelInfo = modelItem;
          break;
        }
      }
    }

    var manualHint = byId("optLocalModelManualHint");
    if (manualHint) {
      if (!showModelSelect) {
        manualHint.hidden = false;
        manualHint.textContent = "Type an exact model ID in Model and click Apply Runtime.";
      } else if (!model || !selectedModelInfo) {
        manualHint.hidden = false;
        manualHint.textContent = "Keep \"Manual model input\" selected, then type the exact model ID in the Model field above this list.";
      } else {
        manualHint.hidden = true;
        manualHint.textContent = "";
      }
    }

    var supportedReasoningEfforts = [];
    var reasoningSupport = resolveReasoningSupport(transport, compatiblePreset);
    var supportsReasoningControls = reasoningSupport.supported;
    var runtimeReasoningSupport = normalizeModelText(runtimeCapabilities.reasoningSupport || "");
    if (typeof runtimeCapabilities.supportsReasoningControls === "boolean") {
      supportsReasoningControls = runtimeCapabilities.supportsReasoningControls;
      if (runtimeReasoningSupport) {
        reasoningSupport = {
          supported: supportsReasoningControls,
          reason: runtimeReasoningSupport
        };
      }
    } else if (runtimeReasoningSupport && !reasoningSupport.reason) {
      reasoningSupport = {
        supported: reasoningSupport.supported,
        reason: runtimeReasoningSupport
      };
    }
    if (selectedModelInfo && Array.isArray(selectedModelInfo.supportedReasoningEfforts || selectedModelInfo.SupportedReasoningEfforts)) {
      var supported = selectedModelInfo.supportedReasoningEfforts || selectedModelInfo.SupportedReasoningEfforts;
      for (var sr = 0; sr < supported.length; sr++) {
        var effortItem = supported[sr] || {};
        var effortName = normalizeReasoningEffortValue(effortItem.reasoningEffort || effortItem.ReasoningEffort || effortItem.value || effortItem.Value);
        if (!effortName) {
          continue;
        }
        if (supportedReasoningEfforts.indexOf(effortName) >= 0) {
          continue;
        }
        supportedReasoningEfforts.push(effortName);
      }
    }

    var effortSelect = byId("optReasoningEffort");
    if (effortSelect) {
      var optionsEfforts = supportedReasoningEfforts.length > 0
        ? supportedReasoningEfforts
        : ["minimal", "low", "medium", "high", "xhigh"];
      var effortLabels = {
        minimal: "Minimal",
        low: "Low",
        medium: "Medium",
        high: "High",
        xhigh: "XHigh"
      };
      effortSelect.innerHTML = "";
      var providerDefaultOption = document.createElement("option");
      providerDefaultOption.value = "";
      providerDefaultOption.textContent = "Provider default";
      effortSelect.appendChild(providerDefaultOption);
      for (var oe = 0; oe < optionsEfforts.length; oe++) {
        var effort = optionsEfforts[oe];
        var effortOption = document.createElement("option");
        effortOption.value = effort;
        effortOption.textContent = effortLabels[effort] || effort;
        effortSelect.appendChild(effortOption);
      }
      effortSelect.value = reasoningEffort;
      if (effortSelect.value !== reasoningEffort) {
        effortSelect.value = "";
      }
      syncCustomSelect(effortSelect);
      effortSelect.disabled = isApplying || !supportsReasoningControls;
    }

    var summarySelect = byId("optReasoningSummary");
    if (summarySelect) {
      summarySelect.value = reasoningSummary;
      if (summarySelect.value !== reasoningSummary) {
        summarySelect.value = "";
      }
      syncCustomSelect(summarySelect);
      summarySelect.disabled = isApplying || !supportsReasoningControls;
    }

    var verbositySelect = byId("optTextVerbosity");
    if (verbositySelect) {
      verbositySelect.value = textVerbosity;
      if (verbositySelect.value !== textVerbosity) {
        verbositySelect.value = "";
      }
      syncCustomSelect(verbositySelect);
      verbositySelect.disabled = isApplying || !supportsReasoningControls;
    }

    var temperatureInput = byId("optTemperature");
    if (temperatureInput) {
      temperatureInput.value = temperatureText;
      temperatureInput.disabled = isApplying;
    }

    var reasoningHint = byId("optReasoningHint");
    if (reasoningHint) {
      var hintParts = ["Reasoning controls are provider/model dependent."];
      if (selectedModelInfo) {
        var modelDefaultEffort = normalizeReasoningEffortValue(
          selectedModelInfo.defaultReasoningEffort || selectedModelInfo.DefaultReasoningEffort || "");
        if (modelDefaultEffort) {
          hintParts.push("Model default effort: " + modelDefaultEffort + ".");
        }
      }
      if (supportedReasoningEfforts.length > 0) {
        hintParts.push("Supported efforts: " + supportedReasoningEfforts.join(", ") + ".");
      } else if (!supportsReasoningControls) {
        hintParts.push(reasoningSupport.reason || "Current provider profile does not expose reasoning controls.");
      } else {
        hintParts.push("Supported efforts were not reported by runtime metadata.");
      }
      reasoningHint.textContent = hintParts.join(" ");
    }
    renderRuntimeCapabilities({
      isApplying: isApplying,
      transport: transport,
      compatiblePreset: compatiblePreset,
      providerLabel: runtimeProviderLabel,
      supportsModelCatalog: supportsModelCatalog,
      supportsReasoningControls: supportsReasoningControls,
      reasoningSupportReason: reasoningSupport.reason || "",
      supportedReasoningEfforts: supportedReasoningEfforts,
      openAIAuthMode: openAIAuthMode,
      openAIBasicUsername: openAIBasicUsername,
      copilotConnected: copilotConnected,
      isNativeTransport: isNativeTransport,
      isCopilotCli: isCopilotCli,
      supportsLiveApply: runtimeCapabilities.supportsLiveApply,
      requiresProcessRestart: runtimeCapabilities.requiresProcessRestart,
      nativeAccountSlots: runtimeCapabilities.nativeAccountSlots,
      trackedAccounts: runtimeCapabilities.trackedAccounts,
      accountsWithRetrySignals: runtimeCapabilities.accountsWithRetrySignals,
      activeNativeAccountSlot: activeNativeAccountSlot,
      accountUsage: accountUsage
    });

    var usageTitle = byId("optAccountUsageTitle");
    var usageList = byId("optAccountUsageList");
    if (usageTitle) {
      usageTitle.hidden = accountUsage.length === 0;
    }
    if (usageList) {
      usageList.innerHTML = "";
      usageList.classList.add("options-usage-list");
      if (accountUsage.length === 0) {
        usageList.hidden = true;
      } else {
        usageList.hidden = false;
        var activeUsageKey = activeAccountUsage ? normalizeModelText(activeAccountUsage.key || "") : "";
        for (var usageIndex = 0; usageIndex < accountUsage.length; usageIndex++) {
          var usage = accountUsage[usageIndex] || {};
          var usageKey = normalizeModelText(usage.key || "");
          var usageItem = document.createElement("div");
          usageItem.className = "options-usage-item";
          if (activeUsageKey && usageKey && usageKey.toLowerCase() === activeUsageKey.toLowerCase()) {
            usageItem.classList.add("active");
          }

          var usageHead = document.createElement("div");
          usageHead.className = "options-usage-head";

          var usageLabel = document.createElement("div");
          usageLabel.className = "options-usage-label";
          usageLabel.textContent = normalizeModelText(usage.label || usage.key || "account");
          usageHead.appendChild(usageLabel);

          var usageTurns = Number(usage.turns);
          var turnsLabel = Number.isFinite(usageTurns) ? String(Math.max(0, Math.floor(usageTurns))) : "0";
          var usagePill = document.createElement("span");
          usagePill.className = "options-pill options-pill-category";
          usagePill.textContent = turnsLabel + " turns";
          usageHead.appendChild(usagePill);
          usageItem.appendChild(usageHead);

          var usageMetrics = document.createElement("div");
          usageMetrics.className = "options-usage-metrics";
          usageMetrics.textContent =
            "total " + formatTokenCount(usage.totalTokens)
            + " | prompt " + formatTokenCount(usage.promptTokens)
            + " | completion " + formatTokenCount(usage.completionTokens)
            + " | reasoning " + formatTokenCount(usage.reasoningTokens);
          usageItem.appendChild(usageMetrics);

          var usagePlanType = normalizeModelText(usage.planType || "");
          var usageUsedPercent = Number(usage.rateLimitUsedPercent);
          var usageWindowResetMinutes = Number(usage.rateLimitWindowResetMinutes);
          var usageMetaParts = [];
          if (usagePlanType) {
            usageMetaParts.push("plan " + usagePlanType);
          }
          if (Number.isFinite(usageUsedPercent) && usageUsedPercent >= 0) {
            usageMetaParts.push("window used " + Math.round(usageUsedPercent) + "%");
          }
          if (Number.isFinite(usageWindowResetMinutes) && usageWindowResetMinutes >= 0) {
            usageMetaParts.push("window reset " + formatRetryMinutes(usageWindowResetMinutes));
          }
          if (usage.rateLimitReached === true) {
            usageMetaParts.push("limit reached");
          }
          if (usage.codeReviewLimitReached === true) {
            usageMetaParts.push("code review limit reached");
          }
          if (usage.creditsUnlimited === true) {
            usageMetaParts.push("credits unlimited");
          } else if (typeof usage.creditsBalance === "number" && isFinite(usage.creditsBalance)) {
            usageMetaParts.push("credits " + usage.creditsBalance);
          }
          if (usageMetaParts.length > 0) {
            var usageMeta = document.createElement("div");
            usageMeta.className = "options-item-sub";
            usageMeta.textContent = usageMetaParts.join(" | ");
            usageItem.appendChild(usageMeta);
          }

          var retryAfterMinutes = Number(usage.retryAfterMinutes);
          if (Number.isFinite(retryAfterMinutes) && retryAfterMinutes >= 0) {
            var usageRetry = document.createElement("div");
            usageRetry.className = "options-usage-retry";
            var renewAt = formatUtcDateTime(usage.usageLimitRetryAfterUtc || "");
            usageRetry.setAttribute("data-retry-after-utc", normalizeModelText(usage.usageLimitRetryAfterUtc || ""));
            if (renewAt) {
              usageRetry.setAttribute("data-renew-at-text", renewAt);
            }
            usageRetry.textContent = "limit retry: " + formatRetryMinutes(retryAfterMinutes) + (renewAt ? " (" + renewAt + ")" : "");
            usageItem.appendChild(usageRetry);
          }

          usageList.appendChild(usageItem);
        }
      }
    }
    refreshAccountUsageRetryCountdowns();

    var stateNote = byId("optLocalModelsState");
    if (stateNote) {
      var parts = [];
      if (transport === "native") {
        parts.push("ChatGPT runtime active");
        if (models.length > 0) {
          parts.push(String(models.length) + " local models cached");
        }
      } else if (isCopilotCli) {
        parts.push("GitHub Copilot subscription runtime active");
        if (models.length > 0) {
          parts.push(String(models.length) + " models returned");
        } else {
          parts.push("No discovered models yet");
          parts.push("Use Sign In, then click Refresh Models");
        }
      } else {
        if (modelsEndpoint) {
          parts.push("model source: " + modelsEndpoint);
        }
        if (!copilotConnected && runtimeDetectedName) {
          parts.push("runtime probe: " + runtimeDetectedName + " reachable");
        } else if (!copilotConnected && runtimeDetectionHasRun && runtimeDetectionWarning) {
          parts.push(runtimeDetectionWarning);
        }
        if (models.length > 0) {
          parts.push(String(models.length) + " models returned");
          var metadataSummary = summarizeModelRuntimeMetadata(models);
          if (metadataSummary.hasMetadata) {
            if (metadataSummary.loadedCount > 0) {
              parts.push(String(metadataSummary.loadedCount) + " loaded");
            }
            if (metadataSummary.notLoadedCount > 0) {
              parts.push(String(metadataSummary.notLoadedCount) + " not-loaded");
            }
            if (metadataSummary.maxLoadedContext > 0) {
              parts.push("active ctx " + String(metadataSummary.maxLoadedContext));
            }
          }
          var cloudHostedCount = countCloudHostedModelNames(models);
          if (!copilotConnected && cloudHostedCount > 0 && cloudHostedCount >= Math.ceil(models.length * 0.6)) {
            parts.push("catalog looks cloud-hosted; load a local model in LM Studio to see local IDs");
          }
        } else {
          parts.push("No discovered models yet");
          if (lmStudioConnected) {
            parts.push("Load a model in LM Studio, then click Refresh Models");
          } else if (copilotConnected) {
            parts.push("Set a GitHub token in API key, then click Refresh Models");
          }
        }
      }
      if (reasoningEffort || reasoningSummary || textVerbosity || temperatureText) {
        parts.push(
          "reasoning: "
          + (reasoningEffort || "default")
          + ", summary: "
          + (reasoningSummary || "default")
          + ", verbosity: "
          + (textVerbosity || "default")
          + ", temperature: "
          + (temperatureText || "default"));
      }
      if (isCompatible) {
        var authText = "auth: " + openAIAuthMode;
        if (openAIAuthMode === "basic" && openAIBasicUsername) {
          authText += " (" + openAIBasicUsername + ")";
        }
        parts.push(authText);
      }
      if (activeAccountUsage) {
        var activeUsageLabel = normalizeModelText(activeAccountUsage.label || "");
        var activeUsageTotal = formatTokenCount(activeAccountUsage.totalTokens);
        var activeUsagePrompt = formatTokenCount(activeAccountUsage.promptTokens);
        var activeUsageCompletion = formatTokenCount(activeAccountUsage.completionTokens);
        var activeTurns = Number(activeAccountUsage.turns);
        var activeTurnsText = Number.isFinite(activeTurns) ? String(Math.max(0, Math.floor(activeTurns))) : "0";
        parts.push(
          "usage (" + (activeUsageLabel || "active account") + "): "
          + activeUsageTotal + " total"
          + " (prompt " + activeUsagePrompt + ", completion " + activeUsageCompletion + ")"
          + ", turns " + activeTurnsText);
        var retryAfterMinutes = Number(activeAccountUsage.retryAfterMinutes);
        if (Number.isFinite(retryAfterMinutes) && retryAfterMinutes >= 0) {
          parts.push("limit retry: " + formatRetryMinutes(retryAfterMinutes));
        }
        var activeWindowResetMinutes = Number(activeAccountUsage.rateLimitWindowResetMinutes);
        if (Number.isFinite(activeWindowResetMinutes) && activeWindowResetMinutes >= 0) {
          parts.push("window reset: " + formatRetryMinutes(activeWindowResetMinutes));
        }
        var activeUsedPercent = Number(activeAccountUsage.rateLimitUsedPercent);
        if (Number.isFinite(activeUsedPercent) && activeUsedPercent >= 0) {
          parts.push("window used: " + Math.round(activeUsedPercent) + "%");
        }
        var activePlanType = normalizeModelText(activeAccountUsage.planType || "");
        if (activePlanType) {
          parts.push("plan: " + activePlanType);
        }
        if (activeAccountUsage.creditsUnlimited === true) {
          parts.push("credits: unlimited");
        } else if (typeof activeAccountUsage.creditsBalance === "number" && isFinite(activeAccountUsage.creditsBalance)) {
          parts.push("credits: " + activeAccountUsage.creditsBalance);
        }
      } else if (transport === "native" && authenticatedAccountId) {
        parts.push("usage: no token metrics reported yet for this account");
      }
      if (transport === "native") {
        var selectedAccount = selectedSlotAccountId || openAIAccountId || authenticatedAccountId || "(unassigned)";
        parts.push("native slot " + String(activeNativeAccountSlot) + ": " + selectedAccount);
      }
      if (accountUsage.length > 1) {
        var accountLabels = [];
        for (var au = 0; au < accountUsage.length && accountLabels.length < 3; au++) {
          var item = accountUsage[au] || {};
          var label = normalizeModelText(item.label || item.key || "");
          if (label) {
            accountLabels.push(label);
          }
        }
        if (accountLabels.length > 0) {
          parts.push("tracked accounts: " + accountLabels.join(", "));
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

  var unexpectedExportVisualThemeModes = Object.create(null);

  function reportUnexpectedExportVisualThemeMode(value) {
    if (!value || unexpectedExportVisualThemeModes[value]) {
      return;
    }
    unexpectedExportVisualThemeModes[value] = true;
    if (typeof console !== "undefined" && typeof console.debug === "function") {
      console.debug("[ix.chat] unexpected export visual theme mode:", value);
    }
  }

  function normalizeExportVisualThemeMode(value) {
    // Keep alias/default parity with ExportPreferencesContract.NormalizeVisualThemeMode (C# host).
    var normalized = String(value || "").trim().toLowerCase();
    switch (normalized) {
      case "print_friendly":
      case "print":
      case "light":
        return "print_friendly";
      case "":
      case "preserve_ui_theme":
      case "preserve":
      case "theme":
        return "preserve_ui_theme";
      default:
        reportUnexpectedExportVisualThemeMode(normalized);
        return "preserve_ui_theme";
    }
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
    var visualThemeMode = normalizeExportVisualThemeMode(exportPrefs.visualThemeMode);
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

    var visualThemeModeSelect = byId("optExportVisualThemeMode");
    if (visualThemeModeSelect) {
      visualThemeModeSelect.value = visualThemeMode;
      syncCustomSelect(visualThemeModeSelect);
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
      if (state.accountId) {
        parts.push("Account: " + String(state.accountId) + ".");
      }

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
        var totalTokens = Number(metrics.totalTokens);
        var tokenText = Number.isFinite(totalTokens) && totalTokens > 0
          ? ", tokens " + formatTokenCount(totalTokens)
          : "";
        parts.push("Last turn: " + outcome + " in " + durationText + ", tools " + callsText + queueWaitText + tokenText + ".");
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
