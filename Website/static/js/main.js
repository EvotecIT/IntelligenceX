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
    var surfaceText = isLight ? '#0F172A' : '#D6E2F0';
    var nodeText = isLight ? '#0F172A' : '#0B1220';
    return {
      startOnLoad: false,
      securityLevel: 'strict',
      theme: 'base',
      flowchart: { curve: 'basis' },
      themeVariables: {
        background: getCssVar('--pf-code-bg', isLight ? '#F1F5F9' : '#0B1220'),
        primaryColor: getCssVar('--pf-card-bg', isLight ? '#E2E8F0' : '#1E293B'),
        textColor: surfaceText,
        primaryTextColor: nodeText,
        secondaryTextColor: nodeText,
        tertiaryTextColor: nodeText,
        clusterTextColor: surfaceText,
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

  function normalizeRoute(value) {
    var route = (value || '').toString().trim();
    if (!route) return '/';
    if (!route.startsWith('/')) route = '/' + route;
    route = route.replace(/\/{2,}/g, '/');
    if (!route.endsWith('/')) route = route + '/';
    return route.toLowerCase();
  }

  function shortenText(value, max) {
    var text = (value || '').toString().trim();
    if (!text) return '';
    if (text.length <= max) return text;
    return text.slice(0, Math.max(12, max - 1)).trimEnd() + '…';
  }

  function unwrapSearchEntries(payload) {
    if (Array.isArray(payload)) return payload;
    if (payload && Array.isArray(payload.entries)) return payload.entries;
    if (payload && Array.isArray(payload.items)) return payload.items;
    return [];
  }

  function parseEntryDate(value) {
    if (!value) return 0;
    var raw = value instanceof Date ? value.toISOString() : value.toString();
    var parsed = Date.parse(raw);
    return Number.isFinite(parsed) ? parsed : 0;
  }

  function normalizeBlogEntries(entries) {
    return entries
      .filter(function (entry) {
        if (!entry || typeof entry !== 'object') return false;
        var url = normalizeRoute(entry.url || entry.output_path || '');
        if (url === '/blog/') return false;

        var collection = (entry.collection || '').toString().toLowerCase();
        var kind = (entry.kind || '').toString().toLowerCase();
        var looksLikeBlogPath = url.startsWith('/blog/');

        if (collection && collection !== 'blog') return false;
        if (kind && kind !== 'page') return false;
        if (!collection && !looksLikeBlogPath) return false;
        return true;
      })
      .map(function (entry) {
        return Object.assign({}, entry, {
          normalizedUrl: normalizeRoute(entry.url || entry.output_path || ''),
          parsedDate: parseEntryDate(entry.date)
        });
      })
      .sort(function (a, b) {
        if (a.parsedDate !== b.parsedDate) return b.parsedDate - a.parsedDate;
        return (a.title || '').localeCompare((b.title || ''));
      });
  }

  var relatedBlogEntriesPromise = null;
  function loadRelatedBlogEntries() {
    if (relatedBlogEntriesPromise) return relatedBlogEntriesPromise;

    function fetchJson(url) {
      return fetch(url, { credentials: 'same-origin' }).then(function (res) {
        if (!res.ok) throw new Error('HTTP ' + res.status);
        return res.json();
      });
    }

    relatedBlogEntriesPromise = fetchJson('/search/collections/blog/index.json')
      .then(unwrapSearchEntries)
      .catch(function () {
        return fetchJson('/search/index.json').then(unwrapSearchEntries);
      })
      .catch(function () {
        return [];
      });

    return relatedBlogEntriesPromise;
  }

  function updateBlogSequenceNav(entries) {
    var articles = Array.from(document.querySelectorAll('.ix-article[data-collection="blog"][data-kind="Page"]'));
    if (!articles.length || !entries.length) return;

    articles.forEach(function (article) {
      var currentPath = normalizeRoute(article.getAttribute('data-route'));
      var idx = entries.findIndex(function (entry) { return entry.normalizedUrl === currentPath; });
      if (idx < 0) return;

      var newerLink = article.querySelector('[data-blog-nav="newer"]');
      var olderLink = article.querySelector('[data-blog-nav="older"]');

      function setNavLink(link, entry, label) {
        if (!link) return;
        if (!entry) {
          link.classList.add('is-disabled');
          link.setAttribute('aria-disabled', 'true');
          link.setAttribute('href', '#');
          link.textContent = label;
          return;
        }
        link.classList.remove('is-disabled');
        link.removeAttribute('aria-disabled');
        link.href = entry.url || entry.output_path || '#';
        link.textContent = label + ': ' + (entry.title || 'Untitled');
      }

      setNavLink(newerLink, idx > 0 ? entries[idx - 1] : null, 'Newer post');
      setNavLink(olderLink, idx < entries.length - 1 ? entries[idx + 1] : null, 'Older post');
    });
  }

  function renderRelatedPosts() {
    var containers = Array.from(document.querySelectorAll('.ix-related-posts[data-current-path]'));
    loadRelatedBlogEntries().then(function (entries) {
      var normalizedEntries = normalizeBlogEntries(entries);
      updateBlogSequenceNav(normalizedEntries);
      if (!containers.length) return;

      containers.forEach(function (container) {
        var currentPath = normalizeRoute(container.getAttribute('data-current-path'));
        var max = parseInt(container.getAttribute('data-max') || '4', 10);
        if (!Number.isFinite(max) || max <= 0) max = 4;

        var posts = normalizedEntries
          .filter(function (entry) { return entry.normalizedUrl !== currentPath; })
          .slice(0, max);

        container.innerHTML = '';
        if (!posts.length) {
          var empty = document.createElement('p');
          empty.className = 'ix-related-posts-empty';
          empty.textContent = 'No related posts available yet.';
          container.appendChild(empty);
          return;
        }

        posts.forEach(function (entry) {
          var link = document.createElement('a');
          link.className = 'ix-related-post-card';
          link.href = entry.url || '#';

          var meta = entry.meta && typeof entry.meta === 'object' ? entry.meta : {};
          var image = (entry.image || meta.image || entry.cover || entry.socialImage || '').toString().trim();
          if (image) {
            var imageNode = document.createElement('img');
            imageNode.className = 'ix-related-post-media';
            imageNode.src = image;
            imageNode.alt = (entry.title || 'Blog post').toString();
            imageNode.loading = 'lazy';
            imageNode.decoding = 'async';
            link.appendChild(imageNode);
          }

          var body = document.createElement('div');
          body.className = 'ix-related-post-body';

          var title = document.createElement('span');
          title.className = 'ix-related-post-title';
          title.textContent = (entry.title || 'Untitled post').toString();
          body.appendChild(title);

          var summary = shortenText(entry.description || entry.snippet || '', 140);
          if (summary) {
            var summaryNode = document.createElement('span');
            summaryNode.className = 'ix-related-post-summary';
            summaryNode.textContent = summary;
            body.appendChild(summaryNode);
          }

          link.appendChild(body);
          container.appendChild(link);
        });
      });
    });
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

  function highlightCodeBlocks(scope) {
    var prism = window.Prism;
    if (!prism) return;
    if (prism.plugins && prism.plugins.autoloader) {
      prism.plugins.autoloader.languages_path = '/assets/prism/components/';
    }
    if (scope && typeof prism.highlightAllUnder === 'function') {
      prism.highlightAllUnder(scope);
      return;
    }
    if (typeof prism.highlightAll === 'function') {
      prism.highlightAll();
    }
  }

  window.addEventListener('load', function () {
    highlightCodeBlocks(document);
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
        if (panel) {
          panel.classList.add('active');
          highlightCodeBlocks(panel);
        }
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

  renderRelatedPosts();
  renderMermaidDiagrams();
});
