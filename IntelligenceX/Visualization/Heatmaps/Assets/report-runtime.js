window.IntelligenceXReportRuntime = (() => {
  function readBootstrap(nodeId = 'ix-report-bootstrap') {
    const node = document.getElementById(nodeId);
    if (!node) {
      return {};
    }

    try {
      return JSON.parse(node.textContent || '{}');
    } catch (_) {
      return {};
    }
  }

  function initThemeController(options = {}) {
    const themeKey = options.themeKey || 'ix-usage-report-theme';
    const defaultTheme = options.defaultTheme || 'system';
    const switchSelector = options.switchSelector || '.theme-switch';
    const themeMedia = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;
    const switches = Array.from(document.querySelectorAll(switchSelector));

    function resolveTheme(target) {
      if (target === 'light' || target === 'dark') {
        return target;
      }

      return themeMedia && themeMedia.matches ? 'dark' : 'light';
    }

    function applyAssetTargets(resolved) {
      document.querySelectorAll('img[data-light-src][data-dark-src]').forEach((img) => {
        const next = resolved === 'dark' ? img.getAttribute('data-dark-src') : img.getAttribute('data-light-src');
        if (next) {
          img.setAttribute('src', next);
        }
      });
      document.querySelectorAll('a[data-light-href][data-dark-href]').forEach((link) => {
        const next = resolved === 'dark' ? link.getAttribute('data-dark-href') : link.getAttribute('data-light-href');
        if (next) {
          link.setAttribute('href', next);
        }
      });
    }

    function applyTheme(target, persist) {
      const resolved = resolveTheme(target);
      document.documentElement.setAttribute('data-theme', resolved);
      switches.forEach((button) => {
        const buttonTarget = button.getAttribute('data-theme-target') || 'system';
        const active = buttonTarget === target;
        button.classList.toggle('active', active);
        button.setAttribute('aria-selected', active ? 'true' : 'false');
      });
      applyAssetTargets(resolved);
      if (typeof options.onApply === 'function') {
        options.onApply(target, resolved);
      }
      if (persist) {
        try {
          localStorage.setItem(themeKey, target);
        } catch (_) {
        }
      }
    }

    const savedTheme = (() => {
      try {
        return localStorage.getItem(themeKey) || defaultTheme;
      } catch (_) {
        return defaultTheme;
      }
    })();

    switches.forEach((button) => {
      button.addEventListener('click', () => applyTheme(button.getAttribute('data-theme-target') || 'system', true));
    });

    if (themeMedia) {
      const listener = () => {
        const current = (() => {
          try {
            return localStorage.getItem(themeKey) || defaultTheme;
          } catch (_) {
            return defaultTheme;
          }
        })();
        if (current === 'system') {
          applyTheme('system', false);
        }
      };
      if (themeMedia.addEventListener) {
        themeMedia.addEventListener('change', listener);
      } else if (themeMedia.addListener) {
        themeMedia.addListener(listener);
      }
    }

    applyTheme(savedTheme, false);
    return {
      applyTheme,
      resolveTheme,
      getSavedTheme: () => savedTheme
    };
  }

  function initAccentController(options = {}) {
    const accentKey = options.accentKey || 'ix-usage-report-accent';
    const defaultAccent = options.defaultAccent || 'violet';
    const switchSelector = options.switchSelector || '.accent-switch';
    const switches = Array.from(document.querySelectorAll(switchSelector));
    let currentAccent = defaultAccent;

    function normalizeAccent(target) {
      switch (target) {
        case 'ocean':
        case 'forest':
        case 'sunset':
        case 'violet':
          return target;
        default:
          return 'violet';
      }
    }

    function resolveTheme() {
      if (typeof options.resolveTheme === 'function') {
        return options.resolveTheme() || 'light';
      }

      return document.documentElement.getAttribute('data-theme') || 'light';
    }

    function getPalette(target, resolvedTheme) {
      const dark = resolvedTheme === 'dark';
      switch (normalizeAccent(target)) {
        case 'ocean':
          return dark
            ? {
                '--accent': '#72b7ff',
                '--accent-strong': '#2f86ff',
                '--tone-current-bg': 'rgba(47,134,255,.18)',
                '--tone-current-border': 'rgba(47,134,255,.28)',
                '--tone-current-ink': '#c9e3ff'
              }
            : {
                '--accent': '#4b9bff',
                '--accent-strong': '#1d62d8',
                '--tone-current-bg': 'rgba(47,134,255,.12)',
                '--tone-current-border': 'rgba(47,134,255,.22)',
                '--tone-current-ink': '#1d62d8'
              };
        case 'forest':
          return dark
            ? {
                '--accent': '#63c78f',
                '--accent-strong': '#2aa06d',
                '--tone-current-bg': 'rgba(42,160,109,.18)',
                '--tone-current-border': 'rgba(42,160,109,.28)',
                '--tone-current-ink': '#caf6de'
              }
            : {
                '--accent': '#46b57d',
                '--accent-strong': '#1f7f56',
                '--tone-current-bg': 'rgba(42,160,109,.12)',
                '--tone-current-border': 'rgba(42,160,109,.22)',
                '--tone-current-ink': '#1f7f56'
              };
        case 'sunset':
          return dark
            ? {
                '--accent': '#f2a06e',
                '--accent-strong': '#dc6a3c',
                '--tone-current-bg': 'rgba(220,106,60,.18)',
                '--tone-current-border': 'rgba(220,106,60,.28)',
                '--tone-current-ink': '#ffd9c6'
              }
            : {
                '--accent': '#ee8554',
                '--accent-strong': '#b44a1f',
                '--tone-current-bg': 'rgba(220,106,60,.12)',
                '--tone-current-border': 'rgba(220,106,60,.22)',
                '--tone-current-ink': '#b44a1f'
              };
        default:
          return dark
            ? {
                '--accent': '#9da9ff',
                '--accent-strong': '#6268f1',
                '--tone-current-bg': 'rgba(129,140,248,.18)',
                '--tone-current-border': 'rgba(129,140,248,.28)',
                '--tone-current-ink': '#c7d2fe'
              }
            : {
                '--accent': '#6268f1',
                '--accent-strong': '#4740d1',
                '--tone-current-bg': 'rgba(98,104,241,.12)',
                '--tone-current-border': 'rgba(98,104,241,.2)',
                '--tone-current-ink': '#4740d1'
              };
      }
    }

    function applyAccent(target, persist) {
      currentAccent = normalizeAccent(target);
      const palette = getPalette(currentAccent, resolveTheme());
      Object.entries(palette).forEach(([key, value]) => {
        document.documentElement.style.setProperty(key, value);
      });
      document.documentElement.setAttribute('data-accent', currentAccent);
      switches.forEach((button) => {
        const buttonTarget = normalizeAccent(button.getAttribute('data-accent-target') || 'violet');
        const active = buttonTarget === currentAccent;
        button.classList.toggle('active', active);
        button.setAttribute('aria-selected', active ? 'true' : 'false');
      });
      if (typeof options.onApply === 'function') {
        options.onApply(currentAccent);
      }
      if (persist) {
        try {
          localStorage.setItem(accentKey, currentAccent);
        } catch (_) {
        }
      }
    }

    const savedAccent = (() => {
      try {
        return normalizeAccent(localStorage.getItem(accentKey) || defaultAccent);
      } catch (_) {
        return normalizeAccent(defaultAccent);
      }
    })();

    switches.forEach((button) => {
      button.addEventListener('click', () => applyAccent(button.getAttribute('data-accent-target') || 'violet', true));
    });

    applyAccent(savedAccent, false);
    return {
      applyAccent,
      reapply: () => applyAccent(currentAccent, false),
      getCurrentAccent: () => currentAccent
    };
  }

  function initToggleGroup(options = {}) {
    const buttonSelector = options.buttonSelector;
    const panelSelector = options.panelSelector;
    const buttonAttr = options.buttonAttr;
    const panelAttr = options.panelAttr;
    const scopeRoot = options.scopeSelector
      ? document.querySelector(options.scopeSelector)
      : document;

    if (!scopeRoot || !buttonSelector || !panelSelector || !buttonAttr || !panelAttr) {
      return null;
    }

    const activeClass = options.activeClass || 'active';
    const buttons = Array.from(scopeRoot.querySelectorAll(buttonSelector));
    const panels = Array.from(scopeRoot.querySelectorAll(panelSelector));

    if (buttons.length === 0 || panels.length === 0) {
      return null;
    }

    const apply = (target) => {
      buttons.forEach((button) => {
        const active = button.getAttribute(buttonAttr) === target;
        button.classList.toggle(activeClass, active);
        button.setAttribute('aria-selected', active ? 'true' : 'false');
      });

      panels.forEach((panel) => {
        panel.classList.toggle(activeClass, panel.getAttribute(panelAttr) === target);
      });
    };

    buttons.forEach((button) => {
      button.addEventListener('click', () => {
        const target = button.getAttribute(buttonAttr);
        if (target) {
          apply(target);
        }
      });
    });

    const defaultTarget =
      buttons.find((button) => button.classList.contains(activeClass))?.getAttribute(buttonAttr) ||
      buttons[0]?.getAttribute(buttonAttr);

    if (defaultTarget) {
      apply(defaultTarget);
    }

    return { apply };
  }

  function createStoredModeController(options = {}) {
    const storageKey = options.storageKey;
    const defaultMode = options.defaultMode || 'preview';
    const compactMode = options.compactMode || 'summary';
    const compactMediaQuery = options.compactMediaQuery || '(max-width: 720px)';

    function readStoredMode() {
      if (!storageKey) {
        return null;
      }

      try {
        return localStorage.getItem(storageKey);
      } catch (_) {
        return null;
      }
    }

    function writeStoredMode(mode) {
      if (!storageKey) {
        return;
      }

      try {
        localStorage.setItem(storageKey, mode);
      } catch (_) {
      }
    }

    function resolveInitialMode() {
      const stored = readStoredMode();
      if (stored) {
        return stored;
      }

      if (window.matchMedia && window.matchMedia(compactMediaQuery).matches) {
        return compactMode;
      }

      return defaultMode;
    }

    return {
      readStoredMode,
      writeStoredMode,
      resolveInitialMode
    };
  }

  return {
    readBootstrap,
    initThemeController,
    initAccentController,
    initToggleGroup,
    createStoredModeController
  };
})();
