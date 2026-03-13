(() => {
  const runtime = window.IntelligenceXReportRuntime;
  if (!runtime) {
    return;
  }

  runtime.readBootstrap();
  runtime.initToggleGroup({
    buttonSelector: '.owner-chip',
    panelSelector: '.owner-panel',
    buttonAttr: 'data-owner-panel',
    panelAttr: 'data-owner-panel-content'
  });
})();
