(() => {
  const ixBreakdownBootstrap = window.IntelligenceXReportRuntime
    ? window.IntelligenceXReportRuntime.readBootstrap()
    : {};

  const ixBreakdownThemeKey = ixBreakdownBootstrap.themeKey || 'ix-usage-report-theme';
  const ixBreakdownDefaultTheme = ixBreakdownBootstrap.defaultTheme || 'system';
  const ixBreakdownModeButtons = document.querySelectorAll('.mode-button');
  const ixBreakdownPreview = document.querySelector('.preview');
  const ixBreakdownSummary = document.querySelector('.summary');

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
    button.addEventListener('click', () => ixBreakdownApplyMode(button.getAttribute('data-mode') || 'preview'));
  });

  if (window.IntelligenceXReportRuntime) {
    window.IntelligenceXReportRuntime.initThemeController({
      themeKey: ixBreakdownThemeKey,
      defaultTheme: ixBreakdownDefaultTheme
    });
  }
})();
