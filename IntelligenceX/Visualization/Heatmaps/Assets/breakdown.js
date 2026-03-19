(() => {
  const ixBreakdownBootstrap = window.IntelligenceXReportRuntime
    ? window.IntelligenceXReportRuntime.readBootstrap()
    : {};

  const ixBreakdownThemeKey = ixBreakdownBootstrap.themeKey || 'ix-usage-report-theme';
  const ixBreakdownDefaultTheme = ixBreakdownBootstrap.defaultTheme || 'system';
  const ixBreakdownModeButtons = document.querySelectorAll('.mode-button');
  const ixBreakdownPreview = document.querySelector('.preview');
  const ixBreakdownSummary = document.querySelector('.summary');
  const ixBreakdownModeController = window.IntelligenceXReportRuntime
    ? window.IntelligenceXReportRuntime.createStoredModeController({
      storageKey: 'ix-usage-breakdown-mode',
      defaultMode: 'preview',
      compactMode: 'summary'
    })
    : null;
  let ixBreakdownAccentController = null;

  function ixBreakdownApplyMode(mode) {
    ixBreakdownModeButtons.forEach((button) => {
      const active = button.getAttribute('data-mode') === mode;
      button.classList.toggle('active', active);
      button.setAttribute('aria-selected', active ? 'true' : 'false');
    });
    if (ixBreakdownPreview) {
      ixBreakdownPreview.classList.toggle('hidden', mode !== 'preview');
    }
    if (ixBreakdownSummary) {
      ixBreakdownSummary.classList.toggle('active', mode === 'summary');
    }
  }

  ixBreakdownModeButtons.forEach((button) => {
    button.addEventListener('click', () => {
      const mode = button.getAttribute('data-mode') || 'preview';
      ixBreakdownApplyMode(mode);
      if (ixBreakdownModeController) {
        ixBreakdownModeController.writeStoredMode(mode);
      }
    });
  });

  if (window.IntelligenceXReportRuntime) {
    const ixBreakdownThemeController = window.IntelligenceXReportRuntime.initThemeController({
      themeKey: ixBreakdownThemeKey,
      defaultTheme: ixBreakdownDefaultTheme,
      onApply: () => {
        if (ixBreakdownAccentController) {
          ixBreakdownAccentController.reapply();
        }
      }
    });
    ixBreakdownAccentController = window.IntelligenceXReportRuntime.initAccentController({
      accentKey: ixBreakdownBootstrap.accentKey || 'ix-usage-report-accent',
      defaultAccent: ixBreakdownBootstrap.defaultAccent || 'violet',
      resolveTheme: () => document.documentElement.getAttribute('data-theme') || ixBreakdownThemeController.resolveTheme(ixBreakdownDefaultTheme)
    });
  }

  ixBreakdownApplyMode(ixBreakdownModeController ? ixBreakdownModeController.resolveInitialMode() : 'preview');
})();
