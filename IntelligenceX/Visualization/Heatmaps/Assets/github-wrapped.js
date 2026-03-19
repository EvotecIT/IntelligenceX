(() => {
  const runtime = window.IntelligenceXReportRuntime;
  if (!runtime) {
    return;
  }

  const bootstrap = runtime.readBootstrap();
  let accentController = null;
  const themeController = runtime.initThemeController({
    themeKey: bootstrap.themeKey || 'ix-usage-report-theme',
    defaultTheme: bootstrap.defaultTheme || 'system',
    onApply: () => {
      if (accentController) {
        accentController.reapply();
      }
    }
  });
  accentController = runtime.initAccentController({
    accentKey: bootstrap.accentKey || 'ix-usage-report-accent',
    defaultAccent: bootstrap.defaultAccent || 'violet',
    resolveTheme: () => document.documentElement.getAttribute('data-theme') || themeController.resolveTheme(bootstrap.defaultTheme || 'system')
  });

  runtime.initToggleGroup({
    buttonSelector: '.owner-chip',
    panelSelector: '.owner-panel',
    buttonAttr: 'data-owner-panel',
    panelAttr: 'data-owner-panel-content'
  });
})();
