  function conversationTitle(item) {
    var title = (item && item.title ? String(item.title) : "").trim();
    return title || "New Chat";
  }

  function conversationMeta(item) {
    var count = item && typeof item.messageCount === "number" ? item.messageCount : 0;
    var updated = item && item.updatedLocal ? String(item.updatedLocal) : "";
    var base = String(count) + (count === 1 ? " message" : " messages");
    return updated ? (base + " · " + updated) : base;
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
      if (id === state.options.activeConversationId) {
        button.classList.add("active");
      }

      var body = document.createElement("div");
      body.className = "chat-sidebar-item-body";

      var title = document.createElement("span");
      title.className = "chat-sidebar-item-title";
      title.textContent = conversationTitle(chat);
      body.appendChild(title);

      var meta = document.createElement("span");
      meta.className = "chat-sidebar-item-meta";
      meta.textContent = conversationMeta(chat);
      body.appendChild(meta);

      button.appendChild(body);

      var deleteBtn = document.createElement("span");
      deleteBtn.className = "chat-sidebar-item-delete";
      deleteBtn.setAttribute("role", "button");
      deleteBtn.setAttribute("aria-label", "Delete conversation");
      deleteBtn.title = "Delete";
      deleteBtn.dataset.conversationId = id;
      deleteBtn.dataset.conversationTitle = conversationTitle(chat);
      deleteBtn.innerHTML = "<svg width='10' height='10' viewBox='0 0 16 16' fill='none'><path d='M4 4l8 8M12 4l-8 8' stroke='currentColor' stroke-width='1.8' stroke-linecap='round'/></svg>";
      button.appendChild(deleteBtn);

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
      if (id === state.options.activeConversationId) {
        row.classList.add("active");
      }

      var main = document.createElement("div");
      main.className = "options-conversation-main";

      var title = document.createElement("div");
      title.className = "options-conversation-title";
      title.textContent = conversationTitle(chat);
      main.appendChild(title);

      var meta = document.createElement("div");
      meta.className = "options-conversation-meta";
      meta.textContent = conversationMeta(chat);
      main.appendChild(meta);

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

        row.appendChild(actions);
      } else {
        var activeActions = document.createElement("div");
        activeActions.className = "options-conversation-actions";

        var activePill = document.createElement("span");
        activePill.className = "options-pill";
        activePill.textContent = "Active";
        activeActions.appendChild(activePill);

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

    menu.style.position = "fixed";
    menu.style.left = rect.left + "px";
    if (openUpwards) {
      menu.classList.add("ix-select-menu-up");
      menu.style.top = Math.max(edgePadding, rect.top - maxHeight - 4) + "px";
    } else {
      menu.classList.remove("ix-select-menu-up");
      menu.style.top = (rect.bottom + 4) + "px";
    }
    menu.style.width = rect.width + "px";
    menu.style.right = "auto";
    menu.style.maxHeight = Math.max(32, maxHeight) + "px";
  }

  function clearSelectMenuPosition(wrap) {
    var menu = wrap ? wrap.querySelector(".ix-select-menu") : null;
    if (!menu) {
      return;
    }
    menu.style.position = "";
    menu.style.left = "";
    menu.style.top = "";
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
    var normalized = String(value || "").toLowerCase().replace(/[\s_.]/g, "-");
    if (normalized.indexOf("ix-") === 0) {
      normalized = normalized.substring(3);
    } else if (normalized.indexOf("intelligencex-") === 0) {
      normalized = normalized.substring("intelligencex-".length);
    }

    if (normalized === "active-directory" || normalized === "activedirectory" || normalized === "adplayground") return "ad";
    if (normalized === "computerx") return "system";
    if (normalized === "event-log") return "eventlog";
    if (normalized === "file-system" || normalized === "filesystem") return "fs";
    if (normalized === "reviewersetup") return "reviewer-setup";
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
      return "Built-in";
    }
    if (normalized === "closed_source") {
      return "Closed source";
    }
    return "Open source";
  }

  function mapCategoryToPackId(category) {
    var normalized = normalizePackId(category);
    if (normalized === "ad") return "ad";
    if (normalized === "active-directory") return "ad";
    if (normalized === "eventlog") return "eventlog";
    if (normalized === "event-log") return "eventlog";
    if (normalized === "fs") return "fs";
    if (normalized === "file-system") return "fs";
    if (normalized === "system") return "system";
    if (normalized === "email") return "email";
    if (normalized === "testimox") return "testimox";
    if (normalized === "reviewer-setup") return "reviewer-setup";
    return "";
  }

  function inferPackIdFromTool(tool) {
    if (!tool) {
      return "other";
    }

    if (tool.packId) {
      var explicit = normalizePackId(tool.packId);
      if (explicit) {
        return explicit;
      }
    }

    var fromCategory = mapCategoryToPackId(tool.category);
    if (fromCategory) {
      return fromCategory;
    }

    var tags = tool.tags || [];
    for (var i = 0; i < tags.length; i++) {
      var mapped = mapCategoryToPackId(tags[i]);
      if (mapped) {
        return mapped;
      }
      var normalized = normalizePackId(tags[i]);
      if (normalized === "ad" || normalized === "eventlog" || normalized === "system" || normalized === "fs" || normalized === "email" || normalized === "testimox" || normalized === "reviewer-setup") {
        return normalized;
      }
    }

    var name = (tool.name || "").toLowerCase();
    if (name.indexOf("ad_") === 0) return "ad";
    if (name.indexOf("eventlog_") === 0) return "eventlog";
    if (name.indexOf("system_") === 0 || name.indexOf("wsl_") === 0) return "system";
    if (name.indexOf("fs_") === 0) return "fs";
    if (name.indexOf("email_") === 0) return "email";
    if (name.indexOf("testimox_") === 0) return "testimox";
    if (name.indexOf("reviewer_setup_") === 0) return "reviewer-setup";

    return "other";
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

    if (packId === "ad") return "ADPlayground";
    if (packId === "eventlog") return "Event Log";
    if (packId === "system") return "ComputerX";
    if (packId === "fs") return "File System";
    if (packId === "email") return "Email";
    if (packId === "testimox") return "TestimoX";
    if (packId === "reviewer-setup") return "Reviewer Setup";
    return "Other";
  }

  function ensureAccordionState(packId) {
    if (!Object.prototype.hasOwnProperty.call(state.expandedToolPacks, packId)) {
      state.expandedToolPacks[packId] = false;
    }
    return state.expandedToolPacks[packId] === true;
  }
