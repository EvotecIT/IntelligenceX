using System.Net;
using System.Text;

namespace IntelligenceX.Visualization.Heatmaps;

#pragma warning disable CS1591

internal static class UsageTelemetryBreakdownHtmlRenderer {
    public static string Render(string reportTitle, string breakdownKey, string breakdownLabel, string? subtitle) {
        var safeTitle = string.IsNullOrWhiteSpace(reportTitle) ? "Usage Overview" : reportTitle.Trim();
        var safeLabel = string.IsNullOrWhiteSpace(breakdownLabel) ? "Breakdown" : breakdownLabel.Trim();
        var safeKey = string.IsNullOrWhiteSpace(breakdownKey) ? "breakdown" : breakdownKey.Trim();
        var summaryHint = string.IsNullOrWhiteSpace(subtitle) ? "Detailed breakdown view." : (subtitle ?? string.Empty).Trim();

        var sb = new StringBuilder(16 * 1024);
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("  <title>").Append(Html(safeTitle)).Append(" · ").Append(Html(safeLabel)).AppendLine("</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    :root { --bg:#f2f2f2; --panel:#ffffff; --panel-soft:#fafafa; --panel-muted:#f6f6f6; --ink:#152038; --muted:#787878; --line:#e5e5e5; --button-bg:#fafafa; --button-hover:#f1f1f1; --button-active:#1d2b48; --button-active-text:#ffffff; --panel-shadow:0 12px 34px rgba(18,24,38,.04); }");
        sb.AppendLine("    html[data-theme='dark'] { --bg:#0f1115; --panel:#171b22; --panel-soft:#1b212b; --panel-muted:#141921; --ink:#f5f7fa; --muted:#9aa4b2; --line:#2b3340; --button-bg:#1b212b; --button-hover:#242c3a; --button-active:#e9edf7; --button-active-text:#111827; --panel-shadow:0 18px 48px rgba(0,0,0,.35); }");
        sb.AppendLine("    * { box-sizing:border-box; }");
        sb.AppendLine("    body { margin:0; background:var(--bg); color:var(--ink); font-family:\"Aptos\",\"IBM Plex Sans\",\"Segoe UI\",sans-serif; }");
        sb.AppendLine("    .page { max-width:1460px; margin:0 auto; padding:32px 36px 48px; }");
        sb.AppendLine("    .toolbar { display:flex; justify-content:space-between; gap:16px; align-items:center; margin-bottom:24px; }");
        sb.AppendLine("    .back-link, .asset-link, .mode-button, .theme-switch { display:inline-flex; align-items:center; justify-content:center; padding:10px 14px; border:1px solid var(--line); border-radius:999px; background:var(--button-bg); color:var(--ink); text-decoration:none; font-size:13px; font-weight:700; cursor:pointer; }");
        sb.AppendLine("    .back-link:hover, .asset-link:hover, .mode-button:hover, .theme-switch:hover { background:var(--button-hover); }");
        sb.AppendLine("    .mode-button.active, .theme-switch.active { background:var(--button-active); color:var(--button-active-text); border-color:var(--button-active); }");
        sb.AppendLine("    .theme-switcher, .mode-switcher, .asset-links { display:flex; flex-wrap:wrap; gap:10px; }");
        sb.AppendLine("    .theme-switch { width:42px; height:42px; padding:0; }");
        sb.AppendLine("    .theme-icon { font-size:18px; line-height:1; }");
        sb.AppendLine("    .hero { display:flex; justify-content:space-between; gap:24px; align-items:flex-end; margin-bottom:20px; }");
        sb.AppendLine("    .hero h1 { margin:0; font-size:34px; line-height:1; letter-spacing:-.03em; }");
        sb.AppendLine("    .hero p { margin:10px 0 0; color:var(--muted); font-size:14px; max-width:72ch; }");
        sb.AppendLine("    .panel { background:linear-gradient(180deg,var(--panel) 0%, var(--panel-soft) 100%); border:1px solid var(--line); border-radius:24px; padding:22px; box-shadow:var(--panel-shadow); }");
        sb.AppendLine("    .panel-toolbar { display:flex; justify-content:space-between; gap:16px; align-items:center; margin-bottom:16px; }");
        sb.AppendLine("    .preview { padding:14px; border-radius:18px; border:1px solid var(--line); background:var(--panel-muted); overflow:auto; }");
        sb.AppendLine("    .preview img { width:100%; min-width:960px; height:auto; display:block; border-radius:12px; background:var(--bg); }");
        sb.AppendLine("    .summary { display:none; padding:18px; border-radius:18px; border:1px solid var(--line); background:var(--panel-soft); }");
        sb.AppendLine("    .summary.active { display:block; }");
        sb.AppendLine("    .preview.hidden { display:none; }");
        sb.AppendLine("    .summary-stats { display:grid; grid-template-columns:repeat(auto-fit,minmax(170px,1fr)); gap:14px; margin-bottom:18px; }");
        sb.AppendLine("    .summary-stat { padding:14px 16px; border:1px solid var(--line); border-radius:16px; background:var(--panel); }");
        sb.AppendLine("    .summary-stat-label { color:var(--muted); font-size:11px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; }");
        sb.AppendLine("    .summary-stat-value { margin-top:6px; font-size:22px; font-weight:800; letter-spacing:-.03em; }");
        sb.AppendLine("    .summary-columns { display:grid; grid-template-columns:1.1fr .9fr; gap:18px; }");
        sb.AppendLine("    .summary-card { padding:16px; border:1px solid var(--line); border-radius:16px; background:var(--panel); }");
        sb.AppendLine("    .summary-card h4 { margin:0 0 10px; font-size:14px; letter-spacing:-.02em; }");
        sb.AppendLine("    .summary-list { display:grid; gap:10px; }");
        sb.AppendLine("    .summary-row { display:grid; grid-template-columns:minmax(0,1fr); gap:10px; }");
        sb.AppendLine("    .summary-row-head { display:flex; justify-content:space-between; gap:12px; align-items:baseline; }");
        sb.AppendLine("    .summary-row-label { font-size:14px; font-weight:700; min-width:0; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }");
        sb.AppendLine("    .summary-row-value, .summary-row-meta, .empty, .note { color:var(--muted); font-size:13px; }");
        sb.AppendLine("    .summary-row-bar { margin-top:8px; width:100%; height:8px; border-radius:999px; background:#eef1f6; overflow:hidden; }");
        sb.AppendLine("    .summary-row-fill { height:100%; border-radius:999px; background:linear-gradient(90deg,#9da9ff 0%, #4740d1 100%); min-width:4px; }");
        sb.AppendLine("    .legend { display:flex; flex-wrap:wrap; gap:10px; }");
        sb.AppendLine("    .legend-item { display:inline-flex; align-items:center; gap:8px; padding:8px 10px; border-radius:999px; background:#f5f5f5; font-size:12px; }");
        sb.AppendLine("    .legend-swatch { width:10px; height:10px; border-radius:999px; display:inline-block; }");
        sb.AppendLine("    @media (max-width: 1080px) { .toolbar, .hero, .panel-toolbar { flex-direction:column; align-items:flex-start; } .summary-columns { grid-template-columns:1fr; } }");
        sb.AppendLine("    @media (max-width: 680px) { .page { padding:22px 18px 32px; } .hero h1 { font-size:28px; } }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <main class=\"page\">");
        sb.AppendLine("    <div class=\"toolbar\">");
        sb.AppendLine("      <a class=\"back-link\" href=\"index.html\">Back to report</a>");
        sb.AppendLine("      <div class=\"theme-switcher\" role=\"tablist\" aria-label=\"Theme selector\">");
        sb.AppendLine("        <button type=\"button\" class=\"theme-switch\" data-theme-target=\"light\" role=\"tab\" aria-selected=\"false\" title=\"Light theme\" aria-label=\"Light theme\"><span class=\"theme-icon\">☀</span></button>");
        sb.AppendLine("        <button type=\"button\" class=\"theme-switch active\" data-theme-target=\"system\" role=\"tab\" aria-selected=\"true\" title=\"System theme\" aria-label=\"System theme\"><span class=\"theme-icon\">◐</span></button>");
        sb.AppendLine("        <button type=\"button\" class=\"theme-switch\" data-theme-target=\"dark\" role=\"tab\" aria-selected=\"false\" title=\"Dark theme\" aria-label=\"Dark theme\"><span class=\"theme-icon\">☾</span></button>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </div>");
        sb.AppendLine("    <section class=\"hero\">");
        sb.AppendLine("      <div>");
        sb.Append("        <h1>").Append(Html(safeLabel)).AppendLine("</h1>");
        sb.Append("        <p>").Append(Html(summaryHint)).AppendLine("</p>");
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div class=\"asset-links\">");
        sb.Append("        <a class=\"asset-link\" data-light-href=\"").Append(Html(safeKey)).Append(".light.svg\" data-dark-href=\"").Append(Html(safeKey)).Append(".dark.svg\" href=\"").Append(Html(safeKey)).Append(".light.svg\" target=\"_blank\" rel=\"noopener\">Open SVG</a>").AppendLine();
        sb.Append("        <a class=\"asset-link\" href=\"").Append(Html(safeKey)).Append(".json\" target=\"_blank\" rel=\"noopener\">Open JSON</a>").AppendLine();
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
        sb.AppendLine("    <section class=\"panel\">");
        sb.AppendLine("      <div class=\"panel-toolbar\">");
        sb.AppendLine("        <div class=\"mode-switcher\" role=\"tablist\" aria-label=\"Breakdown display mode\">");
        sb.AppendLine("          <button type=\"button\" class=\"mode-button active\" data-mode=\"preview\" role=\"tab\" aria-selected=\"true\">Preview</button>");
        sb.AppendLine("          <button type=\"button\" class=\"mode-button\" data-mode=\"summary\" role=\"tab\" aria-selected=\"false\">Summary</button>");
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div class=\"preview\">");
        sb.Append("        <img src=\"").Append(Html(safeKey)).Append(".light.svg\" data-light-src=\"").Append(Html(safeKey)).Append(".light.svg\" data-dark-src=\"").Append(Html(safeKey)).Append(".dark.svg\" alt=\"").Append(Html(safeLabel)).AppendLine(" heatmap\">");
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div class=\"summary\"></div>");
        sb.AppendLine("    </section>");
        sb.AppendLine("  <script>");
        sb.AppendLine("    const themeKey = 'ix-usage-report-theme';");
        sb.AppendLine("    const themeMedia = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;");
        sb.AppendLine("    const themeSwitches = document.querySelectorAll('.theme-switch');");
        sb.AppendLine("    const modeButtons = document.querySelectorAll('.mode-button');");
        sb.AppendLine("    const preview = document.querySelector('.preview');");
        sb.AppendLine("    const summary = document.querySelector('.summary');");
        sb.AppendLine("    const breakdownKey = " + JsonString(safeKey) + ";");
        sb.AppendLine("    let currentMode = 'preview';");
        sb.AppendLine("    let summaryLoaded = false;");
        sb.AppendLine("    function formatCompact(value) {");
        sb.AppendLine("      const numeric = Number(value || 0);");
        sb.AppendLine("      if (!Number.isFinite(numeric)) return '0';");
        sb.AppendLine("      if (numeric >= 1_000_000_000) return `${(numeric / 1_000_000_000).toFixed(numeric >= 10_000_000_000 ? 1 : 2).replace(/\\.0+$/, '').replace(/(\\.\\d*[1-9])0+$/, '$1')}B`;");
        sb.AppendLine("      if (numeric >= 1_000_000) return `${(numeric / 1_000_000).toFixed(numeric >= 10_000_000 ? 1 : 2).replace(/\\.0+$/, '').replace(/(\\.\\d*[1-9])0+$/, '$1')}M`;");
        sb.AppendLine("      if (numeric >= 1_000) return `${(numeric / 1_000).toFixed(numeric >= 10_000 ? 1 : 2).replace(/\\.0+$/, '').replace(/(\\.\\d*[1-9])0+$/, '$1')}K`;");
        sb.AppendLine("      return `${Math.round(numeric)}`;");
        sb.AppendLine("    }");
        sb.AppendLine("    function resolveTheme(target) {");
        sb.AppendLine("      if (target === 'light' || target === 'dark') return target;");
        sb.AppendLine("      return themeMedia && themeMedia.matches ? 'dark' : 'light';");
        sb.AppendLine("    }");
        sb.AppendLine("    function applyTheme(target, persist) {");
        sb.AppendLine("      const resolved = resolveTheme(target);");
        sb.AppendLine("      document.documentElement.setAttribute('data-theme', resolved);");
        sb.AppendLine("      themeSwitches.forEach(button => {");
        sb.AppendLine("        const buttonTarget = button.getAttribute('data-theme-target') || 'system';");
        sb.AppendLine("        const active = buttonTarget === target;");
        sb.AppendLine("        button.classList.toggle('active', active);");
        sb.AppendLine("        button.setAttribute('aria-selected', active ? 'true' : 'false');");
        sb.AppendLine("      });");
        sb.AppendLine("      document.querySelectorAll('img[data-light-src][data-dark-src]').forEach(img => {");
        sb.AppendLine("        const next = resolved === 'dark' ? img.getAttribute('data-dark-src') : img.getAttribute('data-light-src');");
        sb.AppendLine("        if (next) img.setAttribute('src', next);");
        sb.AppendLine("      });");
        sb.AppendLine("      document.querySelectorAll('a[data-light-href][data-dark-href]').forEach(link => {");
        sb.AppendLine("        const next = resolved === 'dark' ? link.getAttribute('data-dark-href') : link.getAttribute('data-light-href');");
        sb.AppendLine("        if (next) link.setAttribute('href', next);");
        sb.AppendLine("      });");
        sb.AppendLine("      if (persist) { try { localStorage.setItem(themeKey, target); } catch (_) { } }");
        sb.AppendLine("    }");
        sb.AppendLine("    function renderRows(rows, totalValue, formatter, subline) {");
        sb.AppendLine("      return rows.length ? `<div class=\"summary-list\">${rows.map(([label, value]) => { const numeric = Number(value || 0); const share = totalValue > 0 ? (numeric / totalValue) * 100 : 0; const safeWidth = Math.max(share, numeric > 0 ? 2 : 0); const meta = subline ? `<div class=\"summary-row-meta\">${subline(label, numeric, share)}</div>` : ''; return `<div class=\"summary-row\"><div class=\"summary-row-head\"><div class=\"summary-row-label\">${label}</div><div class=\"summary-row-value\">${formatter(label, numeric, share)}</div></div>${meta}<div class=\"summary-row-bar\"><div class=\"summary-row-fill\" style=\"width:${safeWidth.toFixed(2)}%\"></div></div></div>`; }).join('')}</div>` : '<div class=\"empty\">No active breakdown totals available.</div>';"); 
        sb.AppendLine("    }");
        sb.AppendLine("    function ensureSummary() {");
        sb.AppendLine("      if (summaryLoaded) return;");
        sb.AppendLine("      fetch(`${breakdownKey}.json`).then(resp => resp.json()).then(data => {");
        sb.AppendLine("        summaryLoaded = true;");
        sb.AppendLine("        const sections = Array.isArray(data.sections) ? data.sections : [];");
        sb.AppendLine("        const days = sections.flatMap(section => Array.isArray(section.days) ? section.days : []);");
        sb.AppendLine("        const activeDays = days.filter(day => Number(day.value || 0) > 0);");
        sb.AppendLine("        const totals = new Map();");
        sb.AppendLine("        activeDays.forEach(day => { const breakdown = day.breakdown || {}; Object.entries(breakdown).forEach(([label, value]) => { const numeric = Number(value || 0); totals.set(label, (totals.get(label) || 0) + numeric); }); });");
        sb.AppendLine("        const top = [...totals.entries()].sort((a, b) => b[1] - a[1]).slice(0, 10);");
        sb.AppendLine("        const legend = Array.isArray(data.legend_items) ? data.legend_items : [];");
        sb.AppendLine("        const labelMap = new Map(legend.map(item => [item.label, item.label]));");
        sb.AppendLine("        legend.forEach(item => { if (item && item.key && item.label) labelMap.set(item.key, item.label); });");
        sb.AppendLine("        const resolveLabel = value => labelMap.get(value) || value;");
        sb.AppendLine("        const totalValue = activeDays.reduce((sum, day) => sum + Number(day.value || 0), 0);");
        sb.AppendLine("        const firstDate = days.length ? days[0].date : 'n/a';");
        sb.AppendLine("        const lastDate = days.length ? days[days.length - 1].date : 'n/a';");
        sb.AppendLine("        const peak = activeDays.reduce((best, day) => Number(day.value || 0) > best.value ? { date: day.date, value: Number(day.value || 0) } : best, { date: 'n/a', value: 0 });");
        sb.AppendLine("        const isSourceRoot = breakdownKey === 'sourceroot';");
        sb.AppendLine("        const sourceFamilyRows = isSourceRoot ? [...totals.entries()].reduce((map, [label, value]) => { const text = String(label || 'Unknown'); let bucket = 'Imported / other'; if (/windows\\.old/i.test(text)) bucket = 'Windows.old'; else if (/current/i.test(text)) bucket = 'Current machine'; else if (/wsl/i.test(text)) bucket = 'WSL'; else if (/mac/i.test(text)) bucket = 'macOS'; map.set(bucket, (map.get(bucket) || 0) + Number(value || 0)); return map; }, new Map()) : new Map();");
        sb.AppendLine("        const topLabeled = top.map(([label, value]) => [resolveLabel(label), value]);");
        sb.AppendLine("        const topHtml = renderRows(topLabeled, totalValue, (_, numeric, share) => `${formatCompact(numeric)} (${share.toFixed(1)}%)`, (_, __, share) => `${share.toFixed(1)}% of visible total`);");
        sb.AppendLine("        const sectionRows = sections.map(section => { const sectionDays = Array.isArray(section.days) ? section.days : []; const sectionActive = sectionDays.filter(day => Number(day.value || 0) > 0); const sectionTotal = sectionActive.reduce((sum, day) => sum + Number(day.value || 0), 0); return [section.title || 'Untitled section', sectionTotal, sectionActive.length]; }).filter(([, value]) => Number(value || 0) > 0).sort((a, b) => Number(b[1]) - Number(a[1]));");
        sb.AppendLine("        const sectionHtml = sectionRows.length ? `<div class=\"summary-list\">${sectionRows.map(([label, value, active]) => { const numeric = Number(value || 0); const share = totalValue > 0 ? (numeric / totalValue) * 100 : 0; const safeWidth = Math.max(share, numeric > 0 ? 2 : 0); return `<div class=\"summary-row\"><div class=\"summary-row-head\"><div class=\"summary-row-label\">${label}</div><div class=\"summary-row-value\">${formatCompact(numeric)} (${share.toFixed(1)}%)</div></div><div class=\"summary-row-meta\">${active} active day(s)</div><div class=\"summary-row-bar\"><div class=\"summary-row-fill\" style=\"width:${safeWidth.toFixed(2)}%\"></div></div></div>`; }).join('')}</div>` : '<div class=\"empty\">No active sections available.</div>';"); 
        sb.AppendLine("        const sourceFamilyHtml = sourceFamilyRows.size ? renderRows([...sourceFamilyRows.entries()].sort((a, b) => b[1] - a[1]), totalValue, (_, numeric, share) => `${formatCompact(numeric)} (${share.toFixed(1)}%)`, (_, __, share) => `${share.toFixed(1)}% of visible total`) : '<div class=\"empty\">No source-family totals available.</div>';"); 
        sb.AppendLine("        const legendHtml = legend.length ? `<div class=\"legend\">${legend.map(item => `<span class=\"legend-item\"><span class=\"legend-swatch\" style=\"background:${item.color}\"></span>${item.label}</span>`).join('')}</div>` : '<div class=\"empty\">No legend categories defined for this breakdown.</div>';"); 
        sb.AppendLine("        const overviewBody = isSourceRoot ? `<div class=\"note\">${" + JsonString(summaryHint) + "}</div><div class=\"note\">${totals.size} distinct source root(s), with labels derived from current roots, Windows.old, and future imported sources like WSL or macOS backups.</div>` : `<div class=\"note\">${" + JsonString(summaryHint) + "}</div>`;");
        sb.AppendLine("        summary.innerHTML = `<div class=\"summary-stats\"><div class=\"summary-stat\"><div class=\"summary-stat-label\">Range</div><div class=\"summary-stat-value\">${firstDate} to ${lastDate}</div></div><div class=\"summary-stat\"><div class=\"summary-stat-label\">Active days</div><div class=\"summary-stat-value\">${activeDays.length}</div></div><div class=\"summary-stat\"><div class=\"summary-stat-label\">Total</div><div class=\"summary-stat-value\">${formatCompact(totalValue)}</div></div><div class=\"summary-stat\"><div class=\"summary-stat-label\">Peak day</div><div class=\"summary-stat-value\">${peak.date} (${formatCompact(peak.value)})</div></div><div class=\"summary-stat\"><div class=\"summary-stat-label\">Categories</div><div class=\"summary-stat-value\">${totals.size}</div></div></div><div class=\"summary-columns\"><article class=\"summary-card\"><h4>${isSourceRoot ? 'Source coverage' : 'Overview'}</h4>${overviewBody}</article><article class=\"summary-card\"><h4>${isSourceRoot ? 'Top source roots' : 'Top categories'}</h4>${topHtml}</article><article class=\"summary-card\"><h4>${isSourceRoot ? 'Source families' : 'Section activity'}</h4>${isSourceRoot ? sourceFamilyHtml : sectionHtml}</article><article class=\"summary-card\"><h4>Legend</h4>${legendHtml}</article></div>`;");
        sb.AppendLine("      }).catch(() => { summary.innerHTML = '<div class=\"empty\">Failed to load breakdown summary.</div>'; });");
        sb.AppendLine("    }");
        sb.AppendLine("    function applyMode(mode) {");
        sb.AppendLine("      currentMode = mode;");
        sb.AppendLine("      modeButtons.forEach(button => { const active = button.getAttribute('data-mode') === mode; button.classList.toggle('active', active); button.setAttribute('aria-selected', active ? 'true' : 'false'); });");
        sb.AppendLine("      if (preview) preview.classList.toggle('hidden', mode !== 'preview');");
        sb.AppendLine("      if (summary) summary.classList.toggle('active', mode === 'summary');");
        sb.AppendLine("      if (mode === 'summary') ensureSummary();");
        sb.AppendLine("    }");
        sb.AppendLine("    themeSwitches.forEach(button => button.addEventListener('click', () => applyTheme(button.getAttribute('data-theme-target') || 'system', true)));");
        sb.AppendLine("    modeButtons.forEach(button => button.addEventListener('click', () => applyMode(button.getAttribute('data-mode') || 'preview')));");
        sb.AppendLine("    if (themeMedia) { const listener = () => { const current = (() => { try { return localStorage.getItem(themeKey) || 'system'; } catch (_) { return 'system'; } })(); if (current === 'system') applyTheme('system', false); }; if (themeMedia.addEventListener) themeMedia.addEventListener('change', listener); else if (themeMedia.addListener) themeMedia.addListener(listener); }");
        sb.AppendLine("    const savedTheme = (() => { try { return localStorage.getItem(themeKey) || 'system'; } catch (_) { return 'system'; } })();");
        sb.AppendLine("    applyTheme(savedTheme, false);");
        sb.AppendLine("  </script>");
        sb.AppendLine("  </main>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string JsonString(string? value) {
        var text = value ?? string.Empty;
        var sb = new StringBuilder(text.Length + 2);
        sb.Append('"');
        foreach (var c in text) {
            switch (c) {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                default:
                    if (char.IsControl(c)) {
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("x4"));
                    } else {
                        sb.Append(c);
                    }
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }
}
