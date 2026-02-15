// Theme toggle
(function() {
  var stored = localStorage.getItem('theme');
  var preferred = stored || (window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark');
  document.documentElement.setAttribute('data-theme', preferred);
})();

// Mobile nav toggle
document.addEventListener('DOMContentLoaded', function () {
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

  async function renderMermaidDiagrams() {
    // Enable rendered Mermaid only on pages we explicitly modernized.
    // This avoids CI rendered-warning regressions from older Mermaid snippets.
    if (!window.location.pathname.startsWith('/docs/chat/architecture/')) {
      return;
    }

    var blocks = Array.from(document.querySelectorAll('pre code.language-mermaid'));
    if (!blocks.length) {
      return;
    }

    var loaded = await loadScript('https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.min.js');
    if (!loaded || !window.mermaid) {
      return;
    }

    var isLight = document.documentElement.getAttribute('data-theme') === 'light';
    window.mermaid.initialize({
      startOnLoad: false,
      securityLevel: 'strict',
      theme: isLight ? 'default' : 'dark'
    });

    blocks.forEach(function (code) {
      var pre = code.closest('pre');
      if (!pre) return;

      var source = (code.textContent || '').trim();
      if (!source) return;

      var host = document.createElement('div');
      host.className = 'mermaid-diagram';
      var diagram = document.createElement('div');
      diagram.className = 'mermaid';
      diagram.textContent = source;
      host.appendChild(diagram);
      pre.replaceWith(host);
    });

    try {
      await window.mermaid.run({ querySelector: '.mermaid-diagram .mermaid' });
    } catch (err) {
      console.warn('Mermaid render failed', err);
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
    });
  });

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
