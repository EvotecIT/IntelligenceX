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

    var order = [];
    var packs = state.options.packs || [];
    for (var p = 0; p < packs.length; p++) {
      var pid = normalizePackId(packs[p].id);
      if (groups[pid]) {
        order.push(pid);
      }
    }

    var remaining = Object.keys(groups).filter(function(k) { return order.indexOf(k) < 0; });
    remaining.sort();
    order = order.concat(remaining);

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

      var title = document.createElement("span");
      title.className = "options-accordion-title";
      title.textContent = packDisplayName(currentPackId);
      summary.appendChild(title);

      var sourceKind = packSourceKind(currentPackId);
      var sourceBadge = document.createElement("span");
      sourceBadge.className = "options-pill options-pill-source options-pill-source-" + sourceKind;
      sourceBadge.textContent = packSourceLabel(sourceKind);
      summary.appendChild(sourceBadge);

      var meta = document.createElement("span");
      meta.className = "options-accordion-meta";
      meta.textContent = String(groupTools.length) + " tools";
      summary.appendChild(meta);

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
      summary.appendChild(pill);

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
      summary.appendChild(packToggle);

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
    var turnTimeoutInput = byId("optAutonomyTurnTimeout");
    var toolTimeoutInput = byId("optAutonomyToolTimeout");
    var weightedRoutingSelect = byId("optAutonomyWeightedRouting");
    var maxCandidatesInput = byId("optAutonomyMaxCandidates");

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
      if (autonomy.parallelTools === true) {
        parallelSelect.value = "on";
      } else if (autonomy.parallelTools === false) {
        parallelSelect.value = "off";
      } else {
        parallelSelect.value = "default";
      }
      syncCustomSelect(parallelSelect);
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
      stateLabel.textContent = normalizeBool(state.debugMode)
        ? "Engine debug is enabled."
        : "Engine debug is disabled.";
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
  }

