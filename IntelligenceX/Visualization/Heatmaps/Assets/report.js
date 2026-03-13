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
    let ixCurrentMode = 'preview';
    if (window.IntelligenceXReportRuntime) {
      window.IntelligenceXReportRuntime.initThemeController({
        themeKey: ixBootstrap.themeKey || 'ix-usage-report-theme',
        defaultTheme: ixBootstrap.defaultTheme || 'system'
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
      ixCurrentMode = mode;
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
      button.addEventListener('click', () => ixApplyMode(button.getAttribute('data-mode') || 'preview'));
    });
