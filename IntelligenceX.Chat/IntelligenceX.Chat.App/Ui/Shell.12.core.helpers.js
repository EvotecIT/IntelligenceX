  function conversationTitle(item) {
    var title = (item && item.title ? String(item.title) : "").trim();
    return title || "New Chat";
  }

  function conversationIsSystem(item) {
    return !!(item && item.isSystem === true);
  }

  function conversationMeta(item) {
    var isSystem = conversationIsSystem(item);
    var count = item && typeof item.messageCount === "number" ? item.messageCount : 0;
    var updated = item && item.updatedLocal ? String(item.updatedLocal) : "";
    var runtimeLabel = item && item.runtimeLabel ? String(item.runtimeLabel).trim() : "";
    var modelLabel = item && item.modelLabel ? String(item.modelLabel).trim() : "";
    var base = isSystem
      ? (String(count) + (count === 1 ? " event" : " events"))
      : (String(count) + (count === 1 ? " message" : " messages"));
    if (isSystem) {
      base = "System log · " + base;
    } else if (runtimeLabel || modelLabel) {
      var runtimeSummary = runtimeLabel || "runtime";
      if (modelLabel) {
        runtimeSummary += " | " + modelLabel;
      }
      base = runtimeSummary + " · " + base;
    }
    return updated ? (base + " · " + updated) : base;
  }

  function normalizeConversationModelValue(value) {
    var normalized = String(value || "").trim();
    if (!normalized || normalized === "(auto)") {
      return "";
    }
    return normalized;
  }

  function buildConversationModelChoices(chat) {
    var local = ((state.options || {}).localModel || {});
    var models = Array.isArray(local.models) ? local.models : [];
    var seen = {};
    var list = [];

    function push(value) {
      var normalized = normalizeConversationModelValue(value);
      if (!normalized) {
        return;
      }
      var key = normalized.toLowerCase();
      if (seen[key]) {
        return;
      }
      seen[key] = true;
      list.push(normalized);
    }

    push(chat && chat.modelOverride ? chat.modelOverride : "");
    push(local.model || "");
    for (var i = 0; i < models.length; i++) {
      var item = models[i] || {};
      push(item.model || item.Model || item.id || item.Id || "");
    }

    return list;
  }

  function getGlobalRuntimeScheduler() {
    var globalScheduler = state.options.runtimeSchedulerGlobal && typeof state.options.runtimeSchedulerGlobal === "object"
      ? state.options.runtimeSchedulerGlobal
      : null;
    if (globalScheduler) {
      return globalScheduler;
    }

    var currentScheduler = state.options.runtimeScheduler && typeof state.options.runtimeScheduler === "object"
      ? state.options.runtimeScheduler
      : null;
    if (currentScheduler && !String(currentScheduler.scopeThreadId || "").trim()) {
      return currentScheduler;
    }

    return null;
  }

  function findConversationSchedulerSummary(chat) {
    var threadId = chat && chat.threadId ? String(chat.threadId).trim() : "";
    if (!threadId || conversationIsSystem(chat)) {
      return null;
    }

    var scheduler = getGlobalRuntimeScheduler();
    if (!scheduler) {
      return null;
    }

    var threadSummaries = Array.isArray(scheduler.threadSummaries)
      ? scheduler.threadSummaries
      : [];
    var lookupKey = threadId.toLowerCase();
    for (var i = 0; i < threadSummaries.length; i++) {
      var candidate = threadSummaries[i] || {};
      if (String(candidate.threadId || "").trim().toLowerCase() === lookupKey) {
        return candidate;
      }
    }

    var runningThreadIds = Array.isArray(scheduler.runningThreadIds)
      ? scheduler.runningThreadIds
      : [];
    for (var runningIndex = 0; runningIndex < runningThreadIds.length; runningIndex++) {
      if (String(runningThreadIds[runningIndex] || "").trim().toLowerCase() === lookupKey) {
        return {
          threadId: threadId,
          queuedItemCount: 0,
          readyItemCount: 0,
          runningItemCount: 1
        };
      }
    }

    var readyThreadIds = Array.isArray(scheduler.readyThreadIds)
      ? scheduler.readyThreadIds
      : [];
    for (var readyIndex = 0; readyIndex < readyThreadIds.length; readyIndex++) {
      if (String(readyThreadIds[readyIndex] || "").trim().toLowerCase() === lookupKey) {
        return {
          threadId: threadId,
          queuedItemCount: 0,
          readyItemCount: 1,
          runningItemCount: 0
        };
      }
    }

    return null;
  }

  function getConversationSchedulerHint(chat) {
    var summary = findConversationSchedulerSummary(chat);
    if (!summary) {
      return null;
    }

    var running = typeof summary.runningItemCount === "number" ? summary.runningItemCount : 0;
    var ready = typeof summary.readyItemCount === "number" ? summary.readyItemCount : 0;
    var queued = typeof summary.queuedItemCount === "number" ? summary.queuedItemCount : 0;
    if (running <= 0 && ready <= 0 && queued <= 0) {
      return null;
    }

    var text = "";
    var tone = "queued";
    if (running > 0) {
      text = "BG run " + String(running);
      tone = "running";
    } else if (ready > 0) {
      text = "BG ready " + String(ready);
      tone = "ready";
    } else {
      text = "BG queued " + String(queued);
    }

    return {
      text: text,
      tone: tone,
      title: "Background scheduler: ready "
        + String(ready)
        + ", running "
        + String(running)
        + ", queued "
        + String(queued)
    };
  }

  function isConversationSchedulerBlocked(chat) {
    var threadId = chat && chat.threadId ? String(chat.threadId).trim() : "";
    if (!threadId) {
      return false;
    }

    var scheduler = getGlobalRuntimeScheduler();
    var blockedThreadIds = scheduler && Array.isArray(scheduler.blockedThreadIds)
      ? scheduler.blockedThreadIds
      : [];
    var lookupKey = threadId.toLowerCase();
    for (var i = 0; i < blockedThreadIds.length; i++) {
      if (String(blockedThreadIds[i] || "").trim().toLowerCase() === lookupKey) {
        return true;
      }
    }

    return false;
  }

  function findConversationSchedulerSuppression(chat) {
    var threadId = chat && chat.threadId ? String(chat.threadId).trim() : "";
    if (!threadId) {
      return null;
    }

    var scheduler = getGlobalRuntimeScheduler();
    var suppressions = scheduler && Array.isArray(scheduler.blockedThreadSuppressions)
      ? scheduler.blockedThreadSuppressions
      : [];
    var lookupKey = threadId.toLowerCase();
    for (var i = 0; i < suppressions.length; i++) {
      var candidate = suppressions[i] || {};
      if (String(candidate.id || "").trim().toLowerCase() === lookupKey) {
        return candidate;
      }
    }

    return null;
  }

  function formatSchedulerSuppressionExpiry(utcTicks) {
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

  function renderSidebarConversations() {
    var host = chatSidebarList;
    if (!host) {
      return;
    }

    host.innerHTML = "";
    var items = state.options.conversations || [];
    if (items.length === 0) {
      var empty = document.createElement("div");
      empty.className = "chat-sidebar-empty";
      empty.textContent = "No chats yet";
      host.appendChild(empty);
      return;
    }

    for (var i = 0; i < items.length; i++) {
      var chat = items[i];
      var id = chat && chat.id ? String(chat.id) : "";
      if (!id) {
        continue;
      }

      var button = document.createElement("button");
      button.type = "button";
      button.className = "chat-sidebar-item";
      button.dataset.conversationId = id;
      var isSystem = conversationIsSystem(chat);
      if (isSystem) {
        button.classList.add("system");
      }
      if (id === state.options.activeConversationId) {
        button.classList.add("active");
      }

      var body = document.createElement("div");
      body.className = "chat-sidebar-item-body";

      var title = document.createElement("span");
      title.className = "chat-sidebar-item-title";
      title.textContent = conversationTitle(chat);
      body.appendChild(title);
      if (isSystem) {
        var badge = document.createElement("span");
        badge.className = "chat-sidebar-item-pill";
        badge.textContent = "System";
        body.appendChild(badge);
      }
      var schedulerBlocked = isConversationSchedulerBlocked(chat);
      var schedulerHint = getConversationSchedulerHint(chat);
      if (schedulerBlocked) {
        var schedulerSuppression = findConversationSchedulerSuppression(chat);
        var suppressionExpiryLabel = schedulerSuppression && schedulerSuppression.temporary === true
          ? formatSchedulerSuppressionExpiry(schedulerSuppression.expiresUtcTicks)
          : "";
        var blockedBadge = document.createElement("span");
        blockedBadge.className = "chat-sidebar-item-pill chat-sidebar-item-pill-scheduler tone-muted";
        blockedBadge.textContent = schedulerSuppression && schedulerSuppression.temporary === true ? "BG temp" : "BG muted";
        blockedBadge.title = schedulerSuppression && schedulerSuppression.temporary === true
          ? ("Background scheduler is temporarily muted for this thread"
            + (suppressionExpiryLabel ? (" until " + suppressionExpiryLabel) : "."))
          : "Background scheduler is muted for this thread.";
        body.appendChild(blockedBadge);
      } else if (schedulerHint) {
        var schedulerBadge = document.createElement("span");
        schedulerBadge.className = "chat-sidebar-item-pill chat-sidebar-item-pill-scheduler";
        schedulerBadge.classList.add("tone-" + String(schedulerHint.tone || "queued"));
        schedulerBadge.textContent = String(schedulerHint.text || "BG");
        if (schedulerHint.title) {
          schedulerBadge.title = String(schedulerHint.title);
        }
        body.appendChild(schedulerBadge);
      }

      var meta = document.createElement("span");
      meta.className = "chat-sidebar-item-meta";
      meta.textContent = conversationMeta(chat);
      body.appendChild(meta);

      button.appendChild(body);

      if (!isSystem) {
        var deleteBtn = document.createElement("span");
        deleteBtn.className = "chat-sidebar-item-delete";
        deleteBtn.setAttribute("role", "button");
        deleteBtn.setAttribute("aria-label", "Delete conversation");
        deleteBtn.title = "Delete";
        deleteBtn.dataset.conversationId = id;
        deleteBtn.dataset.conversationTitle = conversationTitle(chat);
        deleteBtn.innerHTML = "<svg width='10' height='10' viewBox='0 0 16 16' fill='none'><path d='M4 4l8 8M12 4l-8 8' stroke='currentColor' stroke-width='1.8' stroke-linecap='round'/></svg>";
        button.appendChild(deleteBtn);
      }

      host.appendChild(button);
    }
  }

  function renderOptionsConversations() {
    var host = byId("optConversations");
    if (!host) {
      return;
    }

    host.innerHTML = "";
    var items = state.options.conversations || [];
    if (items.length === 0) {
      host.innerHTML = "<div class='options-item'><div class='options-item-title'>No chats yet</div></div>";
      return;
    }

    for (var i = 0; i < items.length; i++) {
      var chat = items[i];
      var id = chat && chat.id ? String(chat.id) : "";
      if (!id) {
        continue;
      }

      var row = document.createElement("div");
      row.className = "options-conversation-item";
      var isSystem = conversationIsSystem(chat);
      if (isSystem) {
        row.classList.add("system");
      }
      if (id === state.options.activeConversationId) {
        row.classList.add("active");
      }

      var main = document.createElement("div");
      main.className = "options-conversation-main";

      var title = document.createElement("div");
      title.className = "options-conversation-title";
      title.textContent = conversationTitle(chat);
      main.appendChild(title);
      if (isSystem) {
        var systemTag = document.createElement("span");
        systemTag.className = "options-pill options-pill-category";
        systemTag.textContent = "System";
        main.appendChild(systemTag);
      }

      var meta = document.createElement("div");
      meta.className = "options-conversation-meta";
      meta.textContent = conversationMeta(chat);
      main.appendChild(meta);

      if (!isSystem) {
        var currentOverride = normalizeConversationModelValue(chat && chat.modelOverride ? chat.modelOverride : "");
        var choices = buildConversationModelChoices(chat);
        var modelRow = document.createElement("div");
        modelRow.className = "options-conversation-model-row";

        var modelRowLabel = document.createElement("span");
        modelRowLabel.className = "options-conversation-model-label";
        modelRowLabel.textContent = "Model";
        modelRow.appendChild(modelRowLabel);

        var modelSelect = document.createElement("select");
        modelSelect.className = "options-select options-select-sm options-conversation-model-select";
        modelSelect.dataset.conversationId = id;

        var autoOption = document.createElement("option");
        autoOption.value = "";
        autoOption.textContent = "Auto (runtime default)";
        modelSelect.appendChild(autoOption);

        for (var m = 0; m < choices.length; m++) {
          var modelOption = document.createElement("option");
          modelOption.value = choices[m];
          modelOption.textContent = choices[m];
          modelSelect.appendChild(modelOption);
        }

        if (currentOverride) {
          modelSelect.value = currentOverride;
          if (modelSelect.value !== currentOverride) {
            var currentOption = document.createElement("option");
            currentOption.value = currentOverride;
            currentOption.textContent = currentOverride;
            modelSelect.appendChild(currentOption);
            modelSelect.value = currentOverride;
          }
        } else {
          modelSelect.value = "";
        }

        modelRow.appendChild(modelSelect);
        main.appendChild(modelRow);
      }

      if (chat.preview) {
        var preview = document.createElement("div");
        preview.className = "options-conversation-preview";
        preview.textContent = String(chat.preview);
        main.appendChild(preview);
      }

      row.appendChild(main);

      if (id !== state.options.activeConversationId) {
        var actions = document.createElement("div");
        actions.className = "options-conversation-actions";

        var openButton = document.createElement("button");
        openButton.type = "button";
        openButton.className = "options-btn options-btn-sm options-conversation-switch";
        openButton.dataset.action = "switch";
        openButton.dataset.conversationId = id;
        openButton.textContent = "Open";
        actions.appendChild(openButton);

        if (!isSystem) {
          var renameButton = document.createElement("button");
          renameButton.type = "button";
          renameButton.className = "options-btn options-btn-sm options-btn-ghost options-conversation-switch";
          renameButton.dataset.action = "rename";
          renameButton.dataset.conversationId = id;
          renameButton.dataset.currentTitle = conversationTitle(chat);
          renameButton.textContent = "Rename";
          actions.appendChild(renameButton);

          var deleteButton = document.createElement("button");
          deleteButton.type = "button";
          deleteButton.className = "options-btn options-btn-sm options-btn-danger options-conversation-switch";
          deleteButton.dataset.action = "delete";
          deleteButton.dataset.conversationId = id;
          deleteButton.dataset.currentTitle = conversationTitle(chat);
          deleteButton.textContent = "Delete";
          actions.appendChild(deleteButton);
        } else {
          var lockedPill = document.createElement("span");
          lockedPill.className = "options-pill";
          lockedPill.textContent = "Locked";
          actions.appendChild(lockedPill);
        }

        row.appendChild(actions);
      } else {
        var activeActions = document.createElement("div");
        activeActions.className = "options-conversation-actions";

        var activePill = document.createElement("span");
        activePill.className = "options-pill";
        activePill.textContent = "Active";
        activeActions.appendChild(activePill);

        if (!isSystem) {
          var renameActiveButton = document.createElement("button");
          renameActiveButton.type = "button";
          renameActiveButton.className = "options-btn options-btn-sm options-btn-ghost options-conversation-switch";
          renameActiveButton.dataset.action = "rename";
          renameActiveButton.dataset.conversationId = id;
          renameActiveButton.dataset.currentTitle = conversationTitle(chat);
          renameActiveButton.textContent = "Rename";
          activeActions.appendChild(renameActiveButton);

          var deleteActiveButton = document.createElement("button");
          deleteActiveButton.type = "button";
          deleteActiveButton.className = "options-btn options-btn-sm options-btn-danger options-conversation-switch";
          deleteActiveButton.dataset.action = "delete";
          deleteActiveButton.dataset.conversationId = id;
          deleteActiveButton.dataset.currentTitle = conversationTitle(chat);
          deleteActiveButton.textContent = "Delete";
          activeActions.appendChild(deleteActiveButton);
        } else {
          var lockedActivePill = document.createElement("span");
          lockedActivePill.className = "options-pill";
          lockedActivePill.textContent = "Locked";
          activeActions.appendChild(lockedActivePill);
        }

        row.appendChild(activeActions);
      }

      host.appendChild(row);
    }
  }

  function positionSelectMenu(wrap) {
    var button = wrap.querySelector(".ix-select-btn");
    var menu = wrap.querySelector(".ix-select-menu");
    if (!button || !menu) {
      return;
    }

    var rect = button.getBoundingClientRect();
    var viewportHeight = window.innerHeight || document.documentElement.clientHeight || 0;
    var edgePadding = 8;
    var preferredMaxHeight = 260;
    var availableBelow = Math.max(0, viewportHeight - rect.bottom - edgePadding);
    var availableAbove = Math.max(0, rect.top - edgePadding);
    var openUpwards = availableBelow < 150 && availableAbove > availableBelow;
    var availableHeight = openUpwards ? availableAbove : availableBelow;
    var maxHeight = Math.max(64, Math.min(preferredMaxHeight, availableHeight));
    if (availableHeight < 64) {
      maxHeight = Math.max(32, availableHeight);
    }

    // Anchor menu to the select wrapper to avoid detached/floating dropdowns while the
    // options pane updates or scrolls.
    menu.style.position = "absolute";
    menu.style.left = "0";
    menu.style.width = "100%";
    menu.style.right = "";
    menu.style.maxHeight = Math.max(32, maxHeight) + "px";

    var offsetTop = Math.max(4, button.offsetHeight + 4);
    if (openUpwards) {
      menu.classList.add("ix-select-menu-up");
      menu.style.top = "auto";
      menu.style.bottom = offsetTop + "px";
    } else {
      menu.classList.remove("ix-select-menu-up");
      menu.style.bottom = "auto";
      menu.style.top = offsetTop + "px";
    }
  }

  function clearSelectMenuPosition(wrap) {
    var menu = wrap ? wrap.querySelector(".ix-select-menu") : null;
    if (!menu) {
      return;
    }
    menu.style.position = "";
    menu.style.left = "";
    menu.style.top = "";
    menu.style.bottom = "";
    menu.style.width = "";
    menu.style.right = "";
    menu.style.maxHeight = "";
    menu.classList.remove("ix-select-menu-up");
  }

  function closeOpenCustomSelect() {
    if (!openCustomSelect) {
      return;
    }
    clearSelectMenuPosition(openCustomSelect);
    openCustomSelect.classList.remove("open");
    openCustomSelect = null;
  }

  function syncCustomSelect(select) {
    if (!select || !select._ixWrap || !select._ixButton || !select._ixMenu) {
      return;
    }

    var button = select._ixButton;
    var menu = select._ixMenu;
    menu.innerHTML = "";

    var selectedText = "";
    for (var i = 0; i < select.options.length; i++) {
      var option = select.options[i];
      if (option.selected) {
        selectedText = option.textContent || option.value || "";
      }

      var item = document.createElement("button");
      item.type = "button";
      item.className = "ix-select-item";
      if (option.selected) {
        item.classList.add("active");
      }
      item.dataset.value = option.value;
      item.textContent = option.textContent || option.value || "";
      menu.appendChild(item);
    }

    if (!selectedText && select.options.length > 0) {
      selectedText = select.options[0].textContent || select.options[0].value || "";
    }

    button.querySelector(".ix-select-label").textContent = selectedText || "";
  }

  function ensureCustomSelect(selectId) {
    var select = byId(selectId);
    if (!select) {
      return;
    }

    if (!select._ixWrap) {
      var parent = select.parentNode;
      var wrap = document.createElement("div");
      wrap.className = "ix-select";
      parent.insertBefore(wrap, select);
      wrap.appendChild(select);

      select.classList.add("options-select-native");
      select.tabIndex = -1;
      select.setAttribute("aria-hidden", "true");

      var button = document.createElement("button");
      button.type = "button";
      button.className = "ix-select-btn";
      button.innerHTML = "<span class='ix-select-label'></span><span class='ix-select-caret' aria-hidden='true'></span>";
      wrap.appendChild(button);

      var menu = document.createElement("div");
      menu.className = "ix-select-menu";
      wrap.appendChild(menu);

      select._ixWrap = wrap;
      select._ixButton = button;
      select._ixMenu = menu;

      button.addEventListener("click", function(e) {
        e.preventDefault();
        e.stopPropagation();
        if (openCustomSelect && openCustomSelect !== wrap) {
          closeOpenCustomSelect();
        }
        var willOpen = !wrap.classList.contains("open");
        wrap.classList.toggle("open", willOpen);
        if (willOpen) {
          positionSelectMenu(wrap);
          openCustomSelect = wrap;
        } else {
          clearSelectMenuPosition(wrap);
          openCustomSelect = null;
        }
      });

      button.addEventListener("keydown", function(e) {
        var key = e.key;
        var count = select.options.length;

        if (key === "Enter" || key === " ") {
          e.preventDefault();
          button.click();
          return;
        }

        if (key === "Escape") {
          closeOpenCustomSelect();
          return;
        }

        if (count === 0) {
          return;
        }

        var idx = select.selectedIndex >= 0 ? select.selectedIndex : 0;
        if (key === "ArrowDown") {
          idx = Math.min(count - 1, idx + 1);
        } else if (key === "ArrowUp") {
          idx = Math.max(0, idx - 1);
        } else if (key === "Home") {
          idx = 0;
        } else if (key === "End") {
          idx = count - 1;
        } else {
          return;
        }

        e.preventDefault();
        if (select.selectedIndex !== idx) {
          select.selectedIndex = idx;
          select.dispatchEvent(new Event("change", { bubbles: true }));
        } else {
          syncCustomSelect(select);
        }
      });

      menu.addEventListener("click", function(e) {
        var optionButton = e.target.closest(".ix-select-item");
        if (!optionButton) {
          return;
        }

        var value = optionButton.dataset.value || "";
        if (select.value !== value) {
          select.value = value;
          select.dispatchEvent(new Event("change", { bubbles: true }));
        }

        closeOpenCustomSelect();
        button.focus();
      });

      select.addEventListener("change", function() {
        syncCustomSelect(select);
      });
    }

    syncCustomSelect(select);
  }

  function normalizePackId(value) {
    var normalized = String(value || "").trim().toLowerCase();
    if (!normalized) {
      return "";
    }

    normalized = normalized.replace(/[\s_.-]/g, "");
    if (normalized === "other") return "uncategorized";
    return normalized;
  }

  function findPackById(packId) {
    var packs = state.options.packs || [];
    var normalized = normalizePackId(packId);
    for (var i = 0; i < packs.length; i++) {
      if (normalizePackId(packs[i].id) === normalized) {
        return packs[i];
      }
    }
    return null;
  }

  function packIsAvailable(packId) {
    var pack = findPackById(packId);
    if (!pack) {
      return true;
    }

    if (normalizeBool(pack.enabled)) {
      return true;
    }

    // Keep runtime-config-disabled packs actionable in UI (for example PowerShell disabled by default).
    return packDisabledByRuntimeConfiguration(packId);
  }

  function packDisabledReason(packId) {
    var pack = findPackById(packId);
    if (!pack || !pack.disabledReason) {
      return "";
    }

    var reason = String(pack.disabledReason || "").trim();
    return reason.length > 0 ? reason : "";
  }

  function packDisabledByRuntimeConfiguration(packId) {
    var reason = packDisabledReason(packId);
    if (!reason) {
      return false;
    }

    return reason.toLowerCase().indexOf("disabled by runtime configuration") >= 0;
  }

  function normalizePackSourceKind(value) {
    var normalized = String(value || "").trim().toLowerCase();
    if (normalized === "builtin" || normalized === "closed_source" || normalized === "open_source") {
      return normalized;
    }
    return "open_source";
  }

  function packSourceKind(packId) {
    var pack = findPackById(packId);
    if (!pack) {
      return "open_source";
    }
    return normalizePackSourceKind(pack.sourceKind);
  }

  function packSourceLabel(sourceKind) {
    var normalized = normalizePackSourceKind(sourceKind);
    if (normalized === "builtin") {
      return "Core";
    }
    if (normalized === "closed_source") {
      return "Private";
    }
    return "Open";
  }

  function packSourceHint(sourceKind) {
    var normalized = normalizePackSourceKind(sourceKind);
    if (normalized === "builtin") {
      return "Pack ships with the core IntelligenceX distribution.";
    }
    if (normalized === "closed_source") {
      return "Pack is from a private/proprietary codebase.";
    }
    return "Pack is from an open-source codebase.";
  }

  function inferPackIdFromTool(tool) {
    if (!tool) {
      return "uncategorized";
    }

    var explicit = normalizePackId(tool.packId);
    if (explicit) {
      return explicit;
    }

    var fromPackName = normalizePackId(tool.packName);
    if (fromPackName) {
      return fromPackName;
    }

    var fromCategory = normalizePackId(tool.category);
    if (fromCategory) {
      return fromCategory;
    }

    return "uncategorized";
  }

  function packDisplayName(packId) {
    var tools = state.options.tools || [];
    for (var i = 0; i < tools.length; i++) {
      var tool = tools[i];
      if (!tool || !tool.packId || !tool.packName) {
        continue;
      }
      if (normalizePackId(tool.packId) === normalizePackId(packId)) {
        return tool.packName;
      }
    }

    var pack = findPackById(packId);
    if (pack && pack.name) {
      return pack.name;
    }

    if (packId === "uncategorized") return "Uncategorized";
    var normalized = String(packId || "").trim();
    if (!normalized) {
      return "Uncategorized";
    }

    return normalized
      .replace(/[_-]+/g, " ")
      .replace(/\b[a-z]/g, function(ch) { return ch.toUpperCase(); });
  }

  function packDescription(packId) {
    var pack = findPackById(packId);
    if (pack && pack.description) {
      var normalized = String(pack.description || "").trim();
      if (normalized.length > 0) {
        return normalized;
      }
    }

    if (packId === "uncategorized") {
      return "Tools without pack metadata. Prefer assigning a canonical pack id and descriptor.";
    }

    return "";
  }

  function packAutonomySummary(packId) {
    var pack = findPackById(packId);
    if (!pack || !pack.autonomySummary || typeof pack.autonomySummary !== "object") {
      return null;
    }
    return pack.autonomySummary;
  }

  function packAutonomySummaryText(packId) {
    var summary = packAutonomySummary(packId);
    if (!summary) {
      return "";
    }

    var segments = [];
    segments.push("Remote " + String(summary.remoteCapableTools || 0));
    segments.push("setup " + String(summary.setupAwareTools || 0));
    segments.push("handoff " + String(summary.handoffAwareTools || 0));
    segments.push("recovery " + String(summary.recoveryAwareTools || 0));
    if (Number(summary.crossPackHandoffTools || 0) > 0) {
      segments.push("cross-pack " + String(summary.crossPackHandoffTools || 0));
    }

    return segments.join(" | ");
  }

  function normalizeStartupBootstrapCacheMode(value) {
    return String(value || "").trim().toLowerCase();
  }

  function startupBootstrapCacheModeIsPersistedPreview(value) {
    return normalizeStartupBootstrapCacheMode(value) === "persisted_preview";
  }

  function ensureAccordionState(packId) {
    if (!Object.prototype.hasOwnProperty.call(state.expandedToolPacks, packId)) {
      state.expandedToolPacks[packId] = false;
    }
    return state.expandedToolPacks[packId] === true;
  }
