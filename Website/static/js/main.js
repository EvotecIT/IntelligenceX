// Theme toggle
(function() {
  var stored = localStorage.getItem('theme');
  var preferred = stored || (window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark');
  document.documentElement.setAttribute('data-theme', preferred);
})();

// Mobile nav toggle
document.addEventListener('DOMContentLoaded', function () {
  var mermaidScriptUrl = 'https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.min.js';
  var mermaidLoadPromise = null;
  var mermaidThemeObserver = null;

  function loadScript(src) {
    return new Promise(function (resolve) {
      var existing = document.querySelector('script[data-dynamic-src="' + src + '"]');
      if (existing) {
        if (existing.getAttribute('data-loaded') === 'true') {
          resolve(true);
          return;
        }
        existing.addEventListener('load', function () { resolve(true); }, { once: true });
        existing.addEventListener('error', function () { resolve(false); }, { once: true });
        return;
      }

      var script = document.createElement('script');
      script.src = src;
      script.async = true;
      script.defer = true;
      script.setAttribute('data-dynamic-src', src);
      script.addEventListener('load', function () {
        script.setAttribute('data-loaded', 'true');
        resolve(true);
      }, { once: true });
      script.addEventListener('error', function () {
        resolve(false);
      }, { once: true });
      document.head.appendChild(script);
    });
  }

  function getThemeName() {
    var theme = document.documentElement.getAttribute('data-theme');
    return theme === 'light' ? 'light' : 'dark';
  }

  function getCssVar(name, fallback) {
    var value = getComputedStyle(document.documentElement).getPropertyValue(name);
    if (!value) return fallback;
    var trimmed = value.trim();
    return trimmed || fallback;
  }

  function getMermaidConfig() {
    var isLight = getThemeName() === 'light';
    return {
      startOnLoad: false,
      securityLevel: 'strict',
      theme: 'base',
      flowchart: { curve: 'basis' },
      themeVariables: {
        background: getCssVar('--pf-code-bg', isLight ? '#F1F5F9' : '#0B1220'),
        primaryColor: getCssVar('--pf-card-bg', isLight ? '#E2E8F0' : '#1E293B'),
        primaryTextColor: getCssVar('--pf-ink-strong', isLight ? '#0F172A' : '#F8FAFC'),
        primaryBorderColor: getCssVar('--pf-accent', isLight ? '#0891B2' : '#06B6D4'),
        lineColor: getCssVar('--pf-muted', isLight ? '#64748B' : '#94A3B8'),
        secondaryColor: getCssVar('--pf-bg-alt', isLight ? '#E2E8F0' : '#111827'),
        tertiaryColor: getCssVar('--pf-glow-primary', isLight ? '#DBEAFE' : '#1F2937'),
        clusterBkg: getCssVar('--pf-bg-alt', isLight ? '#E2E8F0' : '#111827'),
        clusterBorder: getCssVar('--pf-border', isLight ? '#94A3B8' : '#334155'),
        fontFamily: getCssVar('--pf-font-body', 'Segoe UI, sans-serif'),
        fontSize: '15px'
      }
    };
  }

  function ensureMermaidLoaded() {
    if (window.mermaid) {
      return Promise.resolve(true);
    }
    if (!mermaidLoadPromise) {
      mermaidLoadPromise = loadScript(mermaidScriptUrl);
    }
    return mermaidLoadPromise;
  }

  function normalizeMermaidBlocks() {
    var blocks = Array.from(document.querySelectorAll('pre code.language-mermaid'));
    blocks.forEach(function (code) {
      var pre = code.closest('pre');
      if (!pre || pre.parentElement && pre.parentElement.classList.contains('mermaid-diagram')) {
        return;
      }

      var source = (code.textContent || '').trim();
      if (!source) return;

      var host = document.createElement('div');
      host.className = 'mermaid-diagram';
      host.setAttribute('data-mermaid-source', source);
      pre.replaceWith(host);
    });
  }

  async function renderMermaidDiagrams() {
    normalizeMermaidBlocks();
    var hosts = Array.from(document.querySelectorAll('.mermaid-diagram[data-mermaid-source]'));
    if (!hosts.length) {
      return;
    }

    var loaded = await ensureMermaidLoaded();
    if (!loaded || !window.mermaid) {
      return;
    }

    window.mermaid.initialize(getMermaidConfig());

    var nodes = [];
    hosts.forEach(function (host) {
      var source = host.getAttribute('data-mermaid-source') || '';
      host.classList.remove('mermaid-diagram-failed');
      host.innerHTML = '';

      var diagram = document.createElement('div');
      diagram.className = 'mermaid';
      diagram.textContent = source;
      host.appendChild(diagram);
      nodes.push(diagram);
    });

    try {
      await window.mermaid.run({ nodes: nodes });
    } catch (err) {
      console.warn('Mermaid render failed', err);
      hosts.forEach(function (host) {
        if (host.querySelector('svg')) {
          return;
        }
        host.classList.add('mermaid-diagram-failed');
        var fallback = document.createElement('pre');
        var code = document.createElement('code');
        code.className = 'language-mermaid';
        code.textContent = host.getAttribute('data-mermaid-source') || '';
        fallback.appendChild(code);
        host.innerHTML = '';
        host.appendChild(fallback);
      });
    }
  }

  var toggle = document.querySelector('.ix-nav-toggle');
  if (toggle) {
    toggle.addEventListener('click', function () {
      document.body.classList.toggle('nav-open');
    });
  }

  // Theme toggle button
  document.querySelectorAll('.ix-theme-toggle').forEach(function (btn) {
    btn.addEventListener('click', function () {
      var current = document.documentElement.getAttribute('data-theme') || 'dark';
      var next = current === 'dark' ? 'light' : 'dark';
      document.documentElement.setAttribute('data-theme', next);
      localStorage.setItem('theme', next);
      renderMermaidDiagrams();
    });
  });

  if (!mermaidThemeObserver) {
    mermaidThemeObserver = new MutationObserver(function (mutations) {
      mutations.forEach(function (mutation) {
        if (mutation.type === 'attributes' && mutation.attributeName === 'data-theme') {
          renderMermaidDiagrams();
        }
      });
    });
    mermaidThemeObserver.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ['data-theme']
    });
  }

  // Code tabs
  document.querySelectorAll('.code-tabs').forEach(function (tabBar) {
    var tabs = tabBar.querySelectorAll('.code-tab');
    var container = tabBar.parentElement;
    var panels = container.querySelectorAll('.code-panel');

    tabs.forEach(function (tab) {
      tab.addEventListener('click', function () {
        var target = tab.getAttribute('data-tab');
        tabs.forEach(function (t) { t.classList.remove('active'); });
        panels.forEach(function (p) { p.classList.remove('active'); });
        tab.classList.add('active');
        var panel = container.querySelector('[data-panel="' + target + '"]');
        if (panel) panel.classList.add('active');
      });
    });
  });

  // Copy command button
  document.querySelectorAll('[data-copy]').forEach(function (btn) {
    btn.addEventListener('click', function () {
      var text = btn.getAttribute('data-copy');
      if (navigator.clipboard) {
        navigator.clipboard.writeText(text);
      }
    });
  });

  // Smooth scroll for anchor links
  document.querySelectorAll('a[href^="#"]').forEach(function (a) {
    a.addEventListener('click', function (e) {
      var id = a.getAttribute('href').slice(1);
      var el = document.getElementById(id);
      if (el) {
        e.preventDefault();
        el.scrollIntoView({ behavior: 'smooth', block: 'start' });
      }
    });
  });

  renderMermaidDiagrams();
});
