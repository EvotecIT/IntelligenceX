(() => {
  const ixBootstrap = window.IntelligenceXReportRuntime
    ? window.IntelligenceXReportRuntime.readBootstrap()
    : {};
  const ixProviderSwitches = document.querySelectorAll('.hero-switch');
  const ixProviderSections = document.querySelectorAll('.provider-section');
  const ixSupporting = document.querySelector('.supporting');
  const ixProviderDatasetTabs = document.querySelectorAll('.provider-dataset-tab');
  const ixTabs = document.querySelectorAll('.supporting-tab');
  const ixPanels = document.querySelectorAll('.supporting-panel');
  const ixModes = document.querySelectorAll('.supporting-mode');
  const ixSupportingModeController = window.IntelligenceXReportRuntime
    ? window.IntelligenceXReportRuntime.createStoredModeController({
      storageKey: 'ix-usage-report-supporting-mode',
      defaultMode: 'preview',
      compactMode: 'summary'
    })
    : null;
  const ixConversationSortButtons = document.querySelectorAll('[data-conversation-sort]');
  const ixConversationSearchInput = document.querySelector('[data-conversation-search]');
  const ixConversationCount = document.querySelector('[data-conversation-count]');
  const ixConversationEmpty = document.querySelector('[data-conversation-empty]');
  const ixConversationFilterButtons = document.querySelectorAll('[data-conversation-filter-group]');
  const ixConversationResetButton = document.querySelector('[data-conversation-reset]');
  const ixConversationActiveText = document.querySelector('[data-conversation-active-text]');
  const ixConversationActiveChips = document.querySelector('[data-conversation-active-chips]');
  const ixConversationSnapshotCount = document.querySelector('[data-conversation-snapshot-count]');
  const ixConversationSnapshotCountCopy = document.querySelector('[data-conversation-snapshot-count-copy]');
  const ixConversationSnapshotTokens = document.querySelector('[data-conversation-snapshot-tokens]');
  const ixConversationSnapshotTokensCopy = document.querySelector('[data-conversation-snapshot-tokens-copy]');
  const ixConversationSnapshotCost = document.querySelector('[data-conversation-snapshot-cost]');
  const ixConversationSnapshotCostCopy = document.querySelector('[data-conversation-snapshot-cost-copy]');
  const ixConversationSnapshotDuration = document.querySelector('[data-conversation-snapshot-duration]');
  const ixConversationSnapshotDurationCopy = document.querySelector('[data-conversation-snapshot-duration-copy]');
  const ixConversationSnapshotContext = document.querySelector('[data-conversation-snapshot-context]');
  const ixConversationSnapshotContextCopy = document.querySelector('[data-conversation-snapshot-context-copy]');
  const ixConversationContextList = document.querySelector('[data-conversation-context-list]');
  const ixConversationContextCopy = document.querySelector('[data-conversation-context-copy]');
  const ixConversationContextLensButtons = document.querySelectorAll('[data-conversation-context-lens]');
  let ixAccentController = null;
  const ixConversationState = {
    sort: 'tokens',
    contextLens: 'tokens',
    query: '',
    named: '',
    profile: '',
    account: '',
    context: ''
  };
  const ixConversationSortLabels = {
    tokens: 'Tokens',
    cost: 'Cost',
    duration: 'Duration',
    turns: 'Turns',
    compacts: 'Compacts'
  };
  const ixConversationIntegerFormatter = new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 });
  const ixConversationCompactFormatter = new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 });
  const ixConversationPercentFormatter = new Intl.NumberFormat(undefined, { style: 'percent', maximumFractionDigits: 0 });
  const ixConversationDecimalFormatter = new Intl.NumberFormat(undefined, { minimumFractionDigits: 1, maximumFractionDigits: 1 });
  const ixConversationCurrencyFormatter = new Intl.NumberFormat(undefined, { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
  if (window.IntelligenceXReportRuntime) {
    const ixThemeController = window.IntelligenceXReportRuntime.initThemeController({
      themeKey: ixBootstrap.themeKey || 'ix-usage-report-theme',
      defaultTheme: ixBootstrap.defaultTheme || 'system',
      onApply: () => {
        if (ixAccentController) {
          ixAccentController.reapply();
        }
      }
    });
    ixAccentController = window.IntelligenceXReportRuntime.initAccentController({
      accentKey: ixBootstrap.accentKey || 'ix-usage-report-accent',
      defaultAccent: ixBootstrap.defaultAccent || 'violet',
      resolveTheme: () => document.documentElement.getAttribute('data-theme') || ixThemeController.resolveTheme(ixBootstrap.defaultTheme || 'system')
    });
  }
  function ixApplySectionTarget(target) {
    ixProviderSections.forEach(section => {
      const provider = section.getAttribute('data-provider') || '';
      section.classList.toggle('hidden', target !== 'all' && provider !== target);
    });
    if (ixSupporting) ixSupporting.classList.toggle('hidden', target !== 'all');
  }
  ixProviderSwitches.forEach(button => {
    button.addEventListener('click', () => {
      const target = button.getAttribute('data-provider-target') || 'all';
      ixProviderSwitches.forEach(other => {
        const active = other === button;
        other.classList.toggle('active', active);
        other.setAttribute('aria-selected', active ? 'true' : 'false');
      });
      ixApplySectionTarget(target);
    });
  });
  ixApplySectionTarget('all');
  ixProviderDatasetTabs.forEach(button => {
    button.addEventListener('click', () => {
      const shell = button.closest('.provider-datasets');
      if (!shell) return;
      const target = button.getAttribute('data-provider-panel') || 'summary';
      shell.querySelectorAll('.provider-dataset-tab').forEach(other => {
        const active = other === button;
        other.classList.toggle('active', active);
        other.setAttribute('aria-selected', active ? 'true' : 'false');
      });
      shell.querySelectorAll('.provider-panel').forEach(panel => {
        panel.classList.toggle('active', panel.getAttribute('data-provider-panel-content') === target);
      });
    });
  });
  function ixBuildConversationSummary(detail) {
    const parts = [];
    if (detail.tokens) parts.push(`${detail.tokens} tokens`);
    if (detail.share) parts.push(detail.share);
    if (detail.span) parts.push(detail.span);
    if (detail.active) parts.push(detail.active);
    if (detail.turns) parts.push(detail.turns);
    if (detail.compacts) parts.push(detail.compacts);
    if (detail.cost) parts.push(detail.cost);
    return parts.join(' • ');
  }
  function ixRenderConversationChips(host, detail) {
    if (!host) return;
    host.innerHTML = '';
    const values = [];
    if (detail.repository) values.push(detail.repository);
    if (detail.workspace && detail.workspace.toLowerCase() !== (detail.repository || '').toLowerCase()) values.push(detail.workspace);
    if (!detail.repository && !detail.workspace && detail.context) values.push(detail.context);
    if (detail.account) values.push(detail.account);
    if (detail.model) values.push(detail.model);
    if (detail.surface) values.push(detail.surface);
    values.forEach(value => {
      const chip = document.createElement('span');
      chip.textContent = value;
      host.appendChild(chip);
    });
  }
  function ixSetConversationCount(visibleCount, totalCount) {
    if (!ixConversationCount) return;
    if (visibleCount === totalCount) {
      ixConversationCount.textContent = `${visibleCount} visible`;
      return;
    }
    ixConversationCount.textContent = `${visibleCount} of ${totalCount} visible`;
  }
  function ixApplyConversationSelection(button) {
    const detailShell = document.querySelector('[data-conversation-detail]');
    if (!detailShell) return;
    if (!button) {
      detailShell.classList.add('hidden');
      if (ixConversationEmpty) ixConversationEmpty.classList.remove('hidden');
      return;
    }
    detailShell.classList.remove('hidden');
    if (ixConversationEmpty) ixConversationEmpty.classList.add('hidden');
    const detail = {
      rank: button.getAttribute('data-detail-rank') || '',
      title: button.getAttribute('data-detail-title') || '',
      sessionLabel: button.getAttribute('data-detail-sessionlabel') || '',
      sessionCode: button.getAttribute('data-detail-sessioncode') || '',
      tokens: button.getAttribute('data-detail-tokens') || '',
      share: button.getAttribute('data-detail-share') || '',
      started: button.getAttribute('data-detail-started') || '',
      span: button.getAttribute('data-detail-span') || '',
      active: button.getAttribute('data-detail-active') || '',
      turns: button.getAttribute('data-detail-turns') || '',
      compacts: button.getAttribute('data-detail-compacts') || '',
      cost: button.getAttribute('data-detail-cost') || '',
      context: button.getAttribute('data-detail-context') || '',
      repository: button.getAttribute('data-detail-repository') || '',
      workspace: button.getAttribute('data-detail-workspace') || '',
      account: button.getAttribute('data-detail-account') || '',
      model: button.getAttribute('data-detail-model') || '',
      surface: button.getAttribute('data-detail-surface') || ''
    };
    document.querySelectorAll('[data-conversation-button]').forEach(other => {
      const active = other === button;
      other.classList.toggle('active', active);
      other.setAttribute('aria-pressed', active ? 'true' : 'false');
    });
    const sessionText = [detail.sessionLabel, detail.sessionCode].filter(Boolean).join(' ');
    const bindings = [
      ['[data-detail-rank]', detail.rank],
      ['[data-detail-title]', detail.title],
      ['[data-detail-session]', sessionText],
      ['[data-detail-tokens]', detail.tokens],
      ['[data-detail-share]', detail.share],
      ['[data-detail-started]', detail.started],
      ['[data-detail-span]', detail.span],
      ['[data-detail-active]', detail.active],
      ['[data-detail-turns]', detail.turns],
      ['[data-detail-compacts]', detail.compacts],
      ['[data-detail-cost]', detail.cost],
      ['[data-detail-summary]', ixBuildConversationSummary(detail)]
    ];
    bindings.forEach(([selector, value]) => {
      const node = detailShell.querySelector(selector);
      if (!node) return;
      node.textContent = value || 'n/a';
    });
    ixRenderConversationChips(detailShell.querySelector('[data-detail-chip-host]'), detail);
    detailShell.querySelectorAll('.conversation-detail-metric').forEach(metric => {
      const valueNode = metric.querySelector('.conversation-detail-value');
      const text = valueNode ? (valueNode.textContent || '').trim().toLowerCase() : '';
      metric.classList.toggle('hidden', !text || text === 'n/a');
    });
  }
  function ixConversationSearchText(button) {
    return [
      button.getAttribute('data-detail-title') || '',
      button.getAttribute('data-detail-sessioncode') || '',
      button.getAttribute('data-detail-sessionlabel') || '',
      button.getAttribute('data-detail-repository') || '',
      button.getAttribute('data-detail-workspace') || '',
      button.getAttribute('data-detail-account') || '',
      button.getAttribute('data-detail-context') || ''
    ].join(' ').toLowerCase();
  }
  function ixConversationIsNamed(button) {
    const title = (button.getAttribute('data-detail-title') || '').trim();
    return !!title && !/^Session \d+$/i.test(title);
  }
  function ixConversationHasActiveOverrides() {
    return !!((ixConversationState.query || '').trim()
      || ixConversationState.named
      || ixConversationState.profile
      || ixConversationState.account
      || ixConversationState.context
      || ixConversationState.sort !== 'tokens'
      || ixConversationState.contextLens !== 'tokens');
  }
  function ixResetConversationView() {
    ixConversationState.sort = 'tokens';
    ixConversationState.contextLens = 'tokens';
    ixConversationState.query = '';
    ixConversationState.named = '';
    ixConversationState.profile = '';
    ixConversationState.account = '';
    ixConversationState.context = '';
    if (ixConversationSearchInput) {
      ixConversationSearchInput.value = '';
    }
  }
  function ixConversationStateToUrl() {
    if (!window.history || !window.URL) return;
    const url = new URL(window.location.href);
    const params = url.searchParams;
    const normalizedQuery = (ixConversationState.query || '').trim();
    if (normalizedQuery) params.set('conversationSearch', normalizedQuery);
    else params.delete('conversationSearch');
    if (ixConversationState.named) params.set('conversationNamed', ixConversationState.named);
    else params.delete('conversationNamed');
    if (ixConversationState.profile) params.set('conversationProfile', ixConversationState.profile);
    else params.delete('conversationProfile');
    if (ixConversationState.account) params.set('conversationAccount', ixConversationState.account);
    else params.delete('conversationAccount');
    if (ixConversationState.context) params.set('conversationContext', ixConversationState.context);
    else params.delete('conversationContext');
    if (ixConversationState.sort && ixConversationState.sort !== 'tokens') params.set('conversationSort', ixConversationState.sort);
    else params.delete('conversationSort');
    if (ixConversationState.contextLens && ixConversationState.contextLens !== 'tokens') params.set('conversationContextLens', ixConversationState.contextLens);
    else params.delete('conversationContextLens');
    window.history.replaceState(null, '', url.toString());
  }
  function ixRestoreConversationStateFromUrl() {
    if (!window.URLSearchParams) return;
    const params = new URLSearchParams(window.location.search);
    const sort = params.get('conversationSort') || 'tokens';
    ixConversationState.sort = Object.prototype.hasOwnProperty.call(ixConversationSortLabels, sort) ? sort : 'tokens';
    ixConversationState.query = params.get('conversationSearch') || '';
    const named = params.get('conversationNamed') || '';
    ixConversationState.named = named === 'named' || named === 'unnamed' ? named : '';
    const profile = params.get('conversationProfile') || '';
    ixConversationState.profile = profile === 'bursty' || profile === 'marathon' || profile === 'compact-heavy' ? profile : '';
    ixConversationState.account = params.get('conversationAccount') || '';
    ixConversationState.context = params.get('conversationContext') || '';
    const contextLens = params.get('conversationContextLens') || '';
    ixConversationState.contextLens = contextLens === 'cost' || contextLens === 'count' ? contextLens : 'tokens';
    if (ixConversationSearchInput) {
      ixConversationSearchInput.value = ixConversationState.query;
    }
  }
  function ixConversationProfileMatches(button, profile) {
    const tokens = ixConversationSortValue(button, 'tokens');
    const duration = ixConversationSortValue(button, 'duration');
    const turns = ixConversationSortValue(button, 'turns');
    const compacts = ixConversationSortValue(button, 'compacts');
    const hours = duration > 0 ? duration / 3600000 : 0;
    const tokensPerHour = hours > 0 ? tokens / hours : tokens;
    const compactRatio = turns > 0 ? compacts / turns : 0;

    if (profile === 'bursty') {
      return tokens >= 5_000_000 && (duration <= 4 * 3600000 || tokensPerHour >= 2_000_000);
    }
    if (profile === 'marathon') {
      return duration >= 24 * 3600000;
    }
    if (profile === 'compact-heavy') {
      return compacts >= 3 || (turns >= 8 && compactRatio >= 0.2);
    }
    return true;
  }
  function ixConversationMatchesFilters(button) {
    if (ixConversationState.named === 'named' && !ixConversationIsNamed(button)) {
      return false;
    }
    if (ixConversationState.named === 'unnamed' && ixConversationIsNamed(button)) {
      return false;
    }
    if (ixConversationState.profile && !ixConversationProfileMatches(button, ixConversationState.profile)) {
      return false;
    }
    if (ixConversationState.account) {
      const account = (button.getAttribute('data-detail-account') || '').trim().toLowerCase();
      if (account !== ixConversationState.account.toLowerCase()) {
        return false;
      }
    }
    if (ixConversationState.context) {
      const context = ixConversationContextLabel(button).trim().toLowerCase();
      if (context !== ixConversationState.context.toLowerCase()) {
        return false;
      }
    }
    return true;
  }
  function ixApplyConversationFilterButtonState() {
    ixConversationFilterButtons.forEach(button => {
      const group = button.getAttribute('data-conversation-filter-group') || '';
      const value = button.getAttribute('data-conversation-filter-value') || '';
      const active = !!group && ixConversationState[group] === value;
      button.classList.toggle('active', active);
      button.setAttribute('aria-pressed', active ? 'true' : 'false');
    });
  }
  function ixConversationSortValue(button, mode) {
    const attr = {
      tokens: 'data-detail-sorttokens',
      cost: 'data-detail-sortcost',
      duration: 'data-detail-sortduration',
      turns: 'data-detail-sortturns',
      compacts: 'data-detail-sortcompacts'
    }[mode] || 'data-detail-sorttokens';
    const value = Number(button.getAttribute(attr) || '0');
    return Number.isFinite(value) ? value : 0;
  }
  function ixFormatConversationCompactNumber(value) {
    if (!Number.isFinite(value) || value <= 0) return '0';
    if (value < 1000) return ixConversationIntegerFormatter.format(value);
    return ixConversationCompactFormatter.format(value);
  }
  function ixFormatConversationDuration(ms) {
    if (!Number.isFinite(ms) || ms <= 0) return 'n/a';
    const minutes = ms / 60000;
    if (minutes < 60) {
      return `${ixConversationIntegerFormatter.format(Math.max(1, Math.round(minutes)))} min`;
    }
    const hours = minutes / 60;
    if (hours < 24) {
      return `${ixConversationDecimalFormatter.format(hours)} hr`;
    }
    const days = hours / 24;
    return `${ixConversationDecimalFormatter.format(days)} d`;
  }
  function ixFormatConversationPercent(value) {
    if (!Number.isFinite(value) || value <= 0) return '0%';
    return ixConversationPercentFormatter.format(value);
  }
  function ixFormatConversationCurrency(value) {
    if (!Number.isFinite(value) || value <= 0) return '$0';
    if (value >= 1000) {
      return `${ixConversationCurrencyFormatter.format(value).replace(/\.00$/, '')}`;
    }
    return new Intl.NumberFormat(undefined, { style: 'currency', currency: 'USD', minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value);
  }
  function ixConversationContextLabel(row) {
    return (row.getAttribute('data-detail-repository') || '')
      || (row.getAttribute('data-detail-workspace') || '')
      || (row.getAttribute('data-detail-context') || '')
      || 'No repo or workspace';
  }
  function ixConversationContextEntries(rows) {
    const contextMap = new Map();
    rows.forEach(row => {
      const label = ixConversationContextLabel(row);
      const entry = contextMap.get(label) || { label, tokens: 0, cost: 0, count: 0 };
      entry.tokens += ixConversationSortValue(row, 'tokens');
      entry.cost += ixConversationSortValue(row, 'cost');
      entry.count += 1;
      contextMap.set(label, entry);
    });
    return Array.from(contextMap.values()).sort((left, right) => {
      const lens = ixConversationState.contextLens || 'tokens';
      const leftValue = lens === 'cost' ? left.cost : (lens === 'count' ? left.count : left.tokens);
      const rightValue = lens === 'cost' ? right.cost : (lens === 'count' ? right.count : right.tokens);
      if (rightValue !== leftValue) return rightValue - leftValue;
      if (right.count !== left.count) return right.count - left.count;
      return left.label.localeCompare(right.label);
    });
  }
  function ixReindexConversationRows(rows) {
    rows.forEach((row, index) => {
      const rank = `#${index + 1}`;
      row.setAttribute('data-detail-rank', rank);
      const rankNode = row.querySelector('.conversation-rank');
      if (rankNode) rankNode.textContent = rank;
    });
  }
  function ixRenderConversationSnapshot(visibleRows, allRows) {
    const lens = ixConversationState.contextLens || 'tokens';
    const totalVisible = visibleRows.length;
    const visibleTokens = visibleRows.reduce((sum, row) => sum + ixConversationSortValue(row, 'tokens'), 0);
    const allTokens = allRows.reduce((sum, row) => sum + ixConversationSortValue(row, 'tokens'), 0);
    const visibleCost = visibleRows.reduce((sum, row) => sum + ixConversationSortValue(row, 'cost'), 0);
    const allCost = allRows.reduce((sum, row) => sum + ixConversationSortValue(row, 'cost'), 0);
    const visibleDuration = visibleRows.reduce((sum, row) => sum + ixConversationSortValue(row, 'duration'), 0);
    const visibleTurns = visibleRows.reduce((sum, row) => sum + ixConversationSortValue(row, 'turns'), 0);
    const namedCount = visibleRows.filter(ixConversationIsNamed).length;
    const compactedCount = visibleRows.filter(row => ixConversationSortValue(row, 'compacts') > 0).length;
    const averageDuration = totalVisible > 0 ? visibleDuration / totalVisible : 0;
    const averageTurns = totalVisible > 0 ? visibleTurns / totalVisible : 0;
    const contextEntries = ixConversationContextEntries(visibleRows);
    const topContext = contextEntries[0] || { label: 'No repo or workspace', tokens: 0, count: 0 };

    if (ixConversationSnapshotCount) {
      ixConversationSnapshotCount.textContent = ixConversationIntegerFormatter.format(totalVisible);
    }
    if (ixConversationSnapshotCountCopy) {
      if (!totalVisible) {
        ixConversationSnapshotCountCopy.textContent = 'Adjust the search or filters to bring conversations back.';
      } else {
        ixConversationSnapshotCountCopy.textContent = `${ixConversationIntegerFormatter.format(namedCount)} named • ${ixConversationIntegerFormatter.format(compactedCount)} compacted`;
      }
    }
    if (ixConversationSnapshotTokens) {
      ixConversationSnapshotTokens.textContent = ixFormatConversationCompactNumber(visibleTokens);
    }
    if (ixConversationSnapshotTokensCopy) {
      const share = allTokens > 0 ? visibleTokens / allTokens : 0;
      ixConversationSnapshotTokensCopy.textContent = totalVisible
        ? `${ixFormatConversationPercent(share)} of the currently loaded conversation tokens`
        : 'No token volume in the current slice.';
    }
    if (ixConversationSnapshotCost) {
      ixConversationSnapshotCost.textContent = ixFormatConversationCurrency(visibleCost);
    }
    if (ixConversationSnapshotCostCopy) {
      const share = allCost > 0 ? visibleCost / allCost : 0;
      ixConversationSnapshotCostCopy.textContent = totalVisible
        ? `${ixFormatConversationPercent(share)} of the currently loaded conversation cost`
        : 'No cost recorded in the current slice.';
    }
    if (ixConversationSnapshotDuration) {
      ixConversationSnapshotDuration.textContent = ixFormatConversationDuration(averageDuration);
    }
    if (ixConversationSnapshotDurationCopy) {
      ixConversationSnapshotDurationCopy.textContent = totalVisible
        ? `${ixConversationDecimalFormatter.format(averageTurns)} average turns per conversation`
        : 'Average span appears once at least one conversation is visible.';
    }
    if (ixConversationSnapshotContext) {
      ixConversationSnapshotContext.textContent = totalVisible ? topContext.label : 'No repo or workspace';
    }
    if (ixConversationSnapshotContextCopy) {
      if (!totalVisible || topContext.count === 0) {
        ixConversationSnapshotContextCopy.textContent = 'No conversation context is available in the current slice.';
      } else {
        const share = lens === 'cost'
          ? (visibleCost > 0 ? topContext.cost / visibleCost : 0)
          : (lens === 'count'
            ? (totalVisible > 0 ? topContext.count / totalVisible : 0)
            : (visibleTokens > 0 ? topContext.tokens / visibleTokens : 0));
        const shareLabel = lens === 'cost'
          ? 'of visible cost'
          : (lens === 'count' ? 'of visible conversations' : 'of visible tokens');
        ixConversationSnapshotContextCopy.textContent = `${ixConversationIntegerFormatter.format(topContext.count)} conversations • ${ixFormatConversationPercent(share)} ${shareLabel}`;
      }
    }
  }
  function ixRenderConversationContextBreakdown(visibleRows) {
    if (!ixConversationContextList) return;
    ixConversationContextList.innerHTML = '';
    const lens = ixConversationState.contextLens || 'tokens';
    ixConversationContextLensButtons.forEach(button => {
      const active = (button.getAttribute('data-conversation-context-lens') || 'tokens') === lens;
      button.classList.toggle('active', active);
      button.setAttribute('aria-selected', active ? 'true' : 'false');
    });
    if (!visibleRows.length) {
      if (ixConversationContextCopy) {
        ixConversationContextCopy.textContent = 'No visible conversations yet. Clear a filter or widen the search.';
      }
      const empty = document.createElement('div');
      empty.className = 'conversation-context-empty';
      empty.textContent = 'No repo or workspace breakdown is available until conversations are visible.';
      ixConversationContextList.appendChild(empty);
      return;
    }

    const contextEntries = ixConversationContextEntries(visibleRows);
    if (!contextEntries.length) {
      if (ixConversationContextCopy) {
        ixConversationContextCopy.textContent = 'This slice does not include any repo or workspace metadata.';
      }
      const empty = document.createElement('div');
      empty.className = 'conversation-context-empty';
      empty.textContent = 'No repo or workspace metadata is attached to the current conversations.';
      ixConversationContextList.appendChild(empty);
      return;
    }

    if (ixConversationContextCopy) {
      ixConversationContextCopy.textContent = lens === 'cost'
        ? 'Click a repo or workspace to focus the list on where cost is landing.'
        : (lens === 'count'
          ? 'Click a repo or workspace to focus the list on where conversations cluster.'
          : 'Click a repo or workspace to focus the list on token-heavy contexts.');
    }

    const visibleTokens = visibleRows.reduce((sum, row) => sum + ixConversationSortValue(row, 'tokens'), 0);
    const visibleCost = visibleRows.reduce((sum, row) => sum + ixConversationSortValue(row, 'cost'), 0);
    contextEntries.slice(0, 6).forEach(entry => {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'conversation-context-row';
      const isActive = (ixConversationState.context || '').toLowerCase() === entry.label.toLowerCase();
      button.classList.toggle('active', isActive);
      button.setAttribute('aria-pressed', isActive ? 'true' : 'false');

      const main = document.createElement('div');
      main.className = 'conversation-context-main';
      const name = document.createElement('div');
      name.className = 'conversation-context-name';
      name.textContent = entry.label;
      const meta = document.createElement('div');
      meta.className = 'conversation-context-meta';
      meta.textContent = `${ixConversationIntegerFormatter.format(entry.count)} conversations`;
      main.appendChild(name);
      main.appendChild(meta);

      const value = document.createElement('div');
      value.className = 'conversation-context-value';
      const tokens = document.createElement('div');
      tokens.className = 'conversation-context-tokens';
      tokens.textContent = lens === 'cost'
        ? ixFormatConversationCurrency(entry.cost)
        : (lens === 'count'
          ? ixConversationIntegerFormatter.format(entry.count)
          : ixFormatConversationCompactNumber(entry.tokens));
      const share = document.createElement('div');
      share.className = 'conversation-context-share';
      share.textContent = lens === 'cost'
        ? `${ixFormatConversationPercent(visibleCost > 0 ? entry.cost / visibleCost : 0)} of visible cost`
        : (lens === 'count'
          ? `${ixFormatConversationPercent(visibleRows.length > 0 ? entry.count / visibleRows.length : 0)} of visible conversations`
          : `${ixFormatConversationPercent(visibleTokens > 0 ? entry.tokens / visibleTokens : 0)} of visible tokens`);
      value.appendChild(tokens);
      value.appendChild(share);

      button.appendChild(main);
      button.appendChild(value);
      button.addEventListener('click', () => {
        ixConversationState.context = isActive ? '' : entry.label;
        ixApplyConversationView();
      });
      ixConversationContextList.appendChild(button);
    });

    if (contextEntries.length > 6) {
      const extra = document.createElement('div');
      extra.className = 'conversation-context-empty';
      extra.textContent = `${ixConversationIntegerFormatter.format(contextEntries.length - 6)} more contexts remain in this slice. Use search or filters to narrow further.`;
      ixConversationContextList.appendChild(extra);
    }
  }
  function ixConversationFilterDescriptors() {
    const descriptors = [];
    const normalizedQuery = (ixConversationState.query || '').trim();
    if (normalizedQuery) {
      descriptors.push({
        key: 'query',
        value: normalizedQuery,
        label: `Search: ${normalizedQuery}`
      });
    }
    if (ixConversationState.named === 'named') {
      descriptors.push({ key: 'named', value: 'named', label: 'Named Only' });
    }
    if (ixConversationState.named === 'unnamed') {
      descriptors.push({ key: 'named', value: 'unnamed', label: 'Unnamed Only' });
    }
    if (ixConversationState.profile === 'bursty') {
      descriptors.push({ key: 'profile', value: 'bursty', label: 'Signal: Bursty' });
    }
    if (ixConversationState.profile === 'marathon') {
      descriptors.push({ key: 'profile', value: 'marathon', label: 'Signal: Marathon' });
    }
    if (ixConversationState.profile === 'compact-heavy') {
      descriptors.push({ key: 'profile', value: 'compact-heavy', label: 'Signal: Compact-Heavy' });
    }
    if (ixConversationState.account) {
      descriptors.push({
        key: 'account',
        value: ixConversationState.account,
        label: `Account: ${ixConversationState.account}`
      });
    }
    if (ixConversationState.context) {
      descriptors.push({
        key: 'context',
        value: ixConversationState.context,
        label: `Repo/Workspace: ${ixConversationState.context}`
      });
    }
    if (ixConversationState.contextLens === 'cost') {
      descriptors.push({ key: 'contextLens', value: 'cost', label: 'Context Lens: Cost' });
    }
    if (ixConversationState.contextLens === 'count') {
      descriptors.push({ key: 'contextLens', value: 'count', label: 'Context Lens: Conversations' });
    }
    if (ixConversationState.sort !== 'tokens') {
      descriptors.push({
        key: 'sort',
        value: ixConversationState.sort,
        label: `Sort: ${ixConversationSortLabels[ixConversationState.sort] || 'Tokens'}`
      });
    }
    return descriptors;
  }
  function ixClearConversationDescriptor(descriptor) {
    if (!descriptor) return;
    if (descriptor.key === 'query') {
      ixConversationState.query = '';
      if (ixConversationSearchInput) {
        ixConversationSearchInput.value = '';
      }
    } else if (descriptor.key === 'contextLens') {
      ixConversationState.contextLens = 'tokens';
    } else if (descriptor.key === 'sort') {
      ixConversationState.sort = 'tokens';
    } else if (Object.prototype.hasOwnProperty.call(ixConversationState, descriptor.key)) {
      ixConversationState[descriptor.key] = '';
    }
    ixApplyConversationView();
  }
  function ixRenderConversationState(visibleCount, totalCount) {
    const descriptors = ixConversationFilterDescriptors();
    if (ixConversationActiveText) {
      if (visibleCount === 0) {
        ixConversationActiveText.textContent = 'No conversations match the current search and filters.';
      } else if (!descriptors.length) {
        ixConversationActiveText.textContent = `Showing all ${totalCount} conversations by token share.`;
      } else {
        ixConversationActiveText.textContent = `Showing ${visibleCount} of ${totalCount} conversations with the current view settings.`;
      }
    }
    if (ixConversationActiveChips) {
      ixConversationActiveChips.innerHTML = '';
      descriptors.forEach(descriptor => {
        const chip = document.createElement('button');
        chip.type = 'button';
        chip.className = 'conversation-active-chip';
        chip.setAttribute('aria-label', `Remove ${descriptor.label}`);
        const label = document.createElement('span');
        label.textContent = descriptor.label;
        const action = document.createElement('span');
        action.className = 'conversation-active-chip-action';
        action.textContent = 'Clear';
        chip.appendChild(label);
        chip.appendChild(action);
        chip.addEventListener('click', () => ixClearConversationDescriptor(descriptor));
        ixConversationActiveChips.appendChild(chip);
      });
    }
    if (ixConversationResetButton) {
      ixConversationResetButton.classList.toggle('hidden', !ixConversationHasActiveOverrides());
    }
  }
  function ixApplyConversationView() {
    const list = document.querySelector('.conversation-list');
    if (!list) return;
    const rows = Array.from(list.querySelectorAll('[data-conversation-button]'));
    if (!rows.length) return;
    const selected = list.querySelector('[data-conversation-button].active');
    const normalizedQuery = (ixConversationState.query || '').trim().toLowerCase();
    const visibleRows = rows.filter(row =>
      (!normalizedQuery || ixConversationSearchText(row).includes(normalizedQuery))
      && ixConversationMatchesFilters(row));
    const hiddenRows = rows.filter(row => !visibleRows.includes(row));
    visibleRows.sort((left, right) => {
      const delta = ixConversationSortValue(right, ixConversationState.sort) - ixConversationSortValue(left, ixConversationState.sort);
      if (delta !== 0) return delta;
      const leftRank = Number((left.getAttribute('data-detail-rank') || '#0').replace('#', '')) || 0;
      const rightRank = Number((right.getAttribute('data-detail-rank') || '#0').replace('#', '')) || 0;
      return leftRank - rightRank;
    });
    const orderedRows = visibleRows.concat(hiddenRows);
    const fragment = document.createDocumentFragment();
    orderedRows.forEach(row => {
      row.classList.toggle('hidden', !visibleRows.includes(row));
      fragment.appendChild(row);
    });
    list.appendChild(fragment);
    ixReindexConversationRows(visibleRows);
    ixSetConversationCount(visibleRows.length, rows.length);
    ixRenderConversationState(visibleRows.length, rows.length);
    ixRenderConversationSnapshot(visibleRows, rows);
    ixRenderConversationContextBreakdown(visibleRows);
    ixConversationStateToUrl();
    ixConversationSortButtons.forEach(button => {
      const active = (button.getAttribute('data-conversation-sort') || 'tokens') === ixConversationState.sort;
      button.classList.toggle('active', active);
      button.setAttribute('aria-selected', active ? 'true' : 'false');
    });
    ixApplyConversationFilterButtonState();
    const nextSelection = selected && visibleRows.includes(selected)
      ? selected
      : (visibleRows[0] || null);
    ixApplyConversationSelection(nextSelection);
  }
  document.querySelectorAll('[data-conversation-button]').forEach(button => {
    button.addEventListener('click', () => ixApplyConversationSelection(button));
  });
  ixConversationSortButtons.forEach(button => {
    button.addEventListener('click', () => {
      ixConversationState.sort = button.getAttribute('data-conversation-sort') || 'tokens';
      ixApplyConversationView();
    });
  });
  if (ixConversationSearchInput) {
    ixConversationSearchInput.addEventListener('input', () => {
      ixConversationState.query = ixConversationSearchInput.value || '';
      ixApplyConversationView();
    });
  }
  ixConversationFilterButtons.forEach(button => {
    button.addEventListener('click', () => {
      const group = button.getAttribute('data-conversation-filter-group') || '';
      const value = button.getAttribute('data-conversation-filter-value') || '';
      if (!group) return;
      ixConversationState[group] = ixConversationState[group] === value ? '' : value;
      ixApplyConversationView();
    });
  });
  ixConversationContextLensButtons.forEach(button => {
    button.addEventListener('click', () => {
      ixConversationState.contextLens = button.getAttribute('data-conversation-context-lens') || 'tokens';
      ixApplyConversationView();
    });
  });
  if (ixConversationResetButton) {
    ixConversationResetButton.addEventListener('click', () => {
      ixResetConversationView();
      ixApplyConversationView();
    });
  }
  ixRestoreConversationStateFromUrl();
  ixReindexConversationRows(Array.from(document.querySelectorAll('[data-conversation-button]')));
  ixApplyConversationView();
  document.querySelectorAll('.github-lens-tab').forEach(button => {
    button.addEventListener('click', () => {
      const shell = button.closest('.github-impact-shell');
      if (!shell) return;
      const target = button.getAttribute('data-github-lens') || 'impact';
      shell.querySelectorAll('.github-lens-tab').forEach(other => other.classList.toggle('active', other === button));
      shell.querySelectorAll('.github-lens-panel').forEach(panel => {
        panel.classList.toggle('active', panel.getAttribute('data-github-lens-content') === target);
      });
    });
  });
  document.querySelectorAll('.github-owner-chip').forEach(button => {
    button.addEventListener('click', () => {
      const shell = button.closest('.github-owner-explorer');
      if (!shell) return;
      const target = button.getAttribute('data-github-owner') || 'all';
      shell.querySelectorAll('.github-owner-chip').forEach(other => other.classList.toggle('active', other === button));
      shell.querySelectorAll('.github-owner-panel').forEach(panel => {
        panel.classList.toggle('active', panel.getAttribute('data-github-owner-content') === target);
      });
    });
  });
  document.querySelectorAll('.github-repo-sort-tab').forEach(button => {
    button.addEventListener('click', () => {
      const shell = button.closest('.github-impact-shell');
      if (!shell) return;
      const target = button.getAttribute('data-github-repo-sort') || 'stars';
      shell.querySelectorAll('.github-repo-sort-tab').forEach(other => other.classList.toggle('active', other === button));
      shell.querySelectorAll('.github-repo-sort-panel').forEach(panel => {
        panel.classList.toggle('active', panel.getAttribute('data-github-repo-sort-content') === target);
      });
    });
  });
  function ixApplyMode(mode) {
    ixModes.forEach(button => {
      const active = button.getAttribute('data-mode') === mode;
      button.classList.toggle('active', active);
      button.setAttribute('aria-selected', active ? 'true' : 'false');
    });
    ixPanels.forEach(panel => {
      const preview = panel.querySelector('.supporting-preview');
      const summary = panel.querySelector('.supporting-summary');
      if (preview) preview.classList.toggle('hidden', mode !== 'preview');
      if (summary) summary.classList.toggle('active', mode === 'summary');
    });
  }
  ixTabs.forEach(tab => {
    tab.addEventListener('click', () => {
      const target = tab.getAttribute('data-target');
      ixTabs.forEach(other => {
        const active = other === tab;
        other.classList.toggle('active', active);
        other.setAttribute('aria-selected', active ? 'true' : 'false');
      });
      ixPanels.forEach(panel => panel.classList.toggle('active', panel.id === `panel-${target}`));
    });
  });
  ixModes.forEach(button => {
    button.addEventListener('click', () => {
      const mode = button.getAttribute('data-mode') || 'preview';
      ixApplyMode(mode);
      if (ixSupportingModeController) {
        ixSupportingModeController.writeStoredMode(mode);
      }
    });
  });
  ixApplyMode(ixSupportingModeController ? ixSupportingModeController.resolveInitialMode() : 'preview');
})();
