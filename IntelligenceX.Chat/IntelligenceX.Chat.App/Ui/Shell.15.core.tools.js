  var MAX_NATIVE_ACCOUNT_SLOTS = 32;

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

  function normalizeToolExecutionScope(value) {
    var normalized = String(value || "").trim().toLowerCase();
    if (normalized === "remote_only" || normalized === "remote-only") {
      return "remote_only";
    }
    if (normalized === "local_or_remote" || normalized === "local-or-remote" || normalized === "local_and_remote") {
      return "local_or_remote";
    }
    if (normalized === "local_only" || normalized === "local-only") {
      return "local_only";
    }
    return "";
  }

  function resolveToolExecutionScope(tool) {
    var normalized = normalizeToolExecutionScope(tool && tool.executionScope);
    if (normalized) {
      return normalized;
    }

    var supportsLocalExecution = !tool || !Object.prototype.hasOwnProperty.call(tool, "supportsLocalExecution")
      ? true
      : normalizeBool(tool.supportsLocalExecution);
    var supportsRemoteExecution = !!tool && normalizeBool(tool.supportsRemoteExecution);

    if (supportsRemoteExecution && !supportsLocalExecution) {
      return "remote_only";
    }
    if (supportsRemoteExecution) {
      return "local_or_remote";
    }
    return "local_only";
  }

  function resolveToolExecutionBadgeModel(tool) {
    var scope = resolveToolExecutionScope(tool);
    var isExecutionAware = !!tool && normalizeBool(tool.isExecutionAware);
    var contractId = tool && tool.executionContractId ? String(tool.executionContractId).trim() : "";
    var label = "Local only";
    var status = "local";
    var terms = ["execution", scope, "local local-only local only on-box onbox localhost"];

    if (scope === "remote_only") {
      label = "Remote only";
      status = "remote";
      terms = ["execution", scope, "remote remote-only remote only remote-ready remote ready remote-capable remote capable"];
    } else if (scope === "local_or_remote") {
      label = "Local + remote";
      status = "mixed";
      terms = ["execution", scope, "local remote local-and-remote local or remote mixed dual-scope remote-ready remote ready remote-capable remote capable"];
    }

    terms.push(isExecutionAware
      ? "execution-aware declared execution-contract structured"
      : "execution-inferred inferred execution-metadata");

    if (contractId) {
      terms.push(contractId);
    }

    return {
      scope: scope,
      label: label,
      status: status,
      isExecutionAware: isExecutionAware,
      contractId: contractId,
      searchText: terms.join(" "),
      note: isExecutionAware
        ? (contractId
            ? ("Declared execution locality (" + contractId + ").")
            : "Declared execution locality.")
        : "Execution locality inferred from current tool metadata."
    };
  }

  function summarizePackExecutionLocality(tools) {
    if (!Array.isArray(tools) || tools.length === 0) {
      return null;
    }

    var localOnly = 0;
    var remoteOnly = 0;
    var localOrRemote = 0;
    for (var i = 0; i < tools.length; i++) {
      var execution = resolveToolExecutionBadgeModel(tools[i]);
      if (!execution) {
        continue;
      }

      if (execution.scope === "remote_only") {
        remoteOnly++;
      } else if (execution.scope === "local_or_remote") {
        localOrRemote++;
      } else {
        localOnly++;
      }
    }

    var remoteCapable = remoteOnly + localOrRemote;
    var label = "Local-only";
    var status = "local";
    var searchText = "local-only local only";
    if (remoteCapable > 0 && localOnly > 0) {
      label = "Mixed locality";
      status = "mixed";
      searchText = "mixed locality local-and-remote local only remote ready remote-ready";
    } else if (remoteCapable > 0) {
      label = "Remote-ready";
      status = "remote";
      searchText = "remote-ready remote ready remote-capable remote capable";
    }

    return {
      label: label,
      status: status,
      searchText: searchText,
      summary: label + " across " + String(tools.length) + (tools.length === 1 ? " tool" : " tools")
        + " (local-only " + String(localOnly)
        + ", remote-only " + String(remoteOnly)
        + ", local+remote " + String(localOrRemote) + ")."
    };
  }

  function formatExecutionScopeLabel(executionScope) {
    var normalized = String(executionScope || "").trim().toLowerCase();
    if (normalized === "local_or_remote") {
      return "Local or remote";
    }
    if (normalized === "remote_only") {
      return "Remote only";
    }
    return "Local only";
  }

  function appendToolContractSummary(item, label, values) {
    if (!item || !label || !Array.isArray(values) || values.length === 0) {
      return;
    }

    var detail = document.createElement("div");
    detail.className = "options-item-sub";
    detail.textContent = label + ": " + values.join(", ");
    item.appendChild(detail);
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

    var execution = resolveToolExecutionBadgeModel(tool);
    if (execution) {
      var executionPill = document.createElement("span");
      executionPill.className = "options-pill options-pill-execution options-pill-execution-" + execution.status;
      executionPill.textContent = execution.label;
      executionPill.title = execution.note;
      header.appendChild(executionPill);
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

    var identity = document.createElement("div");
    identity.className = "options-item-code";
    identity.textContent = tool.packName
      ? (tool.name + " · " + tool.packName)
      : tool.name;
    item.appendChild(identity);

    if (tool.description) {
      var sub = document.createElement("div");
      sub.className = "options-item-sub";
      sub.textContent = tool.description;
      item.appendChild(sub);
    }

    if (execution) {
      var executionMeta = document.createElement("div");
      executionMeta.className = "options-tool-routing";

      var executionDetail = document.createElement("span");
      executionDetail.className = "options-tool-routing-reason";
      executionDetail.textContent = execution.note;
      executionMeta.appendChild(executionDetail);

      item.appendChild(executionMeta);
    }

    if (packUnavailable) {
      var unavailable = document.createElement("div");
      unavailable.className = "options-item-warning";
      unavailable.textContent = packUnavailableReason
        ? ("Unavailable: " + packUnavailableReason)
        : "Unavailable in current runtime.";
      item.appendChild(unavailable);
    }

    var contractPills = [];
    if (tool.isPackInfoTool) {
      contractPills.push("Pack info");
    }
    if (tool.isEnvironmentDiscoverTool) {
      contractPills.push("Environment");
    }
    if (tool.supportsRemoteHostTargeting || String(tool.executionScope || "").toLowerCase() === "local_or_remote") {
      contractPills.push("Remote");
    }
    if (tool.supportsTargetScoping) {
      contractPills.push("Target scope");
    }
    if (tool.isSetupAware) {
      contractPills.push("Setup");
    }
    if (tool.isHandoffAware) {
      contractPills.push("Handoff");
    }
    if (tool.isRecoveryAware) {
      contractPills.push("Recovery");
    }

    if (contractPills.length > 0) {
      var contractRow = document.createElement("div");
      contractRow.className = "options-tag-row";
      for (var cp = 0; cp < contractPills.length; cp++) {
        var contractPill = document.createElement("span");
        contractPill.className = "options-pill options-pill-category";
        contractPill.textContent = contractPills[cp];
        contractRow.appendChild(contractPill);
      }
      item.appendChild(contractRow);
    }

    var toolDetails = null;
    var toolDetailsBody = null;

    function ensureToolDetailsBody() {
      if (toolDetailsBody) {
        return toolDetailsBody;
      }

      toolDetails = document.createElement("details");
      toolDetails.className = "options-tool-params options-tool-details";

      var summary = document.createElement("summary");
      summary.textContent = "Tool details";
      toolDetails.appendChild(summary);

      toolDetailsBody = document.createElement("div");
      toolDetailsBody.className = "options-tool-params-body";
      toolDetails.appendChild(toolDetailsBody);
      item.appendChild(toolDetails);
      return toolDetailsBody;
    }

    function appendToolDetailsLine(label, values) {
      if (!label) {
        return;
      }

      if (Array.isArray(values)) {
        if (values.length === 0) {
          return;
        }
        values = values.join(", ");
      }

      var normalized = String(values || "").trim();
      if (!normalized) {
        return;
      }

      var detail = document.createElement("div");
      detail.className = "options-item-sub";
      detail.textContent = label + ": " + normalized;
      ensureToolDetailsBody().appendChild(detail);
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
      ensureToolDetailsBody().appendChild(tagsRow);
    }

    appendToolDetailsLine("Execution", formatExecutionScopeLabel(tool.executionScope));
    if (tool.supportsTransientRetry && Number(tool.maxRetryAttempts || 0) > 0) {
      appendToolDetailsLine("Retry policy", "Retry " + String(tool.maxRetryAttempts));
    }
    appendToolDetailsLine("Target arguments", Array.isArray(tool.targetScopeArguments) ? tool.targetScopeArguments : []);
    appendToolDetailsLine("Remote arguments", Array.isArray(tool.remoteHostArguments) ? tool.remoteHostArguments : []);
    appendToolDetailsLine("Required arguments", Array.isArray(tool.requiredArguments) ? tool.requiredArguments : []);
    if (tool.isSetupAware && tool.setupToolName) {
      appendToolDetailsLine("Setup helper", String(tool.setupToolName));
    }
    appendToolDetailsLine("Handoff packs", Array.isArray(tool.handoffTargetPackIds) ? tool.handoffTargetPackIds : []);
    appendToolDetailsLine("Handoff tools", Array.isArray(tool.handoffTargetToolNames) ? tool.handoffTargetToolNames : []);
    appendToolDetailsLine("Recovery tools", Array.isArray(tool.recoveryToolNames) ? tool.recoveryToolNames : []);

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

      ensureToolDetailsBody().appendChild(routing);
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
    var packMetadata = packId ? findPackById(packId) : null;
    var runtimeDisabledByConfig = !!packId && packDisabledByRuntimeConfiguration(packId);
    if (packId && !packMetadata && !packIsAvailable(packId) && !runtimeDisabledByConfig) {
      return;
    }

    if (packId && packMetadata) {
      post("set_pack_enabled", { packId: packId, enabled: enabled });
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

  function normalizeToolLocalityFilter(value) {
    var normalized = String(value || "").trim().toLowerCase();
    if (normalized === "remote_ready" || normalized === "remote-ready") {
      return "remote_ready";
    }
    if (normalized === "local_only" || normalized === "local-only") {
      return "local_only";
    }
    if (normalized === "dual_scope" || normalized === "dual-scope" || normalized === "mixed") {
      return "dual_scope";
    }
    return "all";
  }

  function renderToolLocalityQuickFilters() {
    var active = normalizeToolLocalityFilter(state.options && state.options.toolLocalityFilter);
    var host = byId("optToolLocalityFilters");
    if (host) {
      host.setAttribute("data-active-locality-filter", active);
    }

    var buttons = host ? host.querySelectorAll("[data-locality-filter]") : [];
    for (var i = 0; i < buttons.length; i++) {
      var button = buttons[i];
      var value = normalizeToolLocalityFilter(button.getAttribute("data-locality-filter"));
      var isActive = value === active;
      button.classList.toggle("active", isActive);
      button.setAttribute("aria-pressed", isActive ? "true" : "false");
    }
  }

  function toolMatchesLocalityFilter(tool, localityFilter) {
    if (localityFilter === "all") {
      return true;
    }

    var execution = resolveToolExecutionBadgeModel(tool);
    if (!execution) {
      return localityFilter !== "dual_scope";
    }

    if (localityFilter === "remote_ready") {
      return execution.scope === "remote_only" || execution.scope === "local_or_remote";
    }
    if (localityFilter === "local_only") {
      return execution.scope === "local_only";
    }
    if (localityFilter === "dual_scope") {
      return execution.scope === "local_or_remote";
    }

    return true;
  }

  function toolMatchesFilter(tool, filter) {
    if (!filter) {
      return true;
    }

    var toolPackId = inferPackIdFromTool(tool);
    var execution = resolveToolExecutionBadgeModel(tool);
    var haystack = [
      tool.displayName || "",
      tool.name || "",
      tool.description || "",
      tool.category || "",
      tool.packId || "",
      tool.packName || "",
      tool.packDescription || "",
      tool.isPackInfoTool ? "pack-info pack info orientation" : "",
      tool.isEnvironmentDiscoverTool ? "environment discover preflight bootstrap" : "",
      tool.executionScope || "",
      tool.supportsTargetScoping ? "target scope targeting" : "",
      tool.supportsRemoteHostTargeting ? "remote host remote targeting" : "",
      tool.isSetupAware ? "setup setup-aware bootstrap" : "",
      tool.setupToolName || "",
      tool.isHandoffAware ? "handoff pivot continuation" : "",
      Array.isArray(tool.handoffTargetPackIds) ? tool.handoffTargetPackIds.join(" ") : "",
      Array.isArray(tool.handoffTargetToolNames) ? tool.handoffTargetToolNames.join(" ") : "",
      tool.isRecoveryAware ? "recovery retry remediation" : "",
      tool.supportsTransientRetry ? "transient retry" : "",
      Array.isArray(tool.recoveryToolNames) ? tool.recoveryToolNames.join(" ") : "",
      Array.isArray(tool.targetScopeArguments) ? tool.targetScopeArguments.join(" ") : "",
      Array.isArray(tool.remoteHostArguments) ? tool.remoteHostArguments.join(" ") : "",
      Array.isArray(tool.requiredArguments) ? tool.requiredArguments.join(" ") : "",
      tool.routingConfidence || "",
      tool.routingReason || "",
      typeof tool.routingScore === "number" ? String(tool.routingScore) : "",
      packSourceLabel(packSourceKind(toolPackId)),
      packDisabledReason(toolPackId),
      tool.executionScope || "",
      tool.executionContractId || "",
      normalizeBool(tool.isExecutionAware) ? "execution-aware" : "execution-inferred",
      normalizeBool(tool.supportsLocalExecution) ? "supports-local-execution local-capable" : "",
      normalizeBool(tool.supportsRemoteExecution) ? "supports-remote-execution remote-capable remote-ready" : "",
      execution ? execution.label : "",
      execution ? execution.note : "",
      execution ? execution.searchText : "",
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
    renderToolLocalityQuickFilters();

    var allTools = state.options.tools || [];
    var packTotalCounts = {};
    for (var at = 0; at < allTools.length; at++) {
      var allToolPackId = inferPackIdFromTool(allTools[at]);
      packTotalCounts[allToolPackId] = (packTotalCounts[allToolPackId] || 0) + 1;
    }
    var tools = allTools.slice();
    var filter = normalizeToolFilter(state.options.toolFilter);
    var localityFilter = normalizeToolLocalityFilter(state.options.toolLocalityFilter);
    if (localityFilter !== "all") {
      tools = tools.filter(function(tool) { return toolMatchesLocalityFilter(tool, localityFilter); });
    }
    if (filter) {
      tools = tools.filter(function(tool) { return toolMatchesFilter(tool, filter); });
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

      var autonomySummary = packAutonomySummary(packId);
      var executionSummary = summarizePackExecutionLocality(groups[packId] || []);
      var autonomyHaystack = [];
      if (autonomySummary) {
        if (Number(autonomySummary.remoteCapableTools || 0) > 0) {
          autonomyHaystack.push("remote remote-capable");
        }
        if (Number(autonomySummary.setupAwareTools || 0) > 0) {
          autonomyHaystack.push("setup bootstrap preflight");
        }
        if (Number(autonomySummary.handoffAwareTools || 0) > 0) {
          autonomyHaystack.push("handoff pivot");
        }
        if (Number(autonomySummary.recoveryAwareTools || 0) > 0) {
          autonomyHaystack.push("recovery retry");
        }
        if (Number(autonomySummary.crossPackHandoffTools || 0) > 0) {
          autonomyHaystack.push("cross-pack cross pack");
        }
        autonomyHaystack.push((autonomySummary.remoteCapableToolNames || []).join(" "));
        autonomyHaystack.push((autonomySummary.setupAwareToolNames || []).join(" "));
        autonomyHaystack.push((autonomySummary.handoffAwareToolNames || []).join(" "));
        autonomyHaystack.push((autonomySummary.recoveryAwareToolNames || []).join(" "));
        autonomyHaystack.push((autonomySummary.crossPackHandoffToolNames || []).join(" "));
        autonomyHaystack.push((autonomySummary.crossPackTargetPacks || []).join(" "));
      }
      if (executionSummary) {
        autonomyHaystack.push(executionSummary.label);
        autonomyHaystack.push(executionSummary.summary);
        autonomyHaystack.push(executionSummary.searchText);
      }

      var haystack = [
        packId || "",
        packDisplayName(packId),
        packDescription(packId),
        packSourceLabel(packSourceKind(packId)),
        packDisabledReason(packId),
        autonomyHaystack.join(" ")
      ].join(" ").toLowerCase();

      return haystack.indexOf(query) >= 0;
    }

    var packs = state.options.packs || [];
    var startupDiagnostics = state.options && state.options.startupDiagnostics;
    var startupCacheDiag = startupDiagnostics && startupDiagnostics.cache && typeof startupDiagnostics.cache === "object"
      ? startupDiagnostics.cache
      : null;
    var showPersistedPreviewNotice = !filter
      && normalizeBool(state.options && state.options.toolsLoading)
      && startupCacheDiag
      && startupBootstrapCacheModeIsPersistedPreview(startupCacheDiag.mode);
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
      var startupContext = typeof parseStartupStatusContext === "function"
        ? parseStartupStatusContext(String(state.status || ""))
        : null;
      var metadataSyncDiagnostics = startupDiagnostics && startupDiagnostics.metadataSync && typeof startupDiagnostics.metadataSync === "object"
        ? startupDiagnostics.metadataSync
        : null;
      var metadataSyncActiveByDiagnostics = metadataSyncDiagnostics
        && (metadataSyncDiagnostics.inProgress === true || metadataSyncDiagnostics.queued === true);
      var startupStillSyncingTools = !filter && (
        state.options.toolsLoading === true
        || metadataSyncActiveByDiagnostics
        || (startupContext && (startupContext.phase === "startup_metadata_sync" || startupContext.phase === "startup_auth_wait"))
      );
      if (startupStillSyncingTools) {
        var waitingForSignIn = startupContext && startupContext.phase === "startup_auth_wait";
        var pendingCatalogCount = Math.max(0, Math.floor(state.options.toolsCatalogPendingCount || 0));
        var title = waitingForSignIn
          ? "Waiting for sign-in before loading tools..."
          : "Syncing tool packs in background...";
        var detail = waitingForSignIn
          ? "Runtime is connected. Finish sign-in and tool metadata will appear automatically."
          : pendingCatalogCount > 0
            ? ("Runtime is usable; " + String(pendingCatalogCount) + (pendingCatalogCount === 1 ? " tool definition is" : " tool definitions are") + " still arriving.")
            : "Runtime is usable; tool metadata is still arriving.";
        toolsEl.innerHTML = "<div class='options-item'><div class='options-item-title'>"
          + escapeHtml(title)
          + "</div><div class='options-item-sub'>"
          + escapeHtml(detail)
          + "</div></div>";
        return;
      }

      if (filter) {
        toolsEl.innerHTML = "<div class='options-item'><div class='options-item-title'>No tools match filter</div></div>";
      } else {
        toolsEl.innerHTML = "<div class='options-item'><div class='options-item-title'>No tools registered</div></div>";
      }
      return;
    }

    if (showPersistedPreviewNotice) {
      var previewNotice = document.createElement("div");
      previewNotice.className = "options-item";

      var previewTitle = document.createElement("div");
      previewTitle.className = "options-item-title";
      previewTitle.textContent = "Showing startup preview";
      previewNotice.appendChild(previewTitle);

      var previewDetail = document.createElement("div");
      previewDetail.className = "options-item-sub";
      previewDetail.textContent = "Final tool catalog is still loading, so pack and tool counts may increase automatically.";
      previewNotice.appendChild(previewDetail);

      toolsEl.appendChild(previewNotice);
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
      var sourceKind = packSourceKind(currentPackId);
      var hasPackUnavailableReason = typeof packUnavailableReason === "string"
        ? packUnavailableReason.trim().length > 0
        : !!packUnavailableReason;
      // Closed-source packs without registered tools are usually metadata-only hints, not live payloads.
      var hideMetadataOnlyClosedSourcePack = groupTools.length === 0
        && !filter
        && localityFilter === "all"
        && sourceKind === "closed_source"
        && !packRuntimeDisabledByConfig
        && !hasPackUnavailableReason;
      if (hideMetadataOnlyClosedSourcePack) {
        continue;
      }

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

      var autonomySummary = packAutonomySummary(currentPackId);
      var executionSummary = summarizePackExecutionLocality(groupTools);
      var sourceBadge = document.createElement("span");
      sourceBadge.className = "options-pill options-pill-source options-pill-source-" + sourceKind;
      sourceBadge.textContent = packSourceLabel(sourceKind);
      sourceBadge.title = packSourceHint(sourceKind);
      summaryRight.appendChild(sourceBadge);

      if (executionSummary) {
        var executionBadge = document.createElement("span");
        executionBadge.className = "options-pill options-pill-execution options-pill-execution-" + executionSummary.status;
        executionBadge.textContent = executionSummary.label;
        executionBadge.title = executionSummary.summary;
        summaryRight.appendChild(executionBadge);
      }

      var meta = document.createElement("span");
      meta.className = "options-accordion-meta";
      var totalToolsForPack = Math.max(groupTools.length, Number(packTotalCounts[currentPackId] || 0));
      if (localityFilter !== "all" && totalToolsForPack > groupTools.length) {
        meta.textContent = String(groupTools.length) + " of " + String(totalToolsForPack) + " tools";
      } else {
        meta.textContent = String(groupTools.length) + (groupTools.length === 1 ? " tool" : " tools");
      }
      if (autonomySummary && Number(autonomySummary.remoteCapableTools || 0) > 0) {
        meta.textContent += " • remote " + String(autonomySummary.remoteCapableTools || 0);
      }
      if (autonomySummary || executionSummary) {
        meta.title = [
          autonomySummary ? packAutonomySummaryText(currentPackId) : "",
          executionSummary ? executionSummary.summary : ""
        ].filter(function(value) { return !!value; }).join(" | ");
      }
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
      var packMetadata = findPackById(currentPackId);
      var packEnabledByRuntime = !packMetadata || normalizeBool(packMetadata.enabled);
      var packActivation = packActivationState(currentPackId);
      var packDeferred = packActivation === "deferred";
      var packCanLoadOnDemand = packCanActivateOnDemand(currentPackId);
      var packHasTools = actionableTools.length > 0;
      var isPackLoaded = packAvailable && packEnabledByRuntime && packHasTools;

      var pill = document.createElement("span");
      pill.className = "options-pill" + (isPackLoaded && packAvailable ? "" : " off");
      if (packUnavailable) {
        pill.textContent = "Unavailable";
      } else if (!packEnabledByRuntime || packActivation === "disabled" || (!packHasTools && packRuntimeDisabledByConfig)) {
        pill.textContent = "Disabled";
      } else if (!packHasTools && packDeferred && packCanLoadOnDemand) {
        pill.textContent = "On-demand";
      } else if (!packHasTools) {
        pill.textContent = "No tools";
      } else {
        pill.textContent = "Loaded";
      }
      if (packHasTools && enabledCount < actionableTools.length) {
        pill.title = String(enabledCount) + " of " + String(actionableTools.length) + " tools are currently enabled in this pack.";
      }
      summaryRight.appendChild(pill);

      if (packMetadata) {
        var packToggle = document.createElement("input");
        packToggle.type = "checkbox";
        packToggle.className = "options-toggle options-toggle-pack";
        packToggle.checked = packEnabledByRuntime;
        packToggle.disabled = false;
        packToggle.setAttribute("aria-label", (packEnabledByRuntime ? "Disable pack " : "Enable pack ") + packDisplayName(currentPackId));
        if (packUnavailable && packUnavailableReason) {
          packToggle.title = packUnavailableReason;
        } else if (!packHasTools && packDeferred && packCanLoadOnDemand && packEnabledByRuntime) {
          packToggle.title = "Pack is enabled and can load its tool definitions on demand.";
        } else if (packRuntimeDisabledByConfig) {
          packToggle.title = "Pack is disabled by runtime configuration. Enable it to load this pack live.";
        } else {
          packToggle.title = "Enable or disable this runtime pack.";
        }
        (function(packIdForToggle, groupToolsForToggle) {
          packToggle.addEventListener("change", function(e) {
            e.stopPropagation();
            setPackEnabled(packIdForToggle, groupToolsForToggle, e.target.checked);
            renderTools();
          });
        })(currentPackId, groupTools);
        summaryRight.appendChild(packToggle);
      } else {
        var packAction = document.createElement("button");
        packAction.type = "button";
        packAction.className = "options-btn options-btn-sm options-btn-ghost options-pack-action";
        packAction.disabled = packUnavailable || (!packHasTools && !packRuntimeDisabledByConfig);
        packAction.textContent = allEnabled ? "Disable all" : "Enable all";
        packAction.setAttribute("aria-label", packAction.textContent + " " + packDisplayName(currentPackId));
        if (packUnavailable && packUnavailableReason) {
          packAction.title = packUnavailableReason;
        } else if (packRuntimeDisabledByConfig) {
          packAction.title = "Pack is disabled by runtime configuration. Enable it to load this pack live.";
        } else if (!packHasTools) {
          packAction.title = "No tools are currently registered for this pack.";
        } else {
          packAction.title = "Turn every registered tool in this pack on or off.";
        }
        (function(packIdForToggle, groupToolsForToggle, nextEnabled) {
          packAction.addEventListener("click", function(e) {
            e.preventDefault();
            e.stopPropagation();
            setPackEnabled(packIdForToggle, groupToolsForToggle, nextEnabled);
            renderTools();
          });
        })(currentPackId, groupTools, !allEnabled);
        summaryRight.appendChild(packAction);
      }
      summary.appendChild(summaryRight);

      details.appendChild(summary);

      var body = document.createElement("div");
      body.className = "options-accordion-body";
      if (autonomySummary) {
        var autonomyCard = document.createElement("div");
        autonomyCard.className = "options-item";

        var autonomyTitle = document.createElement("div");
        autonomyTitle.className = "options-item-title";
        autonomyTitle.textContent = "Autonomy readiness";
        autonomyCard.appendChild(autonomyTitle);

        var autonomyDetail = document.createElement("div");
        autonomyDetail.className = "options-item-sub";
        autonomyDetail.textContent = packAutonomySummaryText(currentPackId);
        autonomyCard.appendChild(autonomyDetail);

        if (Array.isArray(autonomySummary.crossPackTargetPacks) && autonomySummary.crossPackTargetPacks.length > 0) {
          var autonomyPivots = document.createElement("div");
          autonomyPivots.className = "options-item-sub";
          autonomyPivots.textContent = "Cross-pack pivots: " + autonomySummary.crossPackTargetPacks.join(", ");
          autonomyCard.appendChild(autonomyPivots);
        }

        body.appendChild(autonomyCard);
      }
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

  function runtimePanelViewStorageKey() {
    return "ixchat.runtime.panel.view";
  }

  function normalizeRuntimePanelView(value) {
    var normalized = String(value || "").trim().toLowerCase();
    if (normalized === "provider" || normalized === "model" || normalized === "usage") {
      return normalized;
    }
    if (normalized === "all") {
      return "all";
    }
    return "provider";
  }

  function getRuntimePanelView() {
    return normalizeRuntimePanelView(readStorage(runtimePanelViewStorageKey()));
  }

  function setRuntimePanelView(value) {
    var normalized = normalizeRuntimePanelView(value);
    writeStorage(runtimePanelViewStorageKey(), normalized);
    return normalized;
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
    if (normalizedTransport === "copilot-cli") {
      return {
        supported: false,
        reason: "GitHub Copilot subscription runtime currently does not expose reasoning controls."
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
      return "Anthropic subscription bridge runtime";
    }
    if (preset === "gemini-bridge") {
      return "Gemini subscription bridge runtime";
    }
    if (copilotConnected) {
      return "GitHub Copilot runtime";
    }
    return "Compatible HTTP runtime";
  }

  function normalizeBridgeSessionState(value) {
    var normalized = normalizeModelText(value || "").toLowerCase();
    if (normalized === "connecting" || normalized === "auth-failed" || normalized === "ready") {
      return normalized;
    }
    return "";
  }

  function resolveBridgeSessionValue(state) {
    if (state === "ready") {
      return "Ready";
    }
    if (state === "auth-failed") {
      return "Authentication failed";
    }
    return "Connecting";
  }

  function resolveBridgeSessionStatus(state) {
    if (state === "ready") {
      return "supported";
    }
    if (state === "auth-failed") {
      return "unavailable";
    }
    return "limited";
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
    var isBridgePreset = data.isBridgePreset === true;
    var bridgeAccountIdentity = normalizeModelText(data.bridgeAccountIdentity || "");
    var bridgeSessionState = normalizeBridgeSessionState(data.bridgeSessionState || "");
    var bridgeSessionDetail = normalizeModelText(data.bridgeSessionDetail || "");
    var executionLocality = data.executionLocality && typeof data.executionLocality === "object"
      ? data.executionLocality
      : {};
    var executionLocalityMode = normalizeExecutionLocalityMode(executionLocality.mode || "");
    var executionLocalitySummary = normalizeModelText(executionLocality.summary || "");
    var executionLocalityLabel = resolveExecutionLocalityLabel(executionLocalityMode);
    var executionLocalityStatus = resolveExecutionLocalityStatus(executionLocalityMode);
    var executionLocalityNote = resolveExecutionLocalityNote(executionLocality);
    if (!isBridgePreset) {
      var preset = String(data.compatiblePreset || "").trim().toLowerCase();
      isBridgePreset = preset === "anthropic-bridge" || preset === "gemini-bridge";
    }

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

    appendRuntimeCapabilityRow(
      listEl,
      "Execution locality",
      executionLocalityStatus,
      executionLocalityLabel,
      executionLocalityNote || executionLocalitySummary || "Execution locality is still loading from the live tool catalog.");

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
        authStatus = openAIAuthMode === "none" ? "limited" : "supported";
        if (openAIAuthMode === "basic") {
          authValue = "Bridge credentials (Basic)";
        }
        authNote = openAIAuthMode === "basic"
          ? "Use bridge login/email and secret/token for your subscription-backed bridge session."
          : "Subscription bridge endpoints typically expect Basic credentials (login + secret/token).";
      }
      appendRuntimeCapabilityRow(listEl, "Authentication", authStatus, authValue, authNote);
    }

    if (isBridgePreset) {
      var bridgeNote = bridgeSessionDetail;
      if (!bridgeNote) {
        if (bridgeSessionState === "ready") {
          bridgeNote = bridgeAccountIdentity
            ? ("Bridge session ready for " + bridgeAccountIdentity + ".")
            : "Bridge session ready.";
        } else if (bridgeSessionState === "auth-failed") {
          bridgeNote = "Bridge authentication failed. Update login/email + secret/token and apply again.";
        } else {
          bridgeNote = bridgeAccountIdentity
            ? ("Connecting to bridge runtime for " + bridgeAccountIdentity + "...")
            : "Connecting to bridge runtime...";
        }
      }
      appendRuntimeCapabilityRow(
        listEl,
        "Bridge session",
        resolveBridgeSessionStatus(bridgeSessionState),
        resolveBridgeSessionValue(bridgeSessionState),
        bridgeNote);
    }

    var nativeAccountSlots = Number(data.nativeAccountSlots);
    if (!Number.isFinite(nativeAccountSlots) || nativeAccountSlots <= 0) {
      nativeAccountSlots = 3;
    }
    nativeAccountSlots = Math.max(1, Math.min(MAX_NATIVE_ACCOUNT_SLOTS, Math.floor(nativeAccountSlots)));

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

  function normalizeExecutionLocalityMode(value) {
    var normalized = normalizeModelText(value || "").toLowerCase();
    if (normalized === "mixed" || normalized === "remote_ready" || normalized === "local_only" || normalized === "execution_aware_unspecified") {
      return normalized;
    }
    return "unknown";
  }

  function resolveExecutionLocalityLabel(mode) {
    if (mode === "mixed") {
      return "Mixed locality";
    }
    if (mode === "remote_ready") {
      return "Remote-ready";
    }
    if (mode === "local_only") {
      return "Local-only";
    }
    if (mode === "execution_aware_unspecified") {
      return "Execution-aware (scope still settling)";
    }
    return "Unknown";
  }

  function resolveExecutionLocalityStatus(mode) {
    if (mode === "remote_ready" || mode === "mixed") {
      return "supported";
    }
    if (mode === "local_only" || mode === "execution_aware_unspecified") {
      return "limited";
    }
    return "unavailable";
  }

  function resolveExecutionLocalityNote(executionLocality) {
    if (!executionLocality || typeof executionLocality !== "object") {
      return "";
    }

    var summary = normalizeModelText(executionLocality.summary || "");
    var executionAwareTools = Number(executionLocality.executionAwareTools);
    var localOnlyTools = Number(executionLocality.localOnlyTools);
    var remoteOnlyTools = Number(executionLocality.remoteOnlyTools);
    var localOrRemoteTools = Number(executionLocality.localOrRemoteTools);
    var localOnlyPackIds = Array.isArray(executionLocality.localOnlyPackIds) ? executionLocality.localOnlyPackIds : [];
    var remoteCapablePackIds = Array.isArray(executionLocality.remoteCapablePackIds) ? executionLocality.remoteCapablePackIds : [];
    var noteParts = [];
    if (summary) {
      noteParts.push(summary);
    }

    if (Number.isFinite(executionAwareTools) && executionAwareTools > 0) {
      noteParts.push("Execution-aware tools: " + String(Math.max(0, Math.floor(executionAwareTools))) + ".");
    }
    if (Number.isFinite(localOnlyTools) && localOnlyTools > 0) {
      noteParts.push("Local-only tools: " + String(Math.max(0, Math.floor(localOnlyTools))) + ".");
    }
    if (Number.isFinite(remoteOnlyTools) && remoteOnlyTools > 0) {
      noteParts.push("Remote-only tools: " + String(Math.max(0, Math.floor(remoteOnlyTools))) + ".");
    }
    if (Number.isFinite(localOrRemoteTools) && localOrRemoteTools > 0) {
      noteParts.push("Local-or-remote tools: " + String(Math.max(0, Math.floor(localOrRemoteTools))) + ".");
    }

    if (remoteCapablePackIds.length > 0) {
      noteParts.push("Remote-ready packs: " + remoteCapablePackIds.join(", ") + ".");
    }
    if (localOnlyPackIds.length > 0) {
      noteParts.push("Local-only packs: " + localOnlyPackIds.join(", ") + ".");
    }

    return noteParts.join(" ");
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
    var runtimePanelView = getRuntimePanelView();
    var runtimePanelViewSelect = byId("optRuntimePanelView");
    if (runtimePanelViewSelect) {
      runtimePanelView = setRuntimePanelView(runtimePanelView);
      runtimePanelViewSelect.value = runtimePanelView;
      syncCustomSelect(runtimePanelViewSelect);
    }
    var showProviderPanel = runtimePanelView === "all" || runtimePanelView === "provider";
    var showModelPanel = runtimePanelView === "all" || runtimePanelView === "model";
    var showUsagePanel = runtimePanelView === "all" || runtimePanelView === "usage";
    var runtimeCapabilities = local.runtimeCapabilities && typeof local.runtimeCapabilities === "object"
      ? local.runtimeCapabilities
      : {};
    var isApplying = local.isApplying === true;
    var turnBusy = state.sending === true || state.cancelRequested === true;
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
    var isBridgePreset = runtimeCapabilities.isBridgePreset === true
      || compatiblePreset === "anthropic-bridge"
      || compatiblePreset === "gemini-bridge";
    var bridgeAccountIdentity = normalizeModelText(runtimeCapabilities.bridgeAccountIdentity || "");
    var bridgeSessionState = normalizeBridgeSessionState(runtimeCapabilities.bridgeSessionState || "");
    var bridgeSessionDetail = normalizeModelText(runtimeCapabilities.bridgeSessionDetail || "");
    var runtimeProviderLabel = normalizeModelText(runtimeCapabilities.providerLabel || "");
    var executionLocality = runtimeCapabilities.executionLocality && typeof runtimeCapabilities.executionLocality === "object"
      ? runtimeCapabilities.executionLocality
      : {};
    var executionLocalityMode = normalizeExecutionLocalityMode(executionLocality.mode || "");
    var executionLocalityLabel = resolveExecutionLocalityLabel(executionLocalityMode);
    var modelsEndpoint = normalizeModelText(local.modelsEndpoint || "");
    var model = normalizeModelText(local.model || "");
    var nativeAccountSlots = Array.isArray(local.nativeAccountSlots) ? local.nativeAccountSlots : [];
    var maxNativeAccountSlots = Number(runtimeCapabilities.nativeAccountSlots);
    if (!Number.isFinite(maxNativeAccountSlots) || maxNativeAccountSlots <= 0) {
      maxNativeAccountSlots = nativeAccountSlots.length;
    }
    if (!Number.isFinite(maxNativeAccountSlots) || maxNativeAccountSlots <= 0) {
      maxNativeAccountSlots = 3;
    }
    maxNativeAccountSlots = Math.max(1, Math.min(MAX_NATIVE_ACCOUNT_SLOTS, Math.floor(maxNativeAccountSlots)));
    var openAIAccountId = normalizeModelText(local.openAIAccountId || "");
    var activeNativeAccountSlot = Number(local.activeNativeAccountSlot);
    if (!Number.isFinite(activeNativeAccountSlot)) {
      activeNativeAccountSlot = 1;
    } else {
      activeNativeAccountSlot = Math.floor(activeNativeAccountSlot);
    }
    if (activeNativeAccountSlot < 1) {
      activeNativeAccountSlot = 1;
    } else if (activeNativeAccountSlot > maxNativeAccountSlots) {
      activeNativeAccountSlot = maxNativeAccountSlots;
    }
    var reasoningEffort = normalizeReasoningEffortValue(local.reasoningEffort || "");
    var reasoningSummary = normalizeReasoningSummaryValue(local.reasoningSummary || "");
    var textVerbosity = normalizeTextVerbosityValue(local.textVerbosity || "");
    var temperatureText = normalizeTemperatureText(local.temperature);
    var models = Array.isArray(local.models) ? local.models : [];
    var authenticatedAccountId = normalizeModelText(local.authenticatedAccountId || "");
    var accountUsage = Array.isArray(local.accountUsage) ? local.accountUsage : [];
    var activeAccountUsage = local.activeAccountUsage && typeof local.activeAccountUsage === "object"
      ? local.activeAccountUsage
      : null;
    var warning = normalizeModelText(local.warning || "");
    var isStale = local.isStale === true;
    var profileSaved = local.profileSaved === true;
    var runtimeApply = local.runtimeApply && typeof local.runtimeApply === "object"
      ? local.runtimeApply
      : {};
    var hasPendingRuntimeDraft = typeof hasPendingLocalProviderChanges === "function"
      ? hasPendingLocalProviderChanges()
      : false;
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
        runtimeSummary.textContent = "Current: GitHub Copilot subscription runtime (CLI transport). Tools: " + executionLocalityLabel + ".";
      } else if (isCompatible) {
        var endpoint = baseUrl ? baseUrl : "(base URL not set)";
        var providerLabel = runtimeProviderLabel
          ? runtimeProviderLabel
          : resolveRuntimeProviderLabel(transport, compatiblePreset, copilotConnected);
        runtimeSummary.textContent = "Current: " + providerLabel + " via " + endpoint + ". Tools: " + executionLocalityLabel + ".";
      } else {
        runtimeSummary.textContent = "Current: ChatGPT runtime (OpenAI native). Tools: " + executionLocalityLabel + ".";
      }
    }

    var runtimeAuthHint = byId("optRuntimeAuthHint");
    if (runtimeAuthHint) {
      if (isCopilotCli) {
        runtimeAuthHint.textContent = "Copilot subscription runtime uses GitHub Copilot sign-in. API key is not used in this mode.";
      } else if (copilotConnected) {
        runtimeAuthHint.textContent = "Copilot runtime uses a GitHub token in API key. ChatGPT sign-in remains separate.";
      } else if (isCompatible && isBridgePreset) {
        if (bridgeSessionState === "auth-failed") {
          runtimeAuthHint.textContent = bridgeSessionDetail
            || "Bridge authentication failed. Update login/email + secret/token and apply runtime again.";
        } else if (bridgeSessionState === "ready") {
          runtimeAuthHint.textContent = bridgeSessionDetail
            || (bridgeAccountIdentity
              ? ("Bridge session ready for " + bridgeAccountIdentity + ".")
              : "Bridge session ready.");
        } else {
          runtimeAuthHint.textContent = bridgeSessionDetail
            || (bridgeAccountIdentity
              ? ("Connecting bridge session for " + bridgeAccountIdentity + "... ChatGPT sign-in remains separate.")
              : "Connecting bridge session... ChatGPT sign-in remains separate.");
        }
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
        runtimeText += " | Slot " + String(activeNativeAccountSlot) + "/" + String(maxNativeAccountSlots);
      } else if (isBridgePreset) {
        var bridgeText = resolveBridgeSessionValue(bridgeSessionState);
        if (bridgeAccountIdentity) {
          bridgeText += " (" + bridgeAccountIdentity + ")";
        }
        runtimeText += " | Bridge: " + bridgeText;
      }
      runtimeText += " | Tools: " + executionLocalityLabel;
      runtimeBadge.textContent = runtimeText;
    }

    var simpleHint = byId("optLocalSimpleHint");
    if (simpleHint) {
      if (isApplying) {
        if (normalizeModelText(runtimeApply.stage || "").toLowerCase() === "queued") {
          simpleHint.textContent = "Runtime switch queued. Current apply will finish first, then latest settings are applied.";
        } else {
          simpleHint.textContent = "Applying runtime settings. Please wait while the runtime updates.";
        }
      } else if (hasPendingRuntimeDraft) {
        simpleHint.textContent = "Runtime edits are pending. Click Apply Runtime to commit provider/model changes.";
      } else if (transport === "native") {
        simpleHint.textContent = "ChatGPT runtime is active. Model list below shows ChatGPT catalog; switch to LM Studio runtime for local models.";
      } else if (isCopilotCli) {
        simpleHint.textContent = "Copilot subscription runtime is active. Use Sign In to authenticate your GitHub Copilot account.";
      } else if (isCompatible && isBridgePreset && bridgeSessionState === "auth-failed") {
        simpleHint.textContent = bridgeSessionDetail || "Bridge authentication failed. Update login/email + secret/token and click Apply Runtime again.";
      } else if (isCompatible && isBridgePreset && bridgeSessionState === "ready") {
        simpleHint.textContent = bridgeSessionDetail || "Bridge runtime is active and ready.";
      } else if (isCompatible && isBridgePreset) {
        simpleHint.textContent = bridgeSessionDetail || "Bridge runtime is connecting. Model list will refresh once the handshake completes.";
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

    var runtimePanelHint = byId("optRuntimePanelHint");
    if (runtimePanelHint) {
      if (runtimePanelView === "provider") {
        runtimePanelHint.textContent = "Showing provider setup and advanced endpoint controls.";
      } else if (runtimePanelView === "model") {
        runtimePanelHint.textContent = "Showing model selection and reasoning controls.";
      } else if (runtimePanelView === "usage") {
        runtimePanelHint.textContent = "Showing account slots and usage/limit tracking.";
      } else {
        runtimePanelHint.textContent = "Showing every runtime section.";
      }
    }

    var runtimeApplyProgress = byId("optRuntimeApplyProgress");
    if (runtimeApplyProgress) {
      var applyStage = normalizeModelText(runtimeApply.stage || "").toLowerCase();
      var applyDetail = normalizeModelText(runtimeApply.detail || "");
      var applyUpdatedLocal = normalizeModelText(runtimeApply.updatedLocal || "");
      var applyActive = runtimeApply.isActive === true || isApplying;
      var showApplyProgress = applyActive
        || applyStage === "failed"
        || applyStage === "completed";

      runtimeApplyProgress.hidden = !showApplyProgress;
      runtimeApplyProgress.classList.toggle("active", applyActive);
      runtimeApplyProgress.classList.toggle("error", applyStage === "failed");

      if (showApplyProgress) {
        var label = applyDetail;
        if (!label) {
          if (applyActive) {
            if (applyStage === "queued") {
              label = "Runtime switch queued. Latest settings will apply next.";
            } else {
              label = "Applying runtime settings...";
            }
          } else if (applyStage === "completed") {
            label = "Runtime settings applied.";
          } else if (applyStage === "failed") {
            label = "Runtime settings failed to apply.";
          }
        }

        if (!applyActive && applyUpdatedLocal) {
          label += " (" + applyUpdatedLocal + ")";
        }

        runtimeApplyProgress.textContent = label;
      } else {
        runtimeApplyProgress.textContent = "";
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

    function resolveNativeAccountEmail(accountId) {
      var normalizedAccountId = normalizeModelText(accountId || "");
      if (!normalizedAccountId) {
        return "";
      }
      var expectedKey = "native:" + normalizedAccountId.toLowerCase();
      for (var i = 0; i < accountUsage.length; i++) {
        var usage = accountUsage[i] || {};
        var usageKey = normalizeModelText(usage.key || "").toLowerCase();
        if (usageKey !== expectedKey) {
          continue;
        }

        return normalizeModelText(usage.email || "");
      }

      return "";
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
      for (var slotIndex = 1; slotIndex <= maxNativeAccountSlots; slotIndex++) {
        var slotState = resolveNativeSlotState(slotIndex) || {};
        var slotAccountId = normalizeModelText(slotState.accountId || "");
        var slotAccountEmail = resolveNativeAccountEmail(slotAccountId);
        var slotLabel = "Slot " + String(slotIndex);
        if (slotAccountId) {
          slotLabel += " | " + slotAccountId;
          if (slotAccountEmail) {
            slotLabel += " | " + slotAccountEmail;
          }
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
    var selectedSlotAccountEmail = resolveNativeAccountEmail(selectedSlotAccountId);
    if (!selectedSlotAccountId) {
      selectedSlotAccountId = openAIAccountId;
      selectedSlotAccountEmail = resolveNativeAccountEmail(selectedSlotAccountId);
    }
    if (nativeAccountIdInput) {
      nativeAccountIdInput.value = selectedSlotAccountId;
      nativeAccountIdInput.disabled = !isNativeTransport || isApplying;
    }
    if (nativeAccountHint) {
      var hintParts = ["Select a slot to switch accounts quickly."];
      hintParts.push("Available slots: " + String(maxNativeAccountSlots) + ".");
      if (selectedSlotAccountId) {
        var selectedSlotAccountText = selectedSlotAccountEmail
          ? (selectedSlotAccountId + " (" + selectedSlotAccountEmail + ")")
          : selectedSlotAccountId;
        hintParts.push("Selected slot account: " + selectedSlotAccountText + ".");
      }
      if (authenticatedAccountId) {
        var authenticatedAccountEmail = resolveNativeAccountEmail(authenticatedAccountId);
        var authenticatedAccountText = authenticatedAccountEmail
          ? (authenticatedAccountId + " (" + authenticatedAccountEmail + ")")
          : authenticatedAccountId;
        hintParts.push("Authenticated now: " + authenticatedAccountText + ".");
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
      useOpenAiRuntimeButton.classList.toggle("options-btn-ghost", !isNative);
      useOpenAiRuntimeButton.disabled = turnBusy;
      useOpenAiRuntimeButton.title = turnBusy
        ? "Wait for the current turn to finish before switching runtime."
        : (isApplying
        ? "Apply in progress. Click to queue switch to ChatGPT runtime."
        : "");
    }

    var connectLmStudioButton = byId("btnConnectLmStudio");
    if (connectLmStudioButton) {
      connectLmStudioButton.textContent = lmStudioConnected ? "LM Studio Runtime Active" : "Use LM Studio Runtime";
      connectLmStudioButton.classList.toggle("options-btn-active", lmStudioConnected);
      connectLmStudioButton.classList.toggle("options-btn-ghost", !lmStudioConnected);
      connectLmStudioButton.disabled = turnBusy;
      connectLmStudioButton.title = turnBusy
        ? "Wait for the current turn to finish before switching runtime."
        : (isApplying
        ? "Apply in progress. Click to queue switch to LM Studio runtime."
        : (runtimeDetectionHasRun && !lmStudioAvailable && !lmStudioConnected
          ? "LM Studio was not detected. Start LM Studio and click Auto Detect Runtime in Advanced Runtime."
          : ""));
    }

    var useCopilotRuntimeButton = byId("btnUseCopilotRuntime");
    if (useCopilotRuntimeButton) {
      useCopilotRuntimeButton.textContent = isCopilotCli ? "Copilot Subscription Active" : "Use Copilot Subscription";
      useCopilotRuntimeButton.classList.toggle("options-btn-active", isCopilotCli);
      useCopilotRuntimeButton.classList.toggle("options-btn-ghost", !isCopilotCli);
      useCopilotRuntimeButton.disabled = turnBusy;
      useCopilotRuntimeButton.title = turnBusy
        ? "Wait for the current turn to finish before switching runtime."
        : (isCopilotCli
        ? ""
        : (isApplying
          ? "Apply in progress. Click to queue switch to Copilot subscription runtime."
          : "Uses GitHub Copilot subscription sign-in (no API key required)."));
    }

    var refreshModelsButton = byId("btnRefreshModels");
    if (refreshModelsButton) {
      refreshModelsButton.disabled = isApplying || turnBusy || transport === "native";
      refreshModelsButton.title = turnBusy
        ? "Wait for the current turn to finish before refreshing models."
        : (isApplying
        ? "Runtime switch in progress."
        : (transport === "native" ? "Switch to Copilot or compatible runtime to refresh models." : ""));
    }

    var applyRuntimeButton = byId("btnApplyLocalProvider");
    if (applyRuntimeButton) {
      applyRuntimeButton.disabled = isApplying || turnBusy;
      applyRuntimeButton.textContent = isApplying
        ? (applyStage === "queued" ? "Runtime Queued..." : "Applying Runtime...")
        : (hasPendingRuntimeDraft ? "Apply Runtime (Pending)" : "Apply Runtime");
      applyRuntimeButton.title = turnBusy
        ? "Wait for the current turn to finish before applying runtime changes."
        : (hasPendingRuntimeDraft
          ? "Unsaved runtime changes detected. Click to apply these provider/model edits."
          : "Manual field edits require Apply Runtime. Quick runtime buttons apply automatically.");
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

    var btnPresetAnthropicBridge = byId("btnLocalPresetAnthropicBridge");
    if (btnPresetAnthropicBridge) {
      btnPresetAnthropicBridge.hidden = !isCompatible;
      btnPresetAnthropicBridge.disabled = isApplying;
    }

    var btnPresetGeminiBridge = byId("btnLocalPresetGeminiBridge");
    if (btnPresetGeminiBridge) {
      btnPresetGeminiBridge.hidden = !isCompatible;
      btnPresetGeminiBridge.disabled = isApplying;
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
        authHint.textContent = "Subscription bridge runtime is active. Use bridge login/email + secret/token (usually Basic auth).";
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
      activeNativeAccountSlot: activeNativeAccountSlot,
      trackedAccounts: runtimeCapabilities.trackedAccounts,
      accountsWithRetrySignals: runtimeCapabilities.accountsWithRetrySignals,
      isBridgePreset: isBridgePreset,
      bridgeAccountIdentity: bridgeAccountIdentity,
      bridgeSessionState: bridgeSessionState,
      bridgeSessionDetail: bridgeSessionDetail,
      executionLocality: executionLocality,
      activeNativeAccountSlot: activeNativeAccountSlot,
      accountUsage: accountUsage
    });

    var usageTitle = byId("optAccountUsageTitle");
    var usageList = byId("optAccountUsageList");
    var clearTrackedAccountUsageButton = byId("btnClearTrackedAccountUsage");
    if (usageTitle) {
      usageTitle.hidden = accountUsage.length === 0;
    }
    if (clearTrackedAccountUsageButton) {
      clearTrackedAccountUsageButton.hidden = accountUsage.length === 0;
      clearTrackedAccountUsageButton.disabled = accountUsage.length === 0;
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
          var usageLabelText = normalizeModelText(usage.label || usage.key || "account");
          var usageEmail = normalizeModelText(usage.email || "");
          if (usageEmail && usageLabelText.toLowerCase().indexOf(usageEmail.toLowerCase()) < 0) {
            usageLabelText += " | " + usageEmail;
          }
          usageLabel.textContent = usageLabelText;
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

    function resetRuntimePanelManagedVisibility(id) {
      var element = byId(id);
      if (!element) {
        return;
      }
      if (element.dataset.ixRuntimePanelHidden === "1") {
        element.hidden = false;
        element.dataset.ixRuntimePanelHidden = "0";
      }
    }

    function mergeRuntimePanelVisibility(id, shouldShow) {
      var element = byId(id);
      if (!element) {
        return;
      }
      element.hidden = !shouldShow;
      element.dataset.ixRuntimePanelHidden = shouldShow ? "0" : "1";
    }

    resetRuntimePanelManagedVisibility("optNativeAccountSlotRow");
    resetRuntimePanelManagedVisibility("optNativeAccountIdRow");
    resetRuntimePanelManagedVisibility("optNativeAccountHint");
    resetRuntimePanelManagedVisibility("optAccountUsageTitle");
    resetRuntimePanelManagedVisibility("optAccountUsageList");
    resetRuntimePanelManagedVisibility("optRuntimeSectionAuthTitle");
    resetRuntimePanelManagedVisibility("optLocalModelInputRow");
    resetRuntimePanelManagedVisibility("optLocalModelSelectRow");
    resetRuntimePanelManagedVisibility("optLocalModelFilterRow");
    resetRuntimePanelManagedVisibility("optLocalModelManualHint");
    resetRuntimePanelManagedVisibility("optReasoningEffortRow");
    resetRuntimePanelManagedVisibility("optReasoningSummaryRow");
    resetRuntimePanelManagedVisibility("optTextVerbosityRow");
    resetRuntimePanelManagedVisibility("optTemperatureRow");
    resetRuntimePanelManagedVisibility("optReasoningHint");
    resetRuntimePanelManagedVisibility("optRuntimeCapabilitiesTitle");
    resetRuntimePanelManagedVisibility("optRuntimeCapabilities");
    resetRuntimePanelManagedVisibility("optLocalModelsState");
    resetRuntimePanelManagedVisibility("optRuntimeSectionModelTitle");
    resetRuntimePanelManagedVisibility("optRuntimeSectionCatalogTitle");
    resetRuntimePanelManagedVisibility("optRuntimeSectionCapabilitiesTitle");
    resetRuntimePanelManagedVisibility("optRuntimeSectionUsageTitle");
    resetRuntimePanelManagedVisibility("optLocalSimpleHint");
    resetRuntimePanelManagedVisibility("optLocalAdvancedArea");
    resetRuntimePanelManagedVisibility("optRuntimeSectionAdvancedTitle");

    mergeRuntimePanelVisibility("optNativeAccountSlotRow", showUsagePanel);
    mergeRuntimePanelVisibility("optNativeAccountIdRow", showUsagePanel);
    mergeRuntimePanelVisibility("optNativeAccountHint", showUsagePanel);
    mergeRuntimePanelVisibility("optAccountUsageTitle", showUsagePanel);
    mergeRuntimePanelVisibility("optAccountUsageList", showUsagePanel);
    mergeRuntimePanelVisibility("optRuntimeSectionAuthTitle", showUsagePanel);

    mergeRuntimePanelVisibility("optLocalModelInputRow", showModelPanel);
    mergeRuntimePanelVisibility("optLocalModelSelectRow", showModelPanel);
    mergeRuntimePanelVisibility("optLocalModelFilterRow", showModelPanel);
    mergeRuntimePanelVisibility("optLocalModelManualHint", showModelPanel);
    mergeRuntimePanelVisibility("optReasoningEffortRow", showModelPanel);
    mergeRuntimePanelVisibility("optReasoningSummaryRow", showModelPanel);
    mergeRuntimePanelVisibility("optTextVerbosityRow", showModelPanel);
    mergeRuntimePanelVisibility("optTemperatureRow", showModelPanel);
    mergeRuntimePanelVisibility("optReasoningHint", showModelPanel);
    mergeRuntimePanelVisibility("optRuntimeCapabilitiesTitle", showModelPanel);
    mergeRuntimePanelVisibility("optRuntimeCapabilities", showModelPanel);
    mergeRuntimePanelVisibility("optLocalModelsState", showModelPanel);
    mergeRuntimePanelVisibility("optRuntimeSectionModelTitle", showModelPanel);
    mergeRuntimePanelVisibility("optRuntimeSectionCatalogTitle", showModelPanel);
    mergeRuntimePanelVisibility("optRuntimeSectionCapabilitiesTitle", showModelPanel);
    mergeRuntimePanelVisibility("optRuntimeSectionUsageTitle", showUsagePanel);

    mergeRuntimePanelVisibility("optLocalSimpleHint", showProviderPanel);
    mergeRuntimePanelVisibility("optLocalAdvancedArea", showProviderPanel);
    mergeRuntimePanelVisibility("optRuntimeSectionAdvancedTitle", showProviderPanel);

    refreshAccountUsageRetryCountdowns();

    var stateNote = byId("optLocalModelsState");
    if (stateNote) {
      var parts = [];
      if (transport === "native") {
        parts.push("ChatGPT runtime active");
        if (models.length > 0) {
          parts.push(String(models.length) + " models returned by native catalog");
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

  function normalizeExportDocxVisualMaxWidthPx(value) {
    return normalizeDocxVisualMaxWidthPxContract(value);
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
    var docxVisualMaxWidthPx = normalizeExportDocxVisualMaxWidthPx(exportPrefs.docxVisualMaxWidthPx);
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

    var docxVisualMaxWidthInput = byId("optExportDocxVisualMaxWidthPx");
    if (docxVisualMaxWidthInput) {
      docxVisualMaxWidthInput.value = String(docxVisualMaxWidthPx);
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
      if (typeof updateDataViewQuickExportLabel === "function") {
        updateDataViewQuickExportLabel();
      } else {
        var label = exportFormatDisplayName(format);
        quickExportButton.textContent = "Quick " + label;
        quickExportButton.title = "Quick export using " + label + " and current save behavior";
      }
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
      proactiveModeToggle.checked = autonomy.proactiveMode === true;
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

    renderRuntimeScheduler();
  }

  function renderRuntimeScheduler() {
    var scheduler = state.options.runtimeScheduler && typeof state.options.runtimeScheduler === "object"
      ? state.options.runtimeScheduler
      : null;
    var schedulerScoped = state.options.runtimeSchedulerScoped && typeof state.options.runtimeSchedulerScoped === "object"
      ? state.options.runtimeSchedulerScoped
      : null;
    var schedulerGlobal = state.options.runtimeSchedulerGlobal && typeof state.options.runtimeSchedulerGlobal === "object"
      ? state.options.runtimeSchedulerGlobal
      : null;
    var schedulerOptionSource = schedulerGlobal || scheduler || schedulerScoped;
    var schedulerState = byId("optRuntimeSchedulerState");
    var schedulerKv = byId("optRuntimeSchedulerKv");
    var maintenanceList = byId("optRuntimeSchedulerMaintenanceList");
    var blockedPackList = byId("optRuntimeSchedulerBlockedPackList");
    var blockedThreadList = byId("optRuntimeSchedulerBlockedThreadList");
    var activityList = byId("optRuntimeSchedulerActivityList");
    var threadList = byId("optRuntimeSchedulerThreadList");
    var scopePackSelect = byId("optSchedulerScopePack");
    var scopeSelect = byId("optSchedulerScopeThread");
    var maintenancePackSelect = byId("optSchedulerMaintenancePackId");
    var maintenanceThreadSelect = byId("optSchedulerMaintenanceThreadId");
    var refreshButton = byId("btnSchedulerRefresh");
    var scopeTogglePackMuteButton = byId("btnSchedulerScopeTogglePackMute");
    var scopeTempPackMuteButton = byId("btnSchedulerScopeTempPackMute");
    var scopePackMuteUntilMaintenanceButton = byId("btnSchedulerScopePackMuteUntilMaintenance");
    var scopePackMuteUntilMaintenanceStartButton = byId("btnSchedulerScopePackMuteUntilMaintenanceStart");
    var clearPackBlocksButton = byId("btnSchedulerClearPackBlocks");
    var scopeToggleMuteButton = byId("btnSchedulerScopeToggleMute");
    var scopeTempMuteButton = byId("btnSchedulerScopeTempMute");
    var scopeThreadMuteUntilMaintenanceButton = byId("btnSchedulerScopeThreadMuteUntilMaintenance");
    var scopeThreadMuteUntilMaintenanceStartButton = byId("btnSchedulerScopeThreadMuteUntilMaintenanceStart");
    var clearThreadBlocksButton = byId("btnSchedulerClearThreadBlocks");
    var pauseButton = byId("btnSchedulerPause");
    var resumeButton = byId("btnSchedulerResume");
    var clearMaintenanceButton = byId("btnSchedulerClearMaintenance");
    var connected = normalizeBool(state.connected);
    var preferredScopeThreadId = scopeSelect && scopeSelect.value
      ? String(scopeSelect.value || "").trim()
      : (schedulerScoped ? String(schedulerScoped.scopeThreadId || "").trim() : "");

    if (refreshButton) {
      refreshButton.disabled = !connected;
    }
    if (pauseButton) {
      pauseButton.disabled = !connected;
    }
    if (resumeButton) {
      resumeButton.disabled = !connected || !(scheduler && scheduler.manualPauseActive === true);
    }
    if (clearMaintenanceButton) {
      clearMaintenanceButton.disabled = !connected || maintenanceWindows.length === 0;
    }

    if (schedulerKv) {
      schedulerKv.textContent = "";
    }
    if (maintenanceList) {
      maintenanceList.textContent = "";
    }
    if (blockedPackList) {
      blockedPackList.textContent = "";
    }
    if (blockedThreadList) {
      blockedThreadList.textContent = "";
    }
    if (activityList) {
      activityList.textContent = "";
    }
    if (threadList) {
      threadList.textContent = "";
    }

    function populateSchedulerSelect(select, emptyLabel, options, preferredValue) {
      if (!select) {
        return;
      }

      var previousValue = String(preferredValue == null ? select.value : preferredValue);
      select.innerHTML = "";

      var emptyOption = document.createElement("option");
      emptyOption.value = "";
      emptyOption.textContent = emptyLabel;
      select.appendChild(emptyOption);

      for (var oi = 0; oi < options.length; oi++) {
        var optionData = options[oi] || {};
        var optionValue = String(optionData.value || "").trim();
        if (!optionValue) {
          continue;
        }

        var option = document.createElement("option");
        option.value = optionValue;
        option.textContent = String(optionData.label || optionValue);
        select.appendChild(option);
      }

      select.value = previousValue;
      if (select.value !== previousValue) {
        select.value = "";
      }
      syncCustomSelect(select);
    }

    function buildSchedulerThreadOptions() {
      var seen = Object.create(null);
      var result = [];

      function addThreadOption(value, label) {
        var normalizedValue = String(value || "").trim();
        if (!normalizedValue) {
          return;
        }

        var key = normalizedValue.toLowerCase();
        if (seen[key]) {
          return;
        }
        seen[key] = true;
        result.push({
          value: normalizedValue,
          label: String(label || normalizedValue)
        });
      }

      var conversations = state.options && Array.isArray(state.options.conversations)
        ? state.options.conversations
        : [];
      for (var ci = 0; ci < conversations.length; ci++) {
        var conversation = conversations[ci] || {};
        var conversationThreadId = String(conversation.threadId || "").trim();
        if (!conversationThreadId) {
          continue;
        }
        var conversationTitle = String(conversation.title || conversation.id || conversationThreadId).trim();
        addThreadOption(conversationThreadId, conversationTitle + " (" + conversationThreadId + ")");
      }

      var threadSummaries = schedulerOptionSource && Array.isArray(schedulerOptionSource.threadSummaries)
        ? schedulerOptionSource.threadSummaries
        : [];
      for (var tsi = 0; tsi < threadSummaries.length; tsi++) {
        var summary = threadSummaries[tsi] || {};
        addThreadOption(summary.threadId, String(summary.threadId || "").trim());
      }

      var readyThreadIds = schedulerOptionSource && Array.isArray(schedulerOptionSource.readyThreadIds)
        ? schedulerOptionSource.readyThreadIds
        : [];
      for (var rti = 0; rti < readyThreadIds.length; rti++) {
        addThreadOption(readyThreadIds[rti], String(readyThreadIds[rti] || "").trim());
      }

      var runningThreadIds = schedulerOptionSource && Array.isArray(schedulerOptionSource.runningThreadIds)
        ? schedulerOptionSource.runningThreadIds
        : [];
      for (var rni = 0; rni < runningThreadIds.length; rni++) {
        addThreadOption(runningThreadIds[rni], String(runningThreadIds[rni] || "").trim());
      }

      var recentActivity = schedulerOptionSource && Array.isArray(schedulerOptionSource.recentActivity)
        ? schedulerOptionSource.recentActivity
        : [];
      for (var rai = 0; rai < recentActivity.length; rai++) {
        var activity = recentActivity[rai] || {};
        addThreadOption(activity.threadId, String(activity.threadId || "").trim());
      }

      return result;
    }

    function buildSchedulerPackOptions() {
      var seen = Object.create(null);
      var result = [];

      function addPackOption(value, label) {
        var normalizedValue = String(value || "").trim();
        if (!normalizedValue) {
          return;
        }

        var key = normalizedValue.toLowerCase();
        if (seen[key]) {
          return;
        }
        seen[key] = true;
        result.push({
          value: normalizedValue,
          label: String(label || normalizedValue)
        });
      }

      var packs = state.options && Array.isArray(state.options.packs)
        ? state.options.packs
        : [];
      for (var pi = 0; pi < packs.length; pi++) {
        var pack = packs[pi] || {};
        var packId = String(pack.id || "").trim();
        var packName = String(pack.name || packId).trim();
        addPackOption(packId, packName && packName !== packId ? (packName + " (" + packId + ")") : packId);
      }

      var configuredWindows = schedulerOptionSource && Array.isArray(schedulerOptionSource.maintenanceWindows)
        ? schedulerOptionSource.maintenanceWindows
        : [];
      for (var mwi = 0; mwi < configuredWindows.length; mwi++) {
        var maintenanceWindow = configuredWindows[mwi] || {};
        var scopedPackId = String(maintenanceWindow.packId || "").trim();
        if (scopedPackId) {
          addPackOption(scopedPackId, scopedPackId);
        }
      }

      return result;
    }

    populateSchedulerSelect(
      scopePackSelect,
      "Any pack",
      buildSchedulerPackOptions());
    populateSchedulerSelect(
      scopeSelect,
      "All tracked threads",
      buildSchedulerThreadOptions(),
      preferredScopeThreadId);
    populateSchedulerSelect(
      maintenancePackSelect,
      "Any pack",
      buildSchedulerPackOptions());
    populateSchedulerSelect(
      maintenanceThreadSelect,
      "Any tracked thread",
      buildSchedulerThreadOptions());

    var selectedScopeThreadId = scopeSelect
      ? String(scopeSelect.value || "").trim()
      : preferredScopeThreadId;
    var hasScopedSchedulerSelection = schedulerScoped
      && selectedScopeThreadId.length > 0
      && String(schedulerScoped.scopeThreadId || "").trim().toLowerCase() === selectedScopeThreadId.toLowerCase();
    if (hasScopedSchedulerSelection) {
      scheduler = schedulerScoped;
    }

    var maintenanceWindows = scheduler && Array.isArray(scheduler.maintenanceWindows)
      ? scheduler.maintenanceWindows
      : [];
    var activeMaintenanceSpecs = scheduler && Array.isArray(scheduler.activeMaintenanceWindowSpecs)
      ? scheduler.activeMaintenanceWindowSpecs
      : [];

    if (!scheduler) {
      if (schedulerState) {
        schedulerState.textContent = connected
          ? "Background scheduler status not loaded yet."
          : "Connect to the service to inspect scheduler runtime state.";
      }
      return;
    }

    if (schedulerState) {
      schedulerState.textContent = String(scheduler.statusSummary || "Background scheduler diagnostics available.");
    }

    function appendSchedulerKv(label, value) {
      if (!schedulerKv) {
        return;
      }
      var k = document.createElement("div");
      k.className = "options-k";
      k.textContent = label;
      var v = document.createElement("div");
      v.className = "options-v";
      v.textContent = value;
      schedulerKv.appendChild(k);
      schedulerKv.appendChild(v);
    }

    function normalizedCount(value) {
      var parsed = Number(value);
      if (!Number.isFinite(parsed) || parsed < 0) {
        return 0;
      }
      return Math.floor(parsed);
    }

    function normalizedThreadIdArray(value) {
      var items = Array.isArray(value) ? value : [];
      var result = [];
      var seen = Object.create(null);
      for (var i = 0; i < items.length; i++) {
        var text = String(items[i] || "").trim();
        if (!text) {
          continue;
        }

        var key = text.toLowerCase();
        if (seen[key]) {
          continue;
        }
        seen[key] = true;
        result.push(text);
      }
      return result;
    }

    function normalizedPackIdArray(value) {
      var items = Array.isArray(value) ? value : [];
      var result = [];
      var seen = Object.create(null);
      for (var i = 0; i < items.length; i++) {
        var text = String(items[i] || "").trim().toLowerCase();
        if (!text || seen[text]) {
          continue;
        }

        seen[text] = true;
        result.push(text);
      }
      return result;
    }

    function normalizedSchedulerSuppressionArray(value, isPack) {
      var items = Array.isArray(value) ? value : [];
      var result = [];
      var seen = Object.create(null);
      for (var i = 0; i < items.length; i++) {
        var item = items[i] || {};
        var rawId = String(item.id || "").trim();
        var normalizedId = isPack ? rawId.toLowerCase() : rawId;
        if (!normalizedId) {
          continue;
        }

        var key = isPack ? normalizedId : normalizedId.toLowerCase();
        if (seen[key]) {
          continue;
        }

        seen[key] = true;
        result.push({
          id: normalizedId,
          mode: String(item.mode || "").trim().toLowerCase(),
          temporary: item.temporary === true,
          expiresUtcTicks: Number(item.expiresUtcTicks) || 0
        });
      }
      return result;
    }

    function findSchedulerSuppression(entries, id, isPack) {
      var normalizedId = String(id || "").trim();
      if (!normalizedId) {
        return null;
      }

      var lookup = isPack ? normalizedId.toLowerCase() : normalizedId;
      for (var i = 0; i < entries.length; i++) {
        var entry = entries[i] || {};
        var entryId = isPack ? String(entry.id || "").trim().toLowerCase() : String(entry.id || "").trim();
        if (entryId === lookup) {
          return entry;
        }
      }

      return null;
    }

    function isSchedulerTemporarySuppression(entry) {
      return !!(entry && entry.temporary === true);
    }

    function formatSchedulerSuppressionMode(entry) {
      if (!entry || typeof entry !== "object") {
        return "";
      }

      if (entry.temporary === true) {
        return "temporary";
      }

      return "persistent";
    }

    function formatSchedulerUtcTicks(utcTicks) {
      var ticks = Number(utcTicks);
      if (!Number.isFinite(ticks) || ticks <= 0) {
        return "";
      }

      var unixMs = Math.floor((ticks - 621355968000000000) / 10000);
      if (!Number.isFinite(unixMs) || unixMs <= 0) {
        return "";
      }

      try {
        return new Date(unixMs).toLocaleString();
      } catch (err) {
        return "";
      }
    }

    function buildSchedulerSuppressionDescription(entry, noun) {
      if (!entry || typeof entry !== "object") {
        return "";
      }

      if (entry.temporary === true) {
        var expiresLabel = formatSchedulerUtcTicks(entry.expiresUtcTicks);
        return expiresLabel
          ? ("Temporary daemon suppression for this " + noun + " until " + expiresLabel + ".")
          : ("Temporary daemon suppression for this " + noun + ".");
      }

      if (String(entry.mode || "").trim().toLowerCase() === "persistent_default") {
        return "Persistent daemon suppression from startup/profile policy.";
      }

      return "Persistent daemon suppression from runtime operator policy.";
    }

    function isSchedulerThreadBlocked(threadId) {
      var normalizedThreadId = String(threadId || "").trim().toLowerCase();
      if (!normalizedThreadId) {
        return false;
      }

      for (var i = 0; i < blockedThreadIds.length; i++) {
        if (blockedThreadIds[i].toLowerCase() === normalizedThreadId) {
          return true;
        }
      }

      return false;
    }

    function isSchedulerPackBlocked(packId) {
      var normalizedPackId = String(packId || "").trim().toLowerCase();
      if (!normalizedPackId) {
        return false;
      }

      for (var i = 0; i < blockedPackIds.length; i++) {
        if (blockedPackIds[i] === normalizedPackId) {
          return true;
        }
      }

      return false;
    }

    function resolveSchedulerThreadLabel(threadId) {
      var normalizedThreadId = String(threadId || "").trim();
      if (!normalizedThreadId) {
        return "thread";
      }

      var conversations = state.options && Array.isArray(state.options.conversations)
        ? state.options.conversations
        : [];
      var lookupKey = normalizedThreadId.toLowerCase();
      for (var i = 0; i < conversations.length; i++) {
        var conversation = conversations[i] || {};
        if (String(conversation.threadId || "").trim().toLowerCase() !== lookupKey) {
          continue;
        }

        var label = String(conversation.title || conversation.id || normalizedThreadId).trim();
        if (label && label !== normalizedThreadId) {
          return label + " (" + normalizedThreadId + ")";
        }
      }

      return normalizedThreadId;
    }

    function resolveSchedulerPackLabel(packId) {
      var normalizedPackId = String(packId || "").trim().toLowerCase();
      if (!normalizedPackId) {
        return "pack";
      }

      var packs = state.options && Array.isArray(state.options.packs)
        ? state.options.packs
        : [];
      for (var i = 0; i < packs.length; i++) {
        var pack = packs[i] || {};
        if (String(pack.id || "").trim().toLowerCase() !== normalizedPackId) {
          continue;
        }

        var packName = String(pack.name || "").trim();
        if (packName && packName.toLowerCase() !== normalizedPackId) {
          return packName + " (" + normalizedPackId + ")";
        }
      }

      return normalizedPackId;
    }

    function findSchedulerToolByName(toolName) {
      var normalizedToolName = String(toolName || "").trim();
      if (!normalizedToolName) {
        return null;
      }

      var tools = state.options && Array.isArray(state.options.tools)
        ? state.options.tools
        : [];
      for (var i = 0; i < tools.length; i++) {
        var tool = tools[i] || {};
        if (String(tool.name || "").trim() === normalizedToolName) {
          return tool;
        }
      }

      return null;
    }

    function resolveActivityPackId(activity) {
      if (!activity || typeof activity !== "object") {
        return "";
      }

      var explicitPackId = normalizePackId(activity.packId);
      if (explicitPackId) {
        return explicitPackId;
      }

      var toolName = String(activity.toolName || "").trim();
      if (!toolName) {
        return "";
      }

      var tool = findSchedulerToolByName(toolName);
      var inferred = normalizePackId(inferPackIdFromTool(tool));
      return inferred === "uncategorized" ? "" : inferred;
    }

    function setSchedulerScopeAndRefresh(threadId) {
      var normalizedThreadId = String(threadId || "").trim();
      if (!normalizedThreadId) {
        return;
      }

      if (scopeSelect) {
        scopeSelect.value = normalizedThreadId;
        syncCustomSelect(scopeSelect);
      }

      post("scheduler_refresh", { threadId: normalizedThreadId });
    }

    function appendSchedulerPackActionBar(host, packId, blocked) {
      if (!host) {
        return;
      }

      var normalizedPackId = String(packId || "").trim().toLowerCase();
      if (!normalizedPackId) {
        return;
      }

      var actions = document.createElement("div");
      actions.className = "options-actions options-actions-wrap";

      var toggleButton = document.createElement("button");
      toggleButton.className = "options-btn options-btn-sm";
      toggleButton.textContent = blocked ? "Unmute Pack" : "Mute Pack";
      toggleButton.disabled = !connected;
      toggleButton.addEventListener("click", function() {
        post("scheduler_set_pack_block", {
          packId: normalizedPackId,
          blocked: !blocked
        });
      });
      actions.appendChild(toggleButton);

      if (!blocked) {
        var tempButton = document.createElement("button");
        tempButton.className = "options-btn options-btn-sm options-btn-ghost";
        tempButton.textContent = "Mute 30m";
        tempButton.disabled = !connected;
        tempButton.addEventListener("click", function() {
          post("scheduler_set_pack_block", {
            packId: normalizedPackId,
            blocked: true,
            durationMinutes: "30"
          });
        });
        actions.appendChild(tempButton);

        var tempLongButton = document.createElement("button");
        tempLongButton.className = "options-btn options-btn-sm options-btn-ghost";
        tempLongButton.textContent = "Mute 2h";
        tempLongButton.disabled = !connected;
        tempLongButton.addEventListener("click", function() {
          post("scheduler_set_pack_block", {
            packId: normalizedPackId,
            blocked: true,
            durationMinutes: "120"
          });
        });
        actions.appendChild(tempLongButton);

        var untilMaintenanceButton = document.createElement("button");
        untilMaintenanceButton.className = "options-btn options-btn-sm options-btn-ghost";
        untilMaintenanceButton.textContent = "Until End";
        untilMaintenanceButton.disabled = !connected;
        untilMaintenanceButton.addEventListener("click", function() {
          post("scheduler_set_pack_block", {
            packId: normalizedPackId,
            blocked: true,
            untilNextMaintenanceWindow: true
          });
        });
        actions.appendChild(untilMaintenanceButton);

        var untilMaintenanceStartButton = document.createElement("button");
        untilMaintenanceStartButton.className = "options-btn options-btn-sm options-btn-ghost";
        untilMaintenanceStartButton.textContent = "Until Start";
        untilMaintenanceStartButton.disabled = !connected;
        untilMaintenanceStartButton.addEventListener("click", function() {
          post("scheduler_set_pack_block", {
            packId: normalizedPackId,
            blocked: true,
            untilNextMaintenanceWindowStart: true
          });
        });
        actions.appendChild(untilMaintenanceStartButton);
      }

      host.appendChild(actions);
    }

    function appendSchedulerThreadActionBar(host, threadId, blocked) {
      if (!host) {
        return;
      }

      var normalizedThreadId = String(threadId || "").trim();
      if (!normalizedThreadId) {
        return;
      }

      var actions = document.createElement("div");
      actions.className = "options-actions options-actions-wrap";

      var inspectButton = document.createElement("button");
      inspectButton.className = "options-btn options-btn-ghost options-btn-sm";
      inspectButton.textContent = "Inspect";
      inspectButton.disabled = !connected;
      inspectButton.addEventListener("click", function() {
        setSchedulerScopeAndRefresh(normalizedThreadId);
      });
      actions.appendChild(inspectButton);

      var toggleButton = document.createElement("button");
      toggleButton.className = "options-btn options-btn-sm";
      toggleButton.textContent = blocked ? "Unmute Thread" : "Mute Thread";
      toggleButton.disabled = !connected;
      toggleButton.addEventListener("click", function() {
        post("scheduler_set_thread_block", {
          threadId: normalizedThreadId,
          blocked: !blocked
        });
      });
      actions.appendChild(toggleButton);

      if (!blocked) {
        var tempButton = document.createElement("button");
        tempButton.className = "options-btn options-btn-sm options-btn-ghost";
        tempButton.textContent = "Mute 30m";
        tempButton.disabled = !connected;
        tempButton.addEventListener("click", function() {
          post("scheduler_set_thread_block", {
            threadId: normalizedThreadId,
            blocked: true,
            durationMinutes: "30"
          });
        });
        actions.appendChild(tempButton);

        var tempLongButton = document.createElement("button");
        tempLongButton.className = "options-btn options-btn-sm options-btn-ghost";
        tempLongButton.textContent = "Mute 2h";
        tempLongButton.disabled = !connected;
        tempLongButton.addEventListener("click", function() {
          post("scheduler_set_thread_block", {
            threadId: normalizedThreadId,
            blocked: true,
            durationMinutes: "120"
          });
        });
        actions.appendChild(tempLongButton);

        var untilMaintenanceButton = document.createElement("button");
        untilMaintenanceButton.className = "options-btn options-btn-sm options-btn-ghost";
        untilMaintenanceButton.textContent = "Until End";
        untilMaintenanceButton.disabled = !connected;
        untilMaintenanceButton.addEventListener("click", function() {
          post("scheduler_set_thread_block", {
            threadId: normalizedThreadId,
            blocked: true,
            untilNextMaintenanceWindow: true
          });
        });
        actions.appendChild(untilMaintenanceButton);

        var untilMaintenanceStartButton = document.createElement("button");
        untilMaintenanceStartButton.className = "options-btn options-btn-sm options-btn-ghost";
        untilMaintenanceStartButton.textContent = "Until Start";
        untilMaintenanceStartButton.disabled = !connected;
        untilMaintenanceStartButton.addEventListener("click", function() {
          post("scheduler_set_thread_block", {
            threadId: normalizedThreadId,
            blocked: true,
            untilNextMaintenanceWindowStart: true
          });
        });
        actions.appendChild(untilMaintenanceStartButton);
      }

      host.appendChild(actions);
    }

    function joinOrNone(value) {
      var items = Array.isArray(value) ? value : [];
      var normalized = [];
      for (var i = 0; i < items.length; i++) {
        var text = String(items[i] || "").trim();
        if (text) {
          normalized.push(text);
        }
      }
      return normalized.length > 0 ? normalized.join(", ") : "none";
    }

    var blockedPackIds = normalizedPackIdArray(scheduler.blockedPackIds);
    var blockedPackSuppressions = normalizedSchedulerSuppressionArray(scheduler.blockedPackSuppressions, true);
    var blockedThreadIds = normalizedThreadIdArray(scheduler.blockedThreadIds);
    var blockedThreadSuppressions = normalizedSchedulerSuppressionArray(scheduler.blockedThreadSuppressions, false);
    var scopedPackId = scopePackSelect ? String(scopePackSelect.value || "").trim().toLowerCase() : "";
    var scopedPackBlocked = isSchedulerPackBlocked(scopedPackId);
    var scopedPackSuppression = findSchedulerSuppression(blockedPackSuppressions, scopedPackId, true);
    var scopedThreadId = scopeSelect ? String(scopeSelect.value || "").trim() : "";
    var scopedThreadBlocked = isSchedulerThreadBlocked(scopedThreadId);
    var scopedThreadSuppression = findSchedulerSuppression(blockedThreadSuppressions, scopedThreadId, false);

    if (scopeTogglePackMuteButton) {
      scopeTogglePackMuteButton.disabled = !connected || !scopedPackId;
      scopeTogglePackMuteButton.dataset.packId = scopedPackId;
      scopeTogglePackMuteButton.dataset.blocked = scopedPackBlocked ? "true" : "false";
      scopeTogglePackMuteButton.textContent = scopedPackBlocked ? "Unmute Scoped Pack" : "Mute Scoped Pack";
    }
    if (scopeTempPackMuteButton) {
      scopeTempPackMuteButton.disabled = !connected || !scopedPackId || scopedPackBlocked;
      scopeTempPackMuteButton.dataset.packId = scopedPackId;
      scopeTempPackMuteButton.title = scopedPackSuppression && scopedPackSuppression.temporary === true
        ? "This pack is already temporarily muted."
        : "";
    }
    if (scopePackMuteUntilMaintenanceButton) {
      scopePackMuteUntilMaintenanceButton.disabled = !connected || !scopedPackId || scopedPackBlocked;
      scopePackMuteUntilMaintenanceButton.dataset.packId = scopedPackId;
      scopePackMuteUntilMaintenanceButton.title = scopedPackSuppression && scopedPackSuppression.temporary === true
        ? "This pack is already temporarily muted."
        : "";
    }
    if (scopePackMuteUntilMaintenanceStartButton) {
      scopePackMuteUntilMaintenanceStartButton.disabled = !connected || !scopedPackId || scopedPackBlocked;
      scopePackMuteUntilMaintenanceStartButton.dataset.packId = scopedPackId;
      scopePackMuteUntilMaintenanceStartButton.title = scopedPackSuppression && scopedPackSuppression.temporary === true
        ? "This pack is already temporarily muted."
        : "";
    }
    if (clearPackBlocksButton) {
      clearPackBlocksButton.disabled = !connected || blockedPackIds.length === 0;
    }
    if (scopeToggleMuteButton) {
      scopeToggleMuteButton.disabled = !connected || !scopedThreadId;
      scopeToggleMuteButton.dataset.threadId = scopedThreadId;
      scopeToggleMuteButton.dataset.blocked = scopedThreadBlocked ? "true" : "false";
      scopeToggleMuteButton.textContent = scopedThreadBlocked ? "Unmute Scoped Thread" : "Mute Scoped Thread";
    }
    if (scopeTempMuteButton) {
      scopeTempMuteButton.disabled = !connected || !scopedThreadId || scopedThreadBlocked;
      scopeTempMuteButton.dataset.threadId = scopedThreadId;
      scopeTempMuteButton.title = scopedThreadSuppression && scopedThreadSuppression.temporary === true
        ? "This thread is already temporarily muted."
        : "";
    }
    if (scopeThreadMuteUntilMaintenanceButton) {
      scopeThreadMuteUntilMaintenanceButton.disabled = !connected || !scopedThreadId || scopedThreadBlocked;
      scopeThreadMuteUntilMaintenanceButton.dataset.threadId = scopedThreadId;
      scopeThreadMuteUntilMaintenanceButton.title = scopedThreadSuppression && scopedThreadSuppression.temporary === true
        ? "This thread is already temporarily muted."
        : "";
    }
    if (scopeThreadMuteUntilMaintenanceStartButton) {
      scopeThreadMuteUntilMaintenanceStartButton.disabled = !connected || !scopedThreadId || scopedThreadBlocked;
      scopeThreadMuteUntilMaintenanceStartButton.dataset.threadId = scopedThreadId;
      scopeThreadMuteUntilMaintenanceStartButton.title = scopedThreadSuppression && scopedThreadSuppression.temporary === true
        ? "This thread is already temporarily muted."
        : "";
    }
    if (clearThreadBlocksButton) {
      clearThreadBlocksButton.disabled = !connected || blockedThreadIds.length === 0;
    }

    appendSchedulerKv("daemon", scheduler.daemonEnabled === true ? "enabled" : "disabled");
    appendSchedulerKv("pause mode", scheduler.paused === true
      ? ("paused" + (scheduler.pauseReason ? " (" + scheduler.pauseReason + ")" : ""))
      : (scheduler.scheduledPauseActive === true ? "scheduled window active" : "running"));
    appendSchedulerKv(
      "workload",
      "queued " + normalizedCount(scheduler.queuedItemCount)
      + " | ready " + normalizedCount(scheduler.readyItemCount)
      + " | running " + normalizedCount(scheduler.runningItemCount)
      + " | tracked threads " + normalizedCount(scheduler.trackedThreadCount));
    appendSchedulerKv(
      "completion",
      "completed items " + normalizedCount(scheduler.completedItemCount)
      + " | pending read-only " + normalizedCount(scheduler.pendingReadOnlyItemCount)
      + " | pending unknown " + normalizedCount(scheduler.pendingUnknownItemCount));
    appendSchedulerKv(
      "executions",
      "completed " + normalizedCount(scheduler.completedExecutionCount)
      + " | requeued " + normalizedCount(scheduler.requeuedExecutionCount)
      + " | released " + normalizedCount(scheduler.releasedExecutionCount));
    appendSchedulerKv(
      "failures",
      "consecutive " + normalizedCount(scheduler.consecutiveFailureCount)
      + " | last outcome " + String(scheduler.lastOutcome || "none"));
    appendSchedulerKv("allowed packs", joinOrNone(scheduler.allowedPackIds));
    appendSchedulerKv("blocked packs", joinOrNone(blockedPackIds));
    appendSchedulerKv("allowed threads", joinOrNone(scheduler.allowedThreadIds));
    appendSchedulerKv("blocked threads", joinOrNone(blockedThreadIds));
    appendSchedulerKv("scope", String(scheduler.scopeThreadId || "").trim() || "global");
    appendSchedulerKv("active maintenance", activeMaintenanceSpecs.length > 0 ? activeMaintenanceSpecs.join(", ") : "none");

    if (blockedPackList) {
      if (blockedPackIds.length === 0) {
        blockedPackList.innerHTML = "<div class='options-item'><div class='options-item-title'>No muted scheduler packs</div></div>";
      } else {
        for (var bp = 0; bp < blockedPackIds.length; bp++) {
          var blockedPackId = blockedPackIds[bp];
          var blockedPackSuppression = findSchedulerSuppression(blockedPackSuppressions, blockedPackId, true);
          var blockedPackCard = document.createElement("div");
          blockedPackCard.className = "options-item";

          var blockedPackHeader = document.createElement("div");
          blockedPackHeader.className = "options-item-header";

          var blockedPackTitle = document.createElement("div");
          blockedPackTitle.className = "options-item-title";
          blockedPackTitle.textContent = resolveSchedulerPackLabel(blockedPackId);
          blockedPackHeader.appendChild(blockedPackTitle);

          var blockedPackPill = document.createElement("span");
          blockedPackPill.className = "options-pill options-pill-routing";
          blockedPackPill.textContent = formatSchedulerSuppressionMode(blockedPackSuppression) || "muted";
          blockedPackHeader.appendChild(blockedPackPill);

          blockedPackCard.appendChild(blockedPackHeader);

          var blockedPackSub = document.createElement("div");
          blockedPackSub.className = "options-item-sub";
          blockedPackSub.textContent = buildSchedulerSuppressionDescription(blockedPackSuppression, "pack")
            || ("Daemon scheduling is suppressed for pack " + blockedPackId + ".");
          blockedPackCard.appendChild(blockedPackSub);

          appendSchedulerPackActionBar(blockedPackCard, blockedPackId, true);
          blockedPackList.appendChild(blockedPackCard);
        }
      }
    }

    if (blockedThreadList) {
      if (blockedThreadIds.length === 0) {
        blockedThreadList.innerHTML = "<div class='options-item'><div class='options-item-title'>No muted scheduler threads</div></div>";
      } else {
        for (var bt = 0; bt < blockedThreadIds.length; bt++) {
          var blockedThreadId = blockedThreadIds[bt];
          var blockedThreadSuppression = findSchedulerSuppression(blockedThreadSuppressions, blockedThreadId, false);
          var blockedCard = document.createElement("div");
          blockedCard.className = "options-item";

          var blockedHeader = document.createElement("div");
          blockedHeader.className = "options-item-header";

          var blockedTitle = document.createElement("div");
          blockedTitle.className = "options-item-title";
          blockedTitle.textContent = resolveSchedulerThreadLabel(blockedThreadId);
          blockedHeader.appendChild(blockedTitle);

          var blockedPill = document.createElement("span");
          blockedPill.className = "options-pill options-pill-routing";
          blockedPill.textContent = formatSchedulerSuppressionMode(blockedThreadSuppression) || "muted";
          blockedHeader.appendChild(blockedPill);

          blockedCard.appendChild(blockedHeader);

          var blockedSub = document.createElement("div");
          blockedSub.className = "options-item-sub";
          blockedSub.textContent = buildSchedulerSuppressionDescription(blockedThreadSuppression, "thread")
            || ("Daemon scheduling is suppressed for thread " + blockedThreadId + ".");
          blockedCard.appendChild(blockedSub);

          appendSchedulerThreadActionBar(blockedCard, blockedThreadId, true);
          blockedThreadList.appendChild(blockedCard);
        }
      }
    }

    if (!maintenanceList) {
      return;
    }

    if (maintenanceWindows.length === 0) {
      maintenanceList.innerHTML = "<div class='options-item'><div class='options-item-title'>No maintenance windows configured</div></div>";
      return;
    }

    for (var mw = 0; mw < maintenanceWindows.length; mw++) {
      var maintenance = maintenanceWindows[mw] || {};
      var spec = String(maintenance.spec || "").trim();
      var card = document.createElement("div");
      card.className = "options-item";

      var header = document.createElement("div");
      header.className = "options-item-header";

      var title = document.createElement("div");
      title.className = "options-item-title";
      title.textContent = spec || "Maintenance window";
      header.appendChild(title);

      var active = activeMaintenanceSpecs.indexOf(spec) >= 0;
      var scopePill = document.createElement("span");
      scopePill.className = "options-pill options-pill-category";
      scopePill.textContent = maintenance.scoped === true ? "scoped" : "global";
      header.appendChild(scopePill);

      if (active) {
        var activePill = document.createElement("span");
        activePill.className = "options-pill options-pill-routing";
        activePill.textContent = "active";
        header.appendChild(activePill);
      }

      var remove = document.createElement("button");
      remove.className = "options-btn options-btn-ghost options-btn-sm";
      remove.textContent = "Remove";
      remove.disabled = !connected || !spec;
      remove.dataset.schedulerSpec = spec;
      remove.addEventListener("click", function(e) {
        var target = e.target;
        var value = target && target.dataset ? String(target.dataset.schedulerSpec || "").trim() : "";
        if (!value) {
          return;
        }
        post("scheduler_remove_maintenance", { spec: value });
      });
      header.appendChild(remove);
      card.appendChild(header);

      var detailParts = [];
      var day = String(maintenance.day || "").trim();
      var startTimeLocal = String(maintenance.startTimeLocal || "").trim();
      var durationMinutes = Number(maintenance.durationMinutes);
      if (day) {
        detailParts.push(day);
      }
      if (startTimeLocal) {
        detailParts.push(startTimeLocal);
      }
      if (Number.isFinite(durationMinutes) && durationMinutes > 0) {
        detailParts.push(String(Math.floor(durationMinutes)) + "m");
      }
      var packId = String(maintenance.packId || "").trim();
      if (packId) {
        detailParts.push("pack " + packId);
      }
      var threadId = String(maintenance.threadId || "").trim();
      if (threadId) {
        detailParts.push("thread " + threadId);
      }
      if (detailParts.length > 0) {
        var sub = document.createElement("div");
        sub.className = "options-item-sub";
        sub.textContent = detailParts.join(" · ");
        card.appendChild(sub);
      }

      maintenanceList.appendChild(card);
    }

    var recentActivity = scheduler && Array.isArray(scheduler.recentActivity)
      ? scheduler.recentActivity
      : [];
    if (activityList) {
      if (recentActivity.length === 0) {
        activityList.innerHTML = "<div class='options-item'><div class='options-item-title'>No recent scheduler activity captured</div></div>";
      } else {
        for (var ra = 0; ra < recentActivity.length; ra++) {
          var activity = recentActivity[ra] || {};
          var activityCard = document.createElement("div");
          activityCard.className = "options-item";

          var activityHeader = document.createElement("div");
          activityHeader.className = "options-item-header";

          var activityTitle = document.createElement("div");
          activityTitle.className = "options-item-title";
          var outcome = String(activity.outcome || "unknown").trim() || "unknown";
          var toolName = String(activity.toolName || "").trim();
          var threadIdForActivity = String(activity.threadId || "").trim();
          var packIdForActivity = resolveActivityPackId(activity);
          var threadSuppressionForActivity = findSchedulerSuppression(blockedThreadSuppressions, threadIdForActivity, false);
          var packSuppressionForActivity = findSchedulerSuppression(blockedPackSuppressions, packIdForActivity, true);
          activityTitle.textContent = toolName
            ? (outcome + " · " + toolName)
            : outcome;
          activityHeader.appendChild(activityTitle);

          if (threadIdForActivity) {
            var activityThreadPill = document.createElement("span");
            activityThreadPill.className = "options-pill options-pill-category";
            activityThreadPill.textContent = threadIdForActivity;
            activityHeader.appendChild(activityThreadPill);
            if (isSchedulerThreadBlocked(threadIdForActivity)) {
              var activityMutedPill = document.createElement("span");
              activityMutedPill.className = "options-pill options-pill-routing";
              activityMutedPill.textContent = formatSchedulerSuppressionMode(threadSuppressionForActivity) || "muted";
              activityHeader.appendChild(activityMutedPill);
            }
          }
          if (packIdForActivity) {
            var activityPackPill = document.createElement("span");
            activityPackPill.className = "options-pill options-pill-category";
            activityPackPill.textContent = resolveSchedulerPackLabel(packIdForActivity);
            activityHeader.appendChild(activityPackPill);
            if (isSchedulerPackBlocked(packIdForActivity)) {
              var activityPackMutedPill = document.createElement("span");
              activityPackMutedPill.className = "options-pill options-pill-routing";
              activityPackMutedPill.textContent = (formatSchedulerSuppressionMode(packSuppressionForActivity) || "pack muted") + (packSuppressionForActivity ? " pack" : "");
              activityHeader.appendChild(activityPackMutedPill);
            }
          }

          activityCard.appendChild(activityHeader);

          var activityParts = [];
          var reason = String(activity.reason || "").trim();
          if (reason) {
            activityParts.push(reason);
          }
          var itemId = String(activity.itemId || "").trim();
          if (itemId) {
            activityParts.push("item " + itemId);
          }
          var outputCount = Number(activity.outputCount);
          if (Number.isFinite(outputCount) && outputCount > 0) {
            activityParts.push("outputs " + Math.floor(outputCount));
          }
          var failureDetail = String(activity.failureDetail || "").trim();
          if (failureDetail) {
            activityParts.push(failureDetail);
          }
          if (activityParts.length > 0) {
            var activitySub = document.createElement("div");
            activitySub.className = "options-item-sub";
            activitySub.textContent = activityParts.join(" · ");
            activityCard.appendChild(activitySub);
          }

          if (threadIdForActivity) {
            appendSchedulerThreadActionBar(activityCard, threadIdForActivity, isSchedulerThreadBlocked(threadIdForActivity));
          }
          if (packIdForActivity) {
            appendSchedulerPackActionBar(activityCard, packIdForActivity, isSchedulerPackBlocked(packIdForActivity));
          }

          activityList.appendChild(activityCard);
        }
      }
    }

    var threadSummaries = scheduler && Array.isArray(scheduler.threadSummaries)
      ? scheduler.threadSummaries
      : [];
    if (threadList) {
      if (threadSummaries.length === 0) {
        threadList.innerHTML = "<div class='options-item'><div class='options-item-title'>No per-thread scheduler summaries captured</div></div>";
      } else {
        for (var ts = 0; ts < threadSummaries.length; ts++) {
          var summary = threadSummaries[ts] || {};
          var threadCard = document.createElement("div");
          threadCard.className = "options-item";

          var threadHeader = document.createElement("div");
          threadHeader.className = "options-item-header";

          var threadTitle = document.createElement("div");
          threadTitle.className = "options-item-title";
          threadTitle.textContent = String(summary.threadId || "thread");
          threadHeader.appendChild(threadTitle);

          var threadPill = document.createElement("span");
          threadPill.className = "options-pill options-pill-category";
          threadPill.textContent = "ready " + normalizedCount(summary.readyItemCount) + " / running " + normalizedCount(summary.runningItemCount);
          threadHeader.appendChild(threadPill);
          var threadBlocked = isSchedulerThreadBlocked(summary.threadId);
          var threadSuppression = findSchedulerSuppression(blockedThreadSuppressions, summary.threadId, false);
          if (threadBlocked) {
            var mutedPill = document.createElement("span");
            mutedPill.className = "options-pill options-pill-routing";
            mutedPill.textContent = formatSchedulerSuppressionMode(threadSuppression) || "muted";
            threadHeader.appendChild(mutedPill);
          }
          threadCard.appendChild(threadHeader);

          var threadParts = [];
          threadParts.push(
            "queued " + normalizedCount(summary.queuedItemCount)
            + ", completed " + normalizedCount(summary.completedItemCount)
            + ", pending read-only " + normalizedCount(summary.pendingReadOnlyItemCount)
            + ", pending unknown " + normalizedCount(summary.pendingUnknownItemCount));
          var evidenceTools = Array.isArray(summary.recentEvidenceTools) ? summary.recentEvidenceTools : [];
          var normalizedEvidence = [];
          for (var et = 0; et < evidenceTools.length; et++) {
            var evidenceTool = String(evidenceTools[et] || "").trim();
            if (evidenceTool) {
              normalizedEvidence.push(evidenceTool);
            }
          }
          if (normalizedEvidence.length > 0) {
            threadParts.push("evidence " + normalizedEvidence.join(", "));
          }

          var threadSub = document.createElement("div");
          threadSub.className = "options-item-sub";
          threadSub.textContent = threadParts.join(" · ");
          threadCard.appendChild(threadSub);
          appendSchedulerThreadActionBar(threadCard, summary.threadId, threadBlocked);
          threadList.appendChild(threadCard);
        }
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
    var showTurnTraceToggle = byId("optShowTurnTrace");
    if (showTurnTraceToggle) {
      showTurnTraceToggle.checked = normalizeBool(state.options.debug && state.options.debug.showTurnTrace);
    }
    var showDraftBubblesToggle = byId("optShowDraftBubbles");
    if (showDraftBubblesToggle) {
      var debugOptions = state.options.debug || {};
      showDraftBubblesToggle.checked = typeof debugOptions.showDraftBubbles === "boolean"
        ? debugOptions.showDraftBubbles
        : false;
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
        var ensureThreadMs = Number(metrics.ensureThreadMs);
        var weightedSubsetSelectionMs = Number(metrics.weightedSubsetSelectionMs);
        var resolveModelMs = Number(metrics.resolveModelMs);
        var slowestPhaseLabel = "";
        var slowestPhaseMs = -1;
        var phaseParts = [];
        if (Number.isFinite(ensureThreadMs) && ensureThreadMs >= 0) {
          if (ensureThreadMs > slowestPhaseMs) {
            slowestPhaseMs = ensureThreadMs;
            slowestPhaseLabel = "ensure-thread";
          }
          phaseParts.push("ensure-thread " + Math.floor(ensureThreadMs) + "ms");
        }
        if (Number.isFinite(weightedSubsetSelectionMs) && weightedSubsetSelectionMs >= 0) {
          if (weightedSubsetSelectionMs > slowestPhaseMs) {
            slowestPhaseMs = weightedSubsetSelectionMs;
            slowestPhaseLabel = "weighted-subset";
          }
          phaseParts.push("weighted-subset " + Math.floor(weightedSubsetSelectionMs) + "ms");
        }
        if (Number.isFinite(resolveModelMs) && resolveModelMs >= 0) {
          if (resolveModelMs > slowestPhaseMs) {
            slowestPhaseMs = resolveModelMs;
            slowestPhaseLabel = "resolve-model";
          }
          phaseParts.push("resolve-model " + Math.floor(resolveModelMs) + "ms");
        }
        if (phaseParts.length > 0) {
          parts.push("Turn phases: " + phaseParts.join(", ") + ".");
          if (slowestPhaseLabel && Number.isFinite(slowestPhaseMs) && slowestPhaseMs >= 0) {
            parts.push("Slowest turn stage: " + slowestPhaseLabel + " (" + Math.floor(slowestPhaseMs) + "ms).");
          }
        }

        var autonomyCounters = Array.isArray(metrics.autonomyCounters) ? metrics.autonomyCounters : [];
        if (autonomyCounters.length > 0) {
          var counterParts = [];
          for (var c = 0; c < autonomyCounters.length; c++) {
            var counter = autonomyCounters[c] || {};
            var counterName = String(counter.name || "").trim();
            var counterCount = Number(counter.count);
            if (!counterName || !Number.isFinite(counterCount) || counterCount <= 0) {
              continue;
            }
            counterParts.push(counterName + "=" + Math.floor(counterCount));
          }
          if (counterParts.length > 0) {
            parts.push("Autonomy counters: " + counterParts.join(", ") + ".");
          }
        }
      }

      var latencySummary = state.latencySummary;
      if (latencySummary && typeof latencySummary === "object") {
        var p50Ms = Number(latencySummary.p50Ms);
        var p95Ms = Number(latencySummary.p95Ms);
        var sampleCount = Number(latencySummary.samples);
        if (Number.isFinite(p50Ms) && Number.isFinite(p95Ms) && Number.isFinite(sampleCount) && sampleCount > 0) {
          parts.push(
            "Provider latency: p50 " + Math.floor(p50Ms) + "ms, p95 " + Math.floor(p95Ms) + "ms (" + Math.floor(sampleCount) + " samples).");
        }
      }

      var providerCircuit = state.providerCircuit;
      if (providerCircuit && typeof providerCircuit === "object") {
        var retryAfterSeconds = Number(providerCircuit.retryAfterSeconds);
        if (providerCircuit.open === true && Number.isFinite(retryAfterSeconds) && retryAfterSeconds > 0) {
          parts.push("Provider circuit: cooling down (" + Math.ceil(retryAfterSeconds) + "s).");
        }
      }

      var queuedTurnCount = Number(state.queuedTurnCount);
      if (Number.isFinite(queuedTurnCount) && queuedTurnCount > 0) {
        parts.push("Turn queue: " + Math.floor(queuedTurnCount) + ".");
      }

      var queuedPromptCount = Number(state.queuedPromptCount);
      if (Number.isFinite(queuedPromptCount) && queuedPromptCount > 0) {
        parts.push("Sign-in queue: " + Math.floor(queuedPromptCount) + ".");
      }

      if (Array.isArray(state.statusTimeline) && state.statusTimeline.length > 0) {
        parts.push("Runtime lifecycle: " + state.statusTimeline.join(" > "));
      }

      if (Array.isArray(state.activityTimeline) && state.activityTimeline.length > 0) {
        parts.push("Live timeline: " + state.activityTimeline.join(" > "));
      }

      if (Array.isArray(state.routingPromptExposureHistory) && state.routingPromptExposureHistory.length > 0) {
        var routingExposureParts = [];
        for (var r = 0; r < state.routingPromptExposureHistory.length; r++) {
          var routingExposure = normalizeRoutingPromptExposure(state.routingPromptExposureHistory[r]);
          if (!routingExposure) {
            continue;
          }

          var routingExposureText = routingExposure.strategy + " (" + routingExposure.selectedToolCount + "/" + routingExposure.totalToolCount + ")";
          if (routingExposure.requestId || routingExposure.threadId) {
            routingExposureText += " [" + (routingExposure.requestId || "n/a") + " @ " + (routingExposure.threadId || "n/a") + "]";
          }
          if (routingExposure.reordered && routingExposure.topToolNames.length > 0) {
            routingExposureText += " -> " + routingExposure.topToolNames.slice(0, 2).join(", ");
            if (routingExposure.topToolNames.length > 2) {
              routingExposureText += ", +" + String(routingExposure.topToolNames.length - 2);
            }
          }

          routingExposureParts.push(routingExposureText);
        }
        if (routingExposureParts.length > 0) {
          parts.push("Routing exposure: " + routingExposureParts.join(" | ") + ".");
        }
      }

      stateLabel.textContent = parts.join(" ");
    }

    var startupPhaseState = byId("optStartupPhaseState");
    var startupPhaseTimeline = byId("optStartupPhaseTimeline");
    if (startupPhaseState || startupPhaseTimeline) {
      var startupModel = buildStartupPhaseTimelineModel();
      if (startupPhaseState) {
        startupPhaseState.textContent = startupModel.summary;
      }
      if (startupPhaseTimeline) {
        startupPhaseTimeline.textContent = "";
        var startupRows = Array.isArray(startupModel.rows) ? startupModel.rows : [];
        for (var sr = 0; sr < startupRows.length; sr++) {
          var startupRow = startupRows[sr] || {};
          var rowState = String(startupRow.state || "pending").trim().toLowerCase();
          if (rowState !== "active" && rowState !== "done" && rowState !== "skipped") {
            rowState = "pending";
          }

          var row = document.createElement("div");
          row.className = "options-startup-phase options-startup-phase-" + rowState;

          var rowHead = document.createElement("div");
          rowHead.className = "options-startup-phase-head";

          var rowLabel = document.createElement("div");
          rowLabel.className = "options-startup-phase-label";
          rowLabel.textContent = startupRow.label || "Phase";
          rowHead.appendChild(rowLabel);

          var rowStatePill = document.createElement("div");
          rowStatePill.className = "options-startup-phase-state";
          rowStatePill.textContent = startupPhaseStateLabel(rowState);
          rowHead.appendChild(rowStatePill);

          row.appendChild(rowHead);

          var rowDetail = document.createElement("div");
          rowDetail.className = "options-startup-phase-detail";
          rowDetail.textContent = startupRow.detail || "";
          row.appendChild(rowDetail);

          startupPhaseTimeline.appendChild(row);
        }
      }
    }

    var startupDiagnosticsState = byId("optStartupDiagnosticsState");
    var startupDiagnosticsKv = byId("optStartupDiagnosticsKv");
    if (startupDiagnosticsKv) {
      startupDiagnosticsKv.textContent = "";
    }
    if (startupDiagnosticsState) {
      startupDiagnosticsState.textContent = "No startup diagnostics yet.";
    }

    var startupDiagnostics = state.options.startupDiagnostics;
    if (startupDiagnostics && typeof startupDiagnostics === "object") {
      var cacheDiag = startupDiagnostics.cache && typeof startupDiagnostics.cache === "object"
        ? startupDiagnostics.cache
        : {};
      var helloDiag = startupDiagnostics.hello && typeof startupDiagnostics.hello === "object"
        ? startupDiagnostics.hello
        : {};
      var listToolsDiag = startupDiagnostics.listTools && typeof startupDiagnostics.listTools === "object"
        ? startupDiagnostics.listTools
        : {};
      var authRefreshDiag = startupDiagnostics.authRefresh && typeof startupDiagnostics.authRefresh === "object"
        ? startupDiagnostics.authRefresh
        : {};
      var metadataDiag = startupDiagnostics.metadataSync && typeof startupDiagnostics.metadataSync === "object"
        ? startupDiagnostics.metadataSync
        : {};
      var metadataFailureRecoveryDiag = metadataDiag.failureRecovery && typeof metadataDiag.failureRecovery === "object"
        ? metadataDiag.failureRecovery
        : {};
      var authGateDiag = startupDiagnostics.authGate && typeof startupDiagnostics.authGate === "object"
        ? startupDiagnostics.authGate
        : {};
      var watchdogDiag = startupDiagnostics.watchdog && typeof startupDiagnostics.watchdog === "object"
        ? startupDiagnostics.watchdog
        : {};

      function formatStartupDiagMs(value) {
        var ms = Number(value);
        if (!Number.isFinite(ms) || ms < 0) {
          return "n/a";
        }
        if (ms >= 1000) {
          return (ms / 1000).toFixed(2) + "s";
        }
        return Math.floor(ms) + "ms";
      }

      function formatStartupDiagPhase(phase) {
        if (!phase || typeof phase !== "object") {
          return "n/a";
        }
        var result = String(phase.result || "unknown").trim().toLowerCase();
        if (!result) {
          result = "unknown";
        }
        var parts = [result + " in " + formatStartupDiagMs(phase.durationMs)];
        var attempts = Number(phase.attempts);
        if (Number.isFinite(attempts) && attempts > 1) {
          parts.push("attempts " + Math.floor(attempts));
        }
        var updatedLocal = String(phase.updatedLocal || "").trim();
        if (updatedLocal) {
          parts.push(updatedLocal);
        }
        return parts.join(" | ");
      }

      function appendStartupDiagKv(label, value) {
        if (!startupDiagnosticsKv) {
          return;
        }
        var k = document.createElement("div");
        k.className = "options-k";
        k.textContent = label;
        var v = document.createElement("div");
        v.className = "options-v";
        v.textContent = value;
        startupDiagnosticsKv.appendChild(k);
        startupDiagnosticsKv.appendChild(v);
      }

      var cacheLabel = String(cacheDiag.label || "Unknown").trim() || "Unknown";
      var cacheMode = String(cacheDiag.mode || "unknown").trim().toLowerCase();
      var cacheUpdated = String(cacheDiag.updatedLocal || "").trim();
      var cacheText = cacheLabel + " (" + (cacheMode || "unknown") + ")";
      if (cacheUpdated) {
        cacheText += " | " + cacheUpdated;
      }
      appendStartupDiagKv("bootstrap cache", cacheText);
      appendStartupDiagKv("hello", formatStartupDiagPhase(helloDiag));
      appendStartupDiagKv("list tools", formatStartupDiagPhase(listToolsDiag));
      appendStartupDiagKv("auth refresh", formatStartupDiagPhase(authRefreshDiag));

      var metadataParts = [
        String(metadataDiag.result || "unknown").trim().toLowerCase() + " in " + formatStartupDiagMs(metadataDiag.durationMs)
      ];
      if (metadataDiag.inProgress === true) {
        metadataParts.push("in progress");
      }
      if (metadataDiag.queued === true) {
        metadataParts.push("queued");
      }
      var metadataUpdated = String(metadataDiag.updatedLocal || "").trim();
      if (metadataUpdated) {
        metadataParts.push(metadataUpdated);
      }
      appendStartupDiagKv("metadata sync", metadataParts.join(" | "));

      var metadataRecoveryParts = [];
      var metadataRecoveryRetriesConsumed = Number(metadataFailureRecoveryDiag.retriesConsumed);
      var metadataRecoveryRetryLimit = Number(metadataFailureRecoveryDiag.retryLimit);
      var normalizedRetriesConsumed = Number.isFinite(metadataRecoveryRetriesConsumed)
        ? Math.max(0, Math.floor(metadataRecoveryRetriesConsumed))
        : 0;
      var normalizedRetryLimit = Number.isFinite(metadataRecoveryRetryLimit)
        ? Math.max(0, Math.floor(metadataRecoveryRetryLimit))
        : 0;
      metadataRecoveryParts.push("retry " + normalizedRetriesConsumed + "/" + normalizedRetryLimit);
      if (metadataFailureRecoveryDiag.rerunRequested === true) {
        metadataRecoveryParts.push("rerun requested");
      }
      var metadataRecoveryQueuedCount = Number(metadataFailureRecoveryDiag.queuedCount);
      if (Number.isFinite(metadataRecoveryQueuedCount) && metadataRecoveryQueuedCount > 0) {
        metadataRecoveryParts.push("queued " + Math.floor(metadataRecoveryQueuedCount));
      }
      var metadataRecoveryLimitReachedCount = Number(metadataFailureRecoveryDiag.limitReachedCount);
      if (Number.isFinite(metadataRecoveryLimitReachedCount) && metadataRecoveryLimitReachedCount > 0) {
        metadataRecoveryParts.push("limit reached " + Math.floor(metadataRecoveryLimitReachedCount));
      }
      var metadataLastFailureKind = String(metadataFailureRecoveryDiag.lastFailureKind || "").trim();
      if (metadataLastFailureKind && metadataLastFailureKind !== "none") {
        metadataRecoveryParts.push("last " + metadataLastFailureKind);
      }
      var metadataLastFailureLocal = String(metadataFailureRecoveryDiag.lastFailureLocal || "").trim();
      if (metadataLastFailureLocal) {
        metadataRecoveryParts.push(metadataLastFailureLocal);
      }
      appendStartupDiagKv("metadata recovery", metadataRecoveryParts.join(" | "));

      var authGateParts = [];
      authGateParts.push(authGateDiag.active === true ? "active" : "idle");
      authGateParts.push("current " + formatStartupDiagMs(authGateDiag.currentWaitMs));
      authGateParts.push("last " + formatStartupDiagMs(authGateDiag.lastWaitMs));
      var waitCount = Number(authGateDiag.waitCount);
      if (Number.isFinite(waitCount) && waitCount >= 0) {
        authGateParts.push("count " + Math.floor(waitCount));
      }
      var waitingSince = String(authGateDiag.waitingSinceLocal || "").trim();
      if (waitingSince) {
        authGateParts.push("since " + waitingSince);
      }
      appendStartupDiagKv("auth gate", authGateParts.join(" | "));

      var watchdogParts = [];
      var activeClears = Number(watchdogDiag.activeClears);
      var queuedClears = Number(watchdogDiag.queuedClears);
      watchdogParts.push("active " + (Number.isFinite(activeClears) ? Math.floor(Math.max(0, activeClears)) : 0));
      watchdogParts.push("queued " + (Number.isFinite(queuedClears) ? Math.floor(Math.max(0, queuedClears)) : 0));
      var lastKind = String(watchdogDiag.lastKind || "none").trim();
      if (lastKind) {
        watchdogParts.push("last " + lastKind);
      }
      var lastCleared = String(watchdogDiag.lastClearedLocal || "").trim();
      if (lastCleared) {
        watchdogParts.push(lastCleared);
      }
      appendStartupDiagKv("watchdog clears", watchdogParts.join(" | "));

      if (startupDiagnosticsState) {
        var startupSummaryParts = [];
        startupSummaryParts.push("Cache " + cacheLabel + ".");
        if (authGateDiag.active === true) {
          startupSummaryParts.push("Authentication gate is active.");
        } else if (metadataFailureRecoveryDiag.rerunRequested === true) {
          startupSummaryParts.push("Metadata recovery rerun is queued.");
        } else if (metadataDiag.inProgress === true) {
          startupSummaryParts.push("Metadata sync is in progress.");
        } else if (metadataDiag.queued === true) {
          startupSummaryParts.push("Metadata sync is queued.");
        } else if (Number(metadataFailureRecoveryDiag.limitReachedCount) > 0) {
          startupSummaryParts.push("Metadata recovery retry limit reached.");
        } else {
          startupSummaryParts.push("Runtime startup diagnostics are healthy.");
        }
        startupDiagnosticsState.textContent = startupSummaryParts.join(" ");
      }
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
