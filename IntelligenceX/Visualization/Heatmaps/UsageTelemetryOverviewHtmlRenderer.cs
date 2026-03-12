using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Visualization.Heatmaps;

#pragma warning disable CS1591

/// <summary>
/// Renders a bundled HTML report for telemetry usage overviews.
/// </summary>
public static class UsageTelemetryOverviewHtmlRenderer {
    public static string Render(UsageTelemetryOverviewDocument overview) {
        if (overview is null) {
            throw new ArgumentNullException(nameof(overview));
        }

        var sb = new StringBuilder(24 * 1024);
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("  <title>").Append(Html(overview.Title)).AppendLine("</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    :root { --bg:#f2f2f2; --panel:#ffffff; --panel-soft:#fafafa; --panel-subtle:#f8f8f8; --panel-muted:#f6f6f6; --panel-strong:#ffffff; --ink:#152038; --muted:#787878; --line:#e5e5e5; --soft:#ececec; --bar-bg:#e7e7e7; --row-bar-bg:#eef1f6; --pill-bg:#f5f5f5; --button-bg:#fafafa; --button-hover:#f1f1f1; --button-active:#1d2b48; --button-active-text:#ffffff; --panel-shadow:0 12px 34px rgba(18,24,38,.04); }");
        sb.AppendLine("    html[data-theme='dark'] { --bg:#0f1115; --panel:#171b22; --panel-soft:#1b212b; --panel-subtle:#1d2430; --panel-muted:#141921; --panel-strong:#202836; --ink:#f5f7fa; --muted:#9aa4b2; --line:#2b3340; --soft:#252b34; --bar-bg:#313947; --row-bar-bg:#2a3140; --pill-bg:#222a37; --button-bg:#1b212b; --button-hover:#242c3a; --button-active:#e9edf7; --button-active-text:#111827; --panel-shadow:0 18px 48px rgba(0,0,0,.35); }");
        sb.AppendLine("    * { box-sizing:border-box; }");
        sb.AppendLine("    body { margin:0; background:var(--bg); color:var(--ink); font-family:\"Aptos\",\"IBM Plex Sans\",\"Segoe UI\",sans-serif; }");
        sb.AppendLine("    .page { max-width:1460px; margin:0 auto; padding:36px 38px 48px; }");
        sb.AppendLine("    .hero { display:flex; justify-content:space-between; gap:24px; align-items:flex-end; margin-bottom:28px; }");
        sb.AppendLine("    .hero h1 { margin:0; font-size:34px; line-height:1; letter-spacing:-.03em; }");
        sb.AppendLine("    .hero p { margin:10px 0 0; color:var(--muted); max-width:70ch; font-size:14px; }");
        sb.AppendLine("    .hero-meta { display:grid; grid-template-columns:repeat(3,minmax(120px,1fr)); gap:18px; min-width:420px; }");
        sb.AppendLine("    .hero-stat { text-align:right; }");
        sb.AppendLine("    .hero-label, .mini-label, .metric-label, .legend-copy { color:var(--muted); font-size:12px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; }");
        sb.AppendLine("    .hero-value { margin-top:4px; font-size:20px; font-weight:800; }");
        sb.AppendLine("    .hero-switcher { margin:0; display:flex; flex-wrap:wrap; gap:10px; }");
        sb.AppendLine("    .page-toolbar { margin:0 0 24px; display:flex; flex-wrap:wrap; justify-content:space-between; gap:16px; align-items:center; }");
        sb.AppendLine("    .hero-switch { display:inline-flex; align-items:center; justify-content:center; padding:10px 14px; border:1px solid var(--line); border-radius:999px; background:var(--button-bg); color:var(--ink); font-size:13px; font-weight:700; letter-spacing:.01em; cursor:pointer; transition:background-color .18s ease,border-color .18s ease,color .18s ease,transform .18s ease,box-shadow .18s ease; }");
        sb.AppendLine("    .hero-switch.active { background:var(--button-active); color:var(--button-active-text); border-color:var(--button-active); }");
        sb.AppendLine("    .hero-switch:hover { transform:translateY(-1px); box-shadow:0 8px 18px rgba(17,24,39,.08); }");
        sb.AppendLine("    .theme-switcher { display:flex; flex-wrap:wrap; gap:10px; }");
        sb.AppendLine("    .theme-switch { width:42px; height:42px; display:inline-flex; align-items:center; justify-content:center; padding:0; border:1px solid var(--line); border-radius:999px; background:var(--button-bg); color:var(--ink); cursor:pointer; transition:background-color .18s ease,border-color .18s ease,color .18s ease,transform .18s ease,box-shadow .18s ease; }");
        sb.AppendLine("    .theme-switch.active { background:var(--button-active); color:var(--button-active-text); border-color:var(--button-active); }");
        sb.AppendLine("    .theme-switch:hover { transform:translateY(-1px); box-shadow:0 8px 18px rgba(17,24,39,.08); }");
        sb.AppendLine("    .theme-icon { font-size:18px; line-height:1; }");
        sb.AppendLine("    .provider-section { padding:6px 0 42px; border-top:1px solid transparent; }");
        sb.AppendLine("    .provider-section.hidden { display:none; }");
        sb.AppendLine("    .provider-shell { background:linear-gradient(180deg,var(--panel-strong) 0%, var(--panel-soft) 100%); border:1px solid var(--line); border-radius:26px; padding:22px 22px 24px; box-shadow:var(--panel-shadow); }");
        sb.AppendLine("    .provider-header { display:flex; justify-content:space-between; gap:24px; align-items:flex-start; margin-bottom:18px; }");
        sb.AppendLine("    .provider-title { margin:0; font-size:40px; line-height:1; letter-spacing:-.04em; }");
        sb.AppendLine("    .provider-subtitle { margin-top:8px; color:var(--muted); font-size:14px; }");
        sb.AppendLine("    .provider-metrics { display:grid; grid-template-columns:repeat(3,minmax(120px,1fr)); gap:22px; min-width:420px; }");
        sb.AppendLine("    .provider-metric { text-align:right; background:var(--panel-subtle); border:1px solid var(--line); border-radius:18px; padding:14px 14px 12px; }");
        sb.AppendLine("    .provider-metric .metric-value { margin-top:4px; font-size:24px; line-height:1; font-weight:800; letter-spacing:-.03em; }");
        sb.AppendLine("    .provider-metric .metric-copy { margin-top:8px; color:var(--muted); font-size:12px; }");
        sb.AppendLine("    .metric-bar { margin-top:12px; height:10px; width:100%; background:var(--bar-bg); border-radius:999px; overflow:hidden; }");
        sb.AppendLine("    .metric-fill { height:100%; border-radius:999px; min-width:10px; }");
        sb.AppendLine("    .provider-token-mix { margin:0; padding:16px 18px; border-radius:20px; background:var(--panel-subtle); border:1px solid var(--line); }");
        sb.AppendLine("    .provider-token-mix-header { display:flex; justify-content:space-between; gap:16px; align-items:baseline; margin-bottom:12px; }");
        sb.AppendLine("    .provider-token-mix-title { font-size:12px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; color:var(--muted); }");
        sb.AppendLine("    .provider-token-mix-copy { font-size:13px; color:var(--muted); }");
        sb.AppendLine("    .provider-token-mix-bar { display:flex; width:100%; height:14px; border-radius:999px; overflow:hidden; background:var(--soft); }");
        sb.AppendLine("    .provider-token-segment { min-width:2px; }");
        sb.AppendLine("    .provider-token-mix-legend { display:flex; flex-wrap:wrap; gap:14px; margin-top:12px; }");
        sb.AppendLine("    .provider-token-mix-item { display:flex; align-items:center; gap:8px; color:var(--muted); font-size:12px; }");
        sb.AppendLine("    .provider-token-mix-item strong { color:var(--ink); font-size:13px; }");
        sb.AppendLine("    .provider-token-dot { width:10px; height:10px; border-radius:999px; display:inline-block; }");
        sb.AppendLine("    .provider-monthly { margin:0; padding:16px 18px 14px; border-radius:20px; background:var(--panel-subtle); border:1px solid var(--line); }");
        sb.AppendLine("    .provider-monthly-header { display:flex; justify-content:space-between; gap:16px; align-items:baseline; margin-bottom:14px; }");
        sb.AppendLine("    .provider-monthly-title { font-size:12px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; color:var(--muted); }");
        sb.AppendLine("    .provider-monthly-copy { font-size:13px; color:var(--muted); }");
        sb.AppendLine("    .provider-monthly-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(42px,1fr)); gap:10px; align-items:end; min-height:112px; }");
        sb.AppendLine("    .provider-month { display:flex; flex-direction:column; gap:8px; align-items:center; }");
        sb.AppendLine("    .provider-month-bar-wrap { width:100%; height:66px; display:flex; align-items:flex-end; }");
        sb.AppendLine("    .provider-month-bar { width:100%; min-height:4px; border-radius:10px 10px 4px 4px; background:#d7dcff; box-shadow:inset 0 -1px 0 rgba(0,0,0,.08); }");
        sb.AppendLine("    .provider-month-label { font-size:12px; color:var(--muted); }");
        sb.AppendLine("    .provider-month-value { font-size:11px; color:var(--muted); }");
        sb.AppendLine("    .provider-heatmap { margin:0; background:var(--panel-muted); border:1px solid var(--line); border-radius:24px; padding:14px; }");
        sb.AppendLine("    .provider-heatmap img { width:100%; height:auto; display:block; }");
        sb.AppendLine("    .provider-note { margin:14px 0 0; color:var(--muted); font-size:14px; }");
        sb.AppendLine("    .provider-legend { display:flex; align-items:center; gap:10px; margin:18px 0 0; }");
        sb.AppendLine("    .legend-swatch { width:20px; height:20px; border-radius:6px; display:inline-block; background:var(--soft); }");
        sb.AppendLine("    .provider-datasets { margin-top:22px; }");
        sb.AppendLine("    .provider-dataset-tabs { display:flex; flex-wrap:wrap; gap:10px; margin:0 0 18px; }");
        sb.AppendLine("    .provider-dataset-tab { display:inline-flex; align-items:center; justify-content:center; padding:10px 14px; border:1px solid var(--line); border-radius:999px; background:var(--button-bg); color:var(--ink); font-size:12px; font-weight:700; letter-spacing:.05em; text-transform:uppercase; cursor:pointer; transition:background-color .18s ease,border-color .18s ease,color .18s ease,transform .18s ease,box-shadow .18s ease; }");
        sb.AppendLine("    .provider-dataset-tab.active { background:var(--button-active); color:var(--button-active-text); border-color:var(--button-active); }");
        sb.AppendLine("    .provider-dataset-tab:hover { transform:translateY(-1px); box-shadow:0 8px 18px rgba(17,24,39,.08); }");
        sb.AppendLine("    .provider-dataset-link { text-decoration:none; margin-left:auto; }");
        sb.AppendLine("    .provider-panel { display:none; }");
        sb.AppendLine("    .provider-panel.active { display:block; }");
        sb.AppendLine("    .provider-summary-grid { display:grid; grid-template-columns:minmax(0,1.08fr) minmax(320px,.92fr); gap:16px; align-items:start; }");
        sb.AppendLine("    .provider-summary-stack { display:grid; gap:12px; align-content:start; }");
        sb.AppendLine("    .provider-feature-grid { display:grid; grid-template-columns:1.05fr .95fr .95fr; gap:14px; align-items:start; }");
        sb.AppendLine("    .provider-feature-card { padding:16px; border:1px solid var(--line); border-radius:20px; background:linear-gradient(180deg,var(--panel-strong) 0%, var(--panel-soft) 100%); }");
        sb.AppendLine("    .provider-feature-kicker { color:var(--muted); font-size:12px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; }");
        sb.AppendLine("    .provider-feature-headline { margin-top:10px; font-size:28px; line-height:1.05; font-weight:800; letter-spacing:-.04em; }");
        sb.AppendLine("    .provider-feature-copy { margin-top:8px; color:var(--muted); font-size:13px; }");
        sb.AppendLine("    .provider-feature-rows { display:grid; gap:12px; margin-top:16px; }");
        sb.AppendLine("    .provider-feature-row { display:grid; gap:6px; }");
        sb.AppendLine("    .provider-feature-row-head { display:flex; justify-content:space-between; gap:12px; align-items:baseline; }");
        sb.AppendLine("    .provider-feature-row-label { font-size:14px; font-weight:700; }");
        sb.AppendLine("    .provider-feature-row-value { color:var(--muted); font-size:13px; white-space:nowrap; }");
        sb.AppendLine("    .provider-feature-row-copy { color:var(--muted); font-size:12px; }");
        sb.AppendLine("    .provider-feature-row-bar { width:100%; height:8px; border-radius:999px; background:var(--row-bar-bg); overflow:hidden; }");
        sb.AppendLine("    .provider-feature-row-fill { height:100%; border-radius:999px; background:linear-gradient(90deg,#8ddf9d 0%, #216e39 100%); min-width:4px; }");
        sb.AppendLine("    .provider-compare-card { padding:20px; border:1px solid var(--line); border-radius:22px; background:linear-gradient(180deg,var(--panel-strong) 0%, var(--panel-soft) 100%); }");
        sb.AppendLine("    .provider-compare-grid { display:grid; grid-template-columns:1fr auto 1fr; gap:14px; align-items:center; margin-top:18px; }");
        sb.AppendLine("    .provider-compare-side { padding:16px; border-radius:18px; background:var(--panel-subtle); border:1px solid var(--line); }");
        sb.AppendLine("    .provider-compare-side.right { background:linear-gradient(180deg,rgba(64,196,99,.10) 0%, rgba(33,110,57,.12) 100%); }");
        sb.AppendLine("    .provider-compare-label { color:var(--muted); font-size:12px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; }");
        sb.AppendLine("    .provider-compare-value { margin-top:8px; font-size:26px; font-weight:800; letter-spacing:-.04em; line-height:1.05; }");
        sb.AppendLine("    .provider-compare-subtitle { margin-top:8px; color:var(--muted); font-size:12px; }");
        sb.AppendLine("    .provider-compare-arrow { display:flex; align-items:center; justify-content:center; width:44px; height:44px; border-radius:999px; border:1px solid var(--line); background:var(--panel-soft); color:var(--muted); font-size:20px; font-weight:800; }");
        sb.AppendLine("    .provider-badge-row { display:flex; flex-wrap:wrap; gap:8px; margin-top:12px; }");
        sb.AppendLine("    .provider-badge { display:inline-flex; align-items:center; justify-content:center; min-height:28px; padding:6px 10px; border-radius:999px; font-size:12px; font-weight:700; letter-spacing:.02em; background:var(--pill-bg); color:var(--ink); border:1px solid var(--line); }");
        sb.AppendLine("    .provider-badge.active { background:rgba(64,196,99,.14); color:#216e39; border-color:rgba(64,196,99,.32); }");
        sb.AppendLine("    .provider-badge.rising { background:rgba(249,115,22,.16); color:#c2410c; border-color:rgba(249,115,22,.28); }");
        sb.AppendLine("    .provider-badge.established { background:rgba(59,130,246,.16); color:#1d4ed8; border-color:rgba(59,130,246,.28); }");
        sb.AppendLine("    .provider-badge.warm { background:rgba(245,158,11,.14); color:#b45309; border-color:rgba(245,158,11,.28); }");
        sb.AppendLine("    .provider-badge.dormant { background:rgba(156,163,175,.18); color:var(--muted); border-color:rgba(156,163,175,.24); }");
        sb.AppendLine("    .provider-badge.scope { background:rgba(79,124,255,.12); color:#3556c8; border-color:rgba(79,124,255,.26); }");
        sb.AppendLine("    .provider-models-stack { display:grid; gap:18px; }");
        sb.AppendLine("    .provider-spotlight { display:grid; grid-template-columns:repeat(auto-fit,minmax(210px,1fr)); gap:14px; }");
        sb.AppendLine("    .provider-footer { display:grid; grid-template-columns:repeat(4,minmax(180px,1fr)); gap:22px; margin-top:28px; }");
        sb.AppendLine("    .mini-card { min-height:58px; padding:12px 14px; border:1px solid var(--line); border-radius:18px; background:var(--panel-soft); display:flex; flex-direction:column; justify-content:flex-start; }");
        sb.AppendLine("    .mini-value { margin-top:6px; font-size:22px; line-height:1.15; font-weight:800; letter-spacing:-.03em; }");
        sb.AppendLine("    .mini-value span { color:var(--muted); font-weight:500; }");
        sb.AppendLine("    .mini-copy { margin-top:6px; color:var(--muted); font-size:13px; }");
        sb.AppendLine("    .provider-insights { display:grid; grid-template-columns:repeat(auto-fit,minmax(280px,1fr)); gap:16px; margin-top:16px; }");
        sb.AppendLine("    .provider-insights.tight { grid-template-columns:repeat(auto-fit,minmax(260px,1fr)); gap:14px; }");
        sb.AppendLine("    .insight-card { min-height:140px; padding:16px 16px 14px; border:1px solid var(--line); border-radius:20px; background:var(--panel-soft); }");
        sb.AppendLine("    .insight-title { color:var(--muted); font-size:12px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; margin-bottom:12px; }");
        sb.AppendLine("    .rank-list { display:grid; gap:10px; }");
        sb.AppendLine("    .rank-row { display:grid; grid-template-columns:32px 1fr auto; gap:12px; align-items:baseline; }");
        sb.AppendLine("    .rank-index { color:var(--muted); font-size:15px; font-weight:700; }");
        sb.AppendLine("    .rank-label { font-size:18px; font-weight:700; letter-spacing:-.02em; }");
        sb.AppendLine("    .rank-value { color:var(--muted); font-size:16px; white-space:nowrap; }");
        sb.AppendLine("    .github-impact-shell { display:grid; gap:16px; }");
        sb.AppendLine("    .github-impact-toolbar { display:flex; flex-wrap:wrap; gap:12px; align-items:center; }");
        sb.AppendLine("    .github-impact-shell .provider-insights.tight { grid-template-columns:repeat(3,minmax(260px,1fr)); gap:16px; align-items:stretch; }");
        sb.AppendLine("    .github-impact-shell .provider-insights.tight > * { min-width:0; }");
        sb.AppendLine("    .github-lens-switcher { display:flex; flex-wrap:wrap; gap:10px; }");
        sb.AppendLine("    .github-lens-tab { display:inline-flex; align-items:center; justify-content:center; padding:9px 13px; border:1px solid var(--line); border-radius:999px; background:var(--button-bg); color:var(--ink); font-size:12px; font-weight:700; letter-spacing:.04em; text-transform:uppercase; cursor:pointer; transition:background-color .18s ease,border-color .18s ease,color .18s ease,transform .18s ease,box-shadow .18s ease; }");
        sb.AppendLine("    .github-lens-tab.active { background:var(--button-active); color:var(--button-active-text); border-color:var(--button-active); }");
        sb.AppendLine("    .github-lens-tab:hover { transform:translateY(-1px); box-shadow:0 8px 18px rgba(17,24,39,.08); }");
        sb.AppendLine("    .github-lens-panel { display:none; }");
        sb.AppendLine("    .github-lens-panel.active { display:block; }");
        sb.AppendLine("    .github-summary-owner-shell { margin-top:14px; padding:14px 16px; border:1px solid var(--line); border-radius:20px; background:var(--panel-subtle); }");
        sb.AppendLine("    .github-owner-switcher { display:flex; flex-wrap:wrap; gap:10px; margin-bottom:14px; }");
        sb.AppendLine("    .github-owner-chip { display:inline-flex; align-items:center; justify-content:center; padding:8px 12px; border:1px solid var(--line); border-radius:999px; background:var(--button-bg); color:var(--ink); font-size:12px; font-weight:700; letter-spacing:.04em; cursor:pointer; transition:background-color .18s ease,border-color .18s ease,color .18s ease,transform .18s ease,box-shadow .18s ease; }");
        sb.AppendLine("    .github-owner-chip.active { background:var(--button-active); color:var(--button-active-text); border-color:var(--button-active); }");
        sb.AppendLine("    .github-owner-chip:hover { transform:translateY(-1px); box-shadow:0 8px 18px rgba(17,24,39,.08); }");
        sb.AppendLine("    .github-owner-panel { display:none; }");
        sb.AppendLine("    .github-owner-panel.active { display:block; }");
        sb.AppendLine("    .github-repo-sorter { display:flex; flex-wrap:wrap; gap:10px; align-items:center; padding:12px 14px; border:1px solid var(--line); border-radius:18px; background:var(--panel-soft); }");
        sb.AppendLine("    .github-repo-sort-kicker { color:var(--muted); font-size:12px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; margin-right:6px; }");
        sb.AppendLine("    .github-repo-sort-tabs { display:flex; flex-wrap:wrap; gap:10px; }");
        sb.AppendLine("    .github-repo-sort-tab { display:inline-flex; align-items:center; justify-content:center; padding:8px 12px; border:1px solid var(--line); border-radius:999px; background:var(--button-bg); color:var(--ink); font-size:12px; font-weight:700; letter-spacing:.04em; text-transform:uppercase; cursor:pointer; transition:background-color .18s ease,border-color .18s ease,color .18s ease,transform .18s ease,box-shadow .18s ease; }");
        sb.AppendLine("    .github-repo-sort-tab.active { background:var(--button-active); color:var(--button-active-text); border-color:var(--button-active); }");
        sb.AppendLine("    .github-repo-sort-tab:hover { transform:translateY(-1px); box-shadow:0 8px 18px rgba(17,24,39,.08); }");
        sb.AppendLine("    .github-repo-sort-panel { display:none; }");
        sb.AppendLine("    .github-repo-sort-panel.active { display:block; }");
        sb.AppendLine("    .github-scope-pills { display:flex; flex-wrap:wrap; gap:8px; margin-top:10px; }");
        sb.AppendLine("    .github-impact-compact { display:grid; grid-template-columns:repeat(auto-fit,minmax(240px,1fr)); gap:14px; }");
        sb.AppendLine("    .estimate-total { display:flex; justify-content:space-between; gap:18px; align-items:flex-end; margin-bottom:12px; }");
        sb.AppendLine("    .estimate-value { font-size:34px; font-weight:800; letter-spacing:-.04em; line-height:1; }");
        sb.AppendLine("    .estimate-copy { color:var(--muted); font-size:13px; max-width:28ch; text-align:right; }");
        sb.AppendLine("    .estimate-note { color:var(--muted); font-size:13px; margin-top:10px; }");
        sb.AppendLine("    .divider { height:1px; background:var(--line); margin:8px 0 30px; }");
        sb.AppendLine("    .supporting { margin-top:24px; padding-top:22px; border-top:1px solid var(--line); }");
        sb.AppendLine("    .supporting.hidden { display:none; }");
        sb.AppendLine("    .supporting h2 { margin:0 0 8px; font-size:18px; letter-spacing:-.02em; }");
        sb.AppendLine("    .supporting p { margin:0 0 18px; color:var(--muted); font-size:14px; }");
        sb.AppendLine("    .supporting-tabs { display:flex; flex-wrap:wrap; gap:10px; margin:0 0 18px; }");
        sb.AppendLine("    .supporting-tab { display:inline-flex; align-items:center; justify-content:center; padding:10px 14px; border:1px solid var(--line); border-radius:999px; background:var(--button-bg); color:var(--ink); font-size:13px; font-weight:700; letter-spacing:.01em; cursor:pointer; transition:background-color .18s ease,border-color .18s ease,color .18s ease,transform .18s ease,box-shadow .18s ease; }");
        sb.AppendLine("    .supporting-tab.active { background:var(--button-active); color:var(--button-active-text); border-color:var(--button-active); }");
        sb.AppendLine("    .supporting-tab:hover { transform:translateY(-1px); box-shadow:0 8px 18px rgba(17,24,39,.08); }");
        sb.AppendLine("    .supporting-viewer { background:var(--panel); border:1px solid var(--line); border-radius:22px; padding:20px; }");
        sb.AppendLine("    .supporting-panel { display:none; }");
        sb.AppendLine("    .supporting-panel.active { display:block; }");
        sb.AppendLine("    .supporting-header { display:flex; justify-content:space-between; gap:18px; align-items:flex-start; margin-bottom:14px; }");
        sb.AppendLine("    .supporting-title { margin:0; font-size:22px; letter-spacing:-.03em; }");
        sb.AppendLine("    .supporting-copy { margin:8px 0 0; color:var(--muted); font-size:14px; max-width:80ch; }");
        sb.AppendLine("    .supporting-toolbar { display:flex; flex-wrap:wrap; justify-content:space-between; gap:14px; align-items:center; margin-bottom:14px; }");
        sb.AppendLine("    .supporting-modes { display:flex; flex-wrap:wrap; gap:10px; }");
        sb.AppendLine("    .supporting-mode { display:inline-flex; align-items:center; justify-content:center; padding:8px 12px; border:1px solid var(--line); border-radius:999px; background:var(--button-bg); color:var(--ink); font-size:12px; font-weight:700; letter-spacing:.04em; text-transform:uppercase; cursor:pointer; transition:background-color .18s ease,border-color .18s ease,color .18s ease,transform .18s ease,box-shadow .18s ease; }");
        sb.AppendLine("    .supporting-mode.active { background:var(--button-active); color:var(--button-active-text); border-color:var(--button-active); }");
        sb.AppendLine("    .supporting-mode:hover { transform:translateY(-1px); box-shadow:0 8px 18px rgba(17,24,39,.08); }");
        sb.AppendLine("    .supporting-links { display:flex; flex-wrap:wrap; gap:10px; margin:0 0 14px; }");
        sb.AppendLine("    .supporting-link { display:inline-flex; align-items:center; justify-content:center; min-width:92px; padding:8px 12px; border:1px solid var(--line); border-radius:999px; background:var(--button-bg); color:var(--ink); text-decoration:none; font-size:12px; font-weight:700; letter-spacing:.04em; text-transform:uppercase; }");
        sb.AppendLine("    .supporting-link:hover { background:var(--button-hover); }");
        sb.AppendLine("    .supporting-preview { padding:14px; border-radius:16px; border:1px solid var(--line); background:var(--panel-muted); overflow:auto; }");
        sb.AppendLine("    .supporting-preview img { width:100%; min-width:900px; height:auto; display:block; border-radius:12px; background:var(--bg); }");
        sb.AppendLine("    .supporting-summary { display:none; padding:18px; border-radius:16px; border:1px solid var(--line); background:var(--panel-soft); }");
        sb.AppendLine("    .supporting-summary.active { display:block; }");
        sb.AppendLine("    .supporting-preview.hidden { display:none; }");
        sb.AppendLine("    .summary-stats { display:grid; grid-template-columns:repeat(auto-fit,minmax(170px,1fr)); gap:14px; margin-bottom:18px; }");
        sb.AppendLine("    .summary-stat { padding:14px 16px; border:1px solid var(--line); border-radius:16px; background:var(--panel-strong); }");
        sb.AppendLine("    .summary-stat-label { color:var(--muted); font-size:11px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; }");
        sb.AppendLine("    .summary-stat-value { margin-top:6px; font-size:22px; font-weight:800; letter-spacing:-.03em; }");
        sb.AppendLine("    .summary-columns { display:grid; grid-template-columns:1.1fr .9fr; gap:18px; }");
        sb.AppendLine("    .summary-card { padding:16px; border:1px solid var(--line); border-radius:16px; background:var(--panel-strong); }");
        sb.AppendLine("    .summary-card h4 { margin:0 0 10px; font-size:14px; letter-spacing:-.02em; }");
        sb.AppendLine("    .summary-list { display:grid; gap:10px; }");
        sb.AppendLine("    .summary-row { display:grid; grid-template-columns:minmax(0,1fr) auto; gap:12px; align-items:center; }");
        sb.AppendLine("    .summary-row-main { min-width:0; }");
        sb.AppendLine("    .summary-row-head { display:flex; justify-content:space-between; gap:12px; align-items:baseline; }");
        sb.AppendLine("    .summary-row-label { font-size:14px; font-weight:700; min-width:0; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }");
        sb.AppendLine("    .summary-row-value { color:var(--muted); font-size:13px; white-space:nowrap; }");
        sb.AppendLine("    .summary-row-meta { margin-top:4px; color:var(--muted); font-size:12px; }");
        sb.AppendLine("    .summary-row-bar { margin-top:8px; width:100%; height:8px; border-radius:999px; background:var(--row-bar-bg); overflow:hidden; }");
        sb.AppendLine("    .summary-row-fill { height:100%; border-radius:999px; background:linear-gradient(90deg,#9da9ff 0%, #4740d1 100%); min-width:4px; }");
        sb.AppendLine("    .summary-empty { color:var(--muted); font-size:13px; }");
        sb.AppendLine("    .summary-legend { display:flex; flex-wrap:wrap; gap:10px; }");
        sb.AppendLine("    .summary-legend-item { display:inline-flex; align-items:center; gap:8px; padding:8px 10px; border-radius:999px; background:var(--pill-bg); font-size:12px; }");
        sb.AppendLine("    .summary-legend-swatch { width:10px; height:10px; border-radius:999px; display:inline-block; }");
        sb.AppendLine("    .footnote { margin-top:24px; color:var(--muted); font-size:13px; }");
        sb.AppendLine("    @media (max-width: 1080px) { .hero, .provider-header, .provider-token-mix-header, .supporting-header, .supporting-toolbar { flex-direction:column; align-items:flex-start; } .hero-meta, .provider-metrics { min-width:0; width:100%; } .hero-stat, .provider-metric { text-align:left; } .provider-footer, .provider-spotlight { grid-template-columns:repeat(2,minmax(180px,1fr)); } .provider-insights, .provider-summary-grid, .provider-feature-grid, .github-impact-shell .provider-insights.tight { grid-template-columns:1fr; } .provider-note, .provider-legend { margin-left:0; } .summary-columns { grid-template-columns:1fr; } .provider-compare-grid { grid-template-columns:1fr; } .provider-compare-arrow { width:36px; height:36px; margin:0 auto; } .github-impact-toolbar { align-items:flex-start; } }");
        sb.AppendLine("    @media (max-width: 680px) { .page { padding:22px 18px 32px; } .hero-meta, .provider-metrics, .provider-footer, .provider-spotlight { grid-template-columns:1fr; gap:14px; } .provider-title { font-size:32px; } }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <main class=\"page\">");
        AppendHero(sb, overview);

        foreach (var providerSection in overview.ProviderSections) {
            AppendProviderSection(sb, providerSection);
        }

        if (overview.Heatmaps.Count > 0) {
            sb.AppendLine("    <section class=\"supporting\">");
            sb.AppendLine("      <h2>Supporting Breakdowns</h2>");
            sb.AppendLine("      <p>These cross-section overlays still ride on the same telemetry ledger, so they help compare telemetry-backed sections like Codex and Claude across surfaces, source roots, accounts, people, and models.</p>");
            sb.AppendLine("      <div class=\"supporting-tabs\" role=\"tablist\" aria-label=\"Supporting breakdowns\">");
            for (var i = 0; i < overview.Heatmaps.Count; i++) {
                var heatmap = overview.Heatmaps[i];
                var isActive = i == 0;
                sb.Append("        <button type=\"button\" class=\"supporting-tab");
                if (isActive) {
                    sb.Append(" active");
                }
                sb.Append("\" data-target=\"").Append(Html(heatmap.Key)).Append("\" role=\"tab\" aria-selected=\"")
                    .Append(isActive ? "true" : "false")
                    .Append("\">")
                    .Append(Html(heatmap.Label))
                    .AppendLine("</button>");
            }
            sb.AppendLine("      </div>");
            sb.AppendLine("      <div class=\"supporting-viewer\">");
            sb.AppendLine("        <div class=\"supporting-toolbar\">");
            sb.AppendLine("          <div class=\"supporting-modes\" role=\"tablist\" aria-label=\"Breakdown display mode\">");
            sb.AppendLine("            <button type=\"button\" class=\"supporting-mode active\" data-mode=\"preview\" role=\"tab\" aria-selected=\"true\">Preview</button>");
            sb.AppendLine("            <button type=\"button\" class=\"supporting-mode\" data-mode=\"summary\" role=\"tab\" aria-selected=\"false\">Summary</button>");
            sb.AppendLine("          </div>");
            sb.AppendLine("        </div>");
            for (var i = 0; i < overview.Heatmaps.Count; i++) {
                var heatmap = overview.Heatmaps[i];
                var isActive = i == 0;
                sb.Append("        <section class=\"supporting-panel");
                if (isActive) {
                    sb.Append(" active");
                }
                sb.Append("\" id=\"panel-").Append(Html(heatmap.Key)).Append("\" data-key=\"").Append(Html(heatmap.Key)).Append("\" role=\"tabpanel\">").AppendLine();
                sb.AppendLine("          <div class=\"supporting-header\">");
                sb.AppendLine("            <div>");
                sb.Append("              <h3 class=\"supporting-title\">").Append(Html(heatmap.Label)).AppendLine("</h3>");
                if (!string.IsNullOrWhiteSpace(heatmap.Document.Subtitle)) {
                    sb.Append("              <p class=\"supporting-copy\">").Append(Html(heatmap.Document.Subtitle!)).AppendLine("</p>");
                }
                sb.AppendLine("            </div>");
                sb.AppendLine("            <div class=\"supporting-links\">");
                sb.Append("              <a class=\"supporting-link\" href=\"").Append(Html(heatmap.Key)).Append(".html\">Open detail</a>").AppendLine();
                sb.Append("              <a class=\"supporting-link\" data-light-href=\"").Append(Html(heatmap.Key)).Append(".light.svg\" data-dark-href=\"").Append(Html(heatmap.Key)).Append(".dark.svg\" href=\"").Append(Html(heatmap.Key)).Append(".light.svg\" target=\"_blank\" rel=\"noopener\">Open SVG</a>").AppendLine();
                sb.Append("              <a class=\"supporting-link\" href=\"").Append(Html(heatmap.Key)).Append(".json\" target=\"_blank\" rel=\"noopener\">Open JSON</a>").AppendLine();
                sb.AppendLine("            </div>");
                sb.AppendLine("          </div>");
                sb.AppendLine("          <div class=\"supporting-preview\">");
                sb.Append("            <img src=\"").Append(Html(heatmap.Key)).Append(".light.svg\" data-light-src=\"").Append(Html(heatmap.Key)).Append(".light.svg\" data-dark-src=\"").Append(Html(heatmap.Key)).Append(".dark.svg\" alt=\"").Append(Html(heatmap.Label)).AppendLine("\">");
                sb.AppendLine("          </div>");
                sb.AppendLine("          <div class=\"supporting-summary\">");
                sb.AppendLine("            <div class=\"summary-empty\">Loading summary…</div>");
                sb.AppendLine("          </div>");
                sb.AppendLine("        </section>");
            }
            sb.AppendLine("      </div>");
            sb.AppendLine("    </section>");
        }

        sb.AppendLine("    <div class=\"footnote\">Built from the provider-neutral telemetry ledger, so the same report format can work for Codex, Claude, IX-native usage, and future compatible providers.</div>");
        sb.AppendLine("  <script>");
        sb.AppendLine("    const ixThemeKey = 'ix-usage-report-theme';");
        sb.AppendLine("    const ixThemeSwitches = document.querySelectorAll('.theme-switch');");
        sb.AppendLine("    const ixThemeMedia = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;");
        sb.AppendLine("    const ixProviderSwitches = document.querySelectorAll('.hero-switch');");
        sb.AppendLine("    const ixProviderSections = document.querySelectorAll('.provider-section');");
        sb.AppendLine("    const ixSupporting = document.querySelector('.supporting');");
        sb.AppendLine("    const ixProviderDatasetTabs = document.querySelectorAll('.provider-dataset-tab');");
        sb.AppendLine("    const ixTabs = document.querySelectorAll('.supporting-tab');");
        sb.AppendLine("    const ixPanels = document.querySelectorAll('.supporting-panel');");
        sb.AppendLine("    const ixModes = document.querySelectorAll('.supporting-mode');");
        sb.AppendLine("    let ixCurrentMode = 'preview';");
        sb.AppendLine("    let ixLoadedSummaries = new Set();");
        sb.AppendLine("    function ixFormatCompact(value) {");
        sb.AppendLine("      const numeric = Number(value || 0);");
        sb.AppendLine("      if (!Number.isFinite(numeric) || numeric <= 0) return '0';");
        sb.AppendLine("      if (numeric >= 1_000_000_000) return `${(numeric / 1_000_000_000).toFixed(numeric >= 10_000_000_000 ? 1 : 2).replace(/\\.0+$/,'').replace(/(\\.\\d*[1-9])0+$/,'$1')}B`;");
        sb.AppendLine("      if (numeric >= 1_000_000) return `${(numeric / 1_000_000).toFixed(numeric >= 10_000_000 ? 1 : 2).replace(/\\.0+$/,'').replace(/(\\.\\d*[1-9])0+$/,'$1')}M`;");
        sb.AppendLine("      if (numeric >= 1_000) return `${(numeric / 1_000).toFixed(numeric >= 10_000 ? 1 : 2).replace(/\\.0+$/,'').replace(/(\\.\\d*[1-9])0+$/,'$1')}K`;");
        sb.AppendLine("      return `${Math.round(numeric)}`;");
        sb.AppendLine("    }");
        sb.AppendLine("    function ixResolveTheme(target) {");
        sb.AppendLine("      if (target === 'light' || target === 'dark') return target;");
        sb.AppendLine("      return ixThemeMedia && ixThemeMedia.matches ? 'dark' : 'light';");
        sb.AppendLine("    }");
        sb.AppendLine("    function ixApplyTheme(target, persist) {");
        sb.AppendLine("      const resolved = ixResolveTheme(target);");
        sb.AppendLine("      document.documentElement.setAttribute('data-theme', resolved);");
        sb.AppendLine("      ixThemeSwitches.forEach(button => {");
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
        sb.AppendLine("      if (persist) {");
        sb.AppendLine("        try { localStorage.setItem(ixThemeKey, target); } catch (_) { }");
        sb.AppendLine("      }");
        sb.AppendLine("    }");
        sb.AppendLine("    const ixSavedTheme = (() => { try { return localStorage.getItem(ixThemeKey) || 'system'; } catch (_) { return 'system'; } })();");
        sb.AppendLine("    ixThemeSwitches.forEach(button => {");
        sb.AppendLine("      button.addEventListener('click', () => ixApplyTheme(button.getAttribute('data-theme-target') || 'system', true));");
        sb.AppendLine("    });");
        sb.AppendLine("    if (ixThemeMedia) {");
        sb.AppendLine("      const ixThemeListener = () => {");
        sb.AppendLine("        const current = (() => { try { return localStorage.getItem(ixThemeKey) || 'system'; } catch (_) { return 'system'; } })();");
        sb.AppendLine("        if (current === 'system') ixApplyTheme('system', false);");
        sb.AppendLine("      };");
        sb.AppendLine("      if (ixThemeMedia.addEventListener) ixThemeMedia.addEventListener('change', ixThemeListener);");
        sb.AppendLine("      else if (ixThemeMedia.addListener) ixThemeMedia.addListener(ixThemeListener);");
        sb.AppendLine("    }");
        sb.AppendLine("    ixApplyTheme(ixSavedTheme, false);");
        sb.AppendLine("    function ixApplySectionTarget(target) {");
        sb.AppendLine("      ixProviderSections.forEach(section => {");
        sb.AppendLine("        const provider = section.getAttribute('data-provider') || '';");
        sb.AppendLine("        section.classList.toggle('hidden', target !== 'all' && provider !== target);");
        sb.AppendLine("      });");
        sb.AppendLine("      if (ixSupporting) ixSupporting.classList.toggle('hidden', target !== 'all');");
        sb.AppendLine("    }");
        sb.AppendLine("    ixProviderSwitches.forEach(button => {");
        sb.AppendLine("      button.addEventListener('click', () => {");
        sb.AppendLine("        const target = button.getAttribute('data-provider-target') || 'all';");
        sb.AppendLine("        ixProviderSwitches.forEach(other => {");
        sb.AppendLine("          const active = other === button;");
        sb.AppendLine("          other.classList.toggle('active', active);");
        sb.AppendLine("          other.setAttribute('aria-selected', active ? 'true' : 'false');");
        sb.AppendLine("        });");
        sb.AppendLine("        ixApplySectionTarget(target);");
        sb.AppendLine("      });");
        sb.AppendLine("    });");
        sb.AppendLine("    ixApplySectionTarget('all');");
        sb.AppendLine("    ixProviderDatasetTabs.forEach(button => {");
        sb.AppendLine("      button.addEventListener('click', () => {");
        sb.AppendLine("        const shell = button.closest('.provider-datasets');");
        sb.AppendLine("        if (!shell) return;");
        sb.AppendLine("        const target = button.getAttribute('data-provider-panel') || 'summary';");
        sb.AppendLine("        shell.querySelectorAll('.provider-dataset-tab').forEach(other => {");
        sb.AppendLine("          const active = other === button;");
        sb.AppendLine("          other.classList.toggle('active', active);");
        sb.AppendLine("          other.setAttribute('aria-selected', active ? 'true' : 'false');");
        sb.AppendLine("        });");
        sb.AppendLine("        shell.querySelectorAll('.provider-panel').forEach(panel => {");
        sb.AppendLine("          panel.classList.toggle('active', panel.getAttribute('data-provider-panel-content') === target);");
        sb.AppendLine("        });");
        sb.AppendLine("      });");
        sb.AppendLine("    });");
        sb.AppendLine("    document.querySelectorAll('.github-lens-tab').forEach(button => {");
        sb.AppendLine("      button.addEventListener('click', () => {");
        sb.AppendLine("        const shell = button.closest('.github-impact-shell');");
        sb.AppendLine("        if (!shell) return;");
        sb.AppendLine("        const target = button.getAttribute('data-github-lens') || 'impact';");
        sb.AppendLine("        shell.querySelectorAll('.github-lens-tab').forEach(other => other.classList.toggle('active', other === button));");
        sb.AppendLine("        shell.querySelectorAll('.github-lens-panel').forEach(panel => {");
        sb.AppendLine("          panel.classList.toggle('active', panel.getAttribute('data-github-lens-content') === target);");
        sb.AppendLine("        });");
        sb.AppendLine("      });");
        sb.AppendLine("    });");
        sb.AppendLine("    document.querySelectorAll('.github-owner-chip').forEach(button => {");
        sb.AppendLine("      button.addEventListener('click', () => {");
        sb.AppendLine("        const shell = button.closest('.github-owner-explorer');");
        sb.AppendLine("        if (!shell) return;");
        sb.AppendLine("        const target = button.getAttribute('data-github-owner') || 'all';");
        sb.AppendLine("        shell.querySelectorAll('.github-owner-chip').forEach(other => other.classList.toggle('active', other === button));");
        sb.AppendLine("        shell.querySelectorAll('.github-owner-panel').forEach(panel => {");
        sb.AppendLine("          panel.classList.toggle('active', panel.getAttribute('data-github-owner-content') === target);");
        sb.AppendLine("        });");
        sb.AppendLine("      });");
        sb.AppendLine("    });");
        sb.AppendLine("    document.querySelectorAll('.github-repo-sort-tab').forEach(button => {");
        sb.AppendLine("      button.addEventListener('click', () => {");
        sb.AppendLine("        const shell = button.closest('.github-impact-shell');");
        sb.AppendLine("        if (!shell) return;");
        sb.AppendLine("        const target = button.getAttribute('data-github-repo-sort') || 'stars';");
        sb.AppendLine("        shell.querySelectorAll('.github-repo-sort-tab').forEach(other => other.classList.toggle('active', other === button));");
        sb.AppendLine("        shell.querySelectorAll('.github-repo-sort-panel').forEach(panel => {");
        sb.AppendLine("          panel.classList.toggle('active', panel.getAttribute('data-github-repo-sort-content') === target);");
        sb.AppendLine("        });");
        sb.AppendLine("      });");
        sb.AppendLine("    });");
        sb.AppendLine("    function ixApplyMode(mode) {");
        sb.AppendLine("      ixCurrentMode = mode;");
        sb.AppendLine("      ixModes.forEach(button => {");
        sb.AppendLine("        const active = button.getAttribute('data-mode') === mode;");
        sb.AppendLine("        button.classList.toggle('active', active);");
        sb.AppendLine("        button.setAttribute('aria-selected', active ? 'true' : 'false');");
        sb.AppendLine("      });");
        sb.AppendLine("      ixPanels.forEach(panel => {");
        sb.AppendLine("        const preview = panel.querySelector('.supporting-preview');");
        sb.AppendLine("        const summary = panel.querySelector('.supporting-summary');");
        sb.AppendLine("        if (preview) preview.classList.toggle('hidden', mode !== 'preview');");
        sb.AppendLine("        if (summary) summary.classList.toggle('active', mode === 'summary');");
        sb.AppendLine("      });");
        sb.AppendLine("      if (mode === 'summary') ixEnsureActiveSummary();");
        sb.AppendLine("    }");
        sb.AppendLine("    function ixEnsureActiveSummary() {");
        sb.AppendLine("      const active = document.querySelector('.supporting-panel.active');");
        sb.AppendLine("      if (!active) return;");
        sb.AppendLine("      const key = active.getAttribute('data-key');");
        sb.AppendLine("      if (!key || ixLoadedSummaries.has(key)) return;");
        sb.AppendLine("      const summary = active.querySelector('.supporting-summary');");
        sb.AppendLine("      if (!summary) return;");
        sb.AppendLine("      fetch(`${key}.json`).then(resp => resp.json()).then(data => {");
        sb.AppendLine("        ixLoadedSummaries.add(key);");
        sb.AppendLine("        const sections = Array.isArray(data.sections) ? data.sections : [];");
        sb.AppendLine("        const days = sections.flatMap(section => Array.isArray(section.days) ? section.days : []);");
        sb.AppendLine("        const activeDays = days.filter(day => Number(day.value || 0) > 0);");
        sb.AppendLine("        const totals = new Map();");
        sb.AppendLine("        activeDays.forEach(day => {");
        sb.AppendLine("          const breakdown = day.breakdown || {};");
        sb.AppendLine("          Object.entries(breakdown).forEach(([label, value]) => {");
        sb.AppendLine("            const numeric = Number(value || 0);");
        sb.AppendLine("            totals.set(label, (totals.get(label) || 0) + numeric);");
        sb.AppendLine("          });");
        sb.AppendLine("        });");
        sb.AppendLine("        const top = [...totals.entries()].sort((a, b) => b[1] - a[1]).slice(0, 6);");
        sb.AppendLine("        const legend = Array.isArray(data.legend_items) ? data.legend_items : [];");
        sb.AppendLine("        const labelMap = new Map(legend.map(item => [item.label, item.label]));");
        sb.AppendLine("        legend.forEach(item => { if (item && item.key && item.label) labelMap.set(item.key, item.label); });");
        sb.AppendLine("        const resolveLabel = (value) => labelMap.get(value) || value;");
        sb.AppendLine("        const firstDate = days.length ? days[0].date : 'n/a';");
        sb.AppendLine("        const lastDate = days.length ? days[days.length - 1].date : 'n/a';");
        sb.AppendLine("        const totalValue = activeDays.reduce((sum, day) => sum + Number(day.value || 0), 0);");
        sb.AppendLine("        const peak = activeDays.reduce((best, day) => Number(day.value || 0) > best.value ? { date: day.date, value: Number(day.value || 0) } : best, { date: 'n/a', value: 0 });");
        sb.AppendLine("        const subtitle = data.subtitle || 'No subtitle available.';");
        sb.AppendLine("        const categoriesCount = totals.size;");
        sb.AppendLine("        const isSourceRoot = key === 'sourceroot';");
        sb.AppendLine("        const sourceFamilyRows = isSourceRoot ? [...totals.entries()].reduce((map, [label, value]) => { const text = String(label || 'Unknown'); let bucket = 'Imported / other'; if (/windows\\.old/i.test(text)) bucket = 'Windows.old'; else if (/current/i.test(text)) bucket = 'Current machine'; else if (/wsl/i.test(text)) bucket = 'WSL'; else if (/mac/i.test(text)) bucket = 'macOS'; map.set(bucket, (map.get(bucket) || 0) + Number(value || 0)); return map; }, new Map()) : new Map();");
        sb.AppendLine("        const renderRows = (rows, formatter, subline) => rows.length");
        sb.AppendLine("          ? `<div class=\"summary-list\">${rows.map(([label, value]) => { const numeric = Number(value || 0); const share = totalValue > 0 ? (numeric / totalValue) * 100 : 0; const safeWidth = Math.max(share, numeric > 0 ? 2 : 0); const meta = subline ? `<div class=\"summary-row-meta\">${subline(label, numeric, share)}</div>` : ''; return `<div class=\"summary-row\"><div class=\"summary-row-main\"><div class=\"summary-row-head\"><div class=\"summary-row-label\">${label}</div><div class=\"summary-row-value\">${formatter(label, numeric, share)}</div></div>${meta}<div class=\"summary-row-bar\"><div class=\"summary-row-fill\" style=\"width:${safeWidth.toFixed(2)}%\"></div></div></div></div>`; }).join('')}</div>`");
        sb.AppendLine("          : '<div class=\"summary-empty\">No active breakdown totals available.</div>';");
        sb.AppendLine("        const legendHtml = legend.length");
        sb.AppendLine("          ? `<div class=\"summary-legend\">${legend.map(item => `<span class=\"summary-legend-item\"><span class=\"summary-legend-swatch\" style=\"background:${item.color}\"></span>${item.label}</span>`).join('')}</div>`");
        sb.AppendLine("          : '<div class=\"summary-empty\">No legend categories defined for this breakdown.</div>';");
        sb.AppendLine("        const topLabeled = top.map(([label, value]) => [resolveLabel(label), value]);");
        sb.AppendLine("        const topHtml = renderRows(topLabeled, (_, numeric, share) => `${ixFormatCompact(numeric)} (${share.toFixed(1)}%)`, (_, __, share) => `${share.toFixed(1)}% of visible total`);");
        sb.AppendLine("        const sectionRows = sections.map(section => {");
        sb.AppendLine("          const sectionDays = Array.isArray(section.days) ? section.days : [];");
        sb.AppendLine("          const sectionActive = sectionDays.filter(day => Number(day.value || 0) > 0);");
        sb.AppendLine("          const sectionTotal = sectionActive.reduce((sum, day) => sum + Number(day.value || 0), 0);");
        sb.AppendLine("          return [section.title || 'Untitled section', sectionTotal, sectionActive.length];");
        sb.AppendLine("        }).filter(([, value]) => Number(value || 0) > 0).sort((a, b) => Number(b[1]) - Number(a[1]));");
        sb.AppendLine("        const sectionHtml = sectionRows.length");
        sb.AppendLine("          ? `<div class=\"summary-list\">${sectionRows.map(([label, value, active]) => { const numeric = Number(value || 0); const share = totalValue > 0 ? (numeric / totalValue) * 100 : 0; const safeWidth = Math.max(share, numeric > 0 ? 2 : 0); return `<div class=\"summary-row\"><div class=\"summary-row-main\"><div class=\"summary-row-head\"><div class=\"summary-row-label\">${label}</div><div class=\"summary-row-value\">${ixFormatCompact(numeric)} (${share.toFixed(1)}%)</div></div><div class=\"summary-row-meta\">${active} active day(s)</div><div class=\"summary-row-bar\"><div class=\"summary-row-fill\" style=\"width:${safeWidth.toFixed(2)}%\"></div></div></div></div>`; }).join('')}</div>`");
        sb.AppendLine("          : '<div class=\"summary-empty\">No active sections available.</div>';");
        sb.AppendLine("        const sourceFamilyHtml = sourceFamilyRows.size ? renderRows([...sourceFamilyRows.entries()].sort((a, b) => b[1] - a[1]), (_, numeric, share) => `${ixFormatCompact(numeric)} (${share.toFixed(1)}%)`, (_, __, share) => `${share.toFixed(1)}% of visible total`) : '<div class=\"summary-empty\">No source-family totals available.</div>';");
        sb.AppendLine("        const overviewHeading = isSourceRoot ? 'Source coverage' : 'Overview';");
        sb.AppendLine("        const topHeading = isSourceRoot ? 'Top source roots' : 'Top categories';");
        sb.AppendLine("        const sectionHeading = isSourceRoot ? 'Source families' : 'Section activity';");
        sb.AppendLine("        const sectionBody = isSourceRoot ? sourceFamilyHtml : sectionHtml;");
        sb.AppendLine("        const overviewBody = isSourceRoot ? `<div class=\"summary-empty\">${subtitle}</div><div class=\"estimate-note\">${categoriesCount} distinct source root(s), with labels derived from current roots, Windows.old, and future imported sources like WSL or macOS backups.</div>` : `<div class=\"summary-empty\">${subtitle}</div>`;");
        sb.AppendLine("        summary.innerHTML = `");
        sb.AppendLine("          <div class=\"summary-stats\">");
        sb.AppendLine("            <div class=\"summary-stat\"><div class=\"summary-stat-label\">Range</div><div class=\"summary-stat-value\">${firstDate} to ${lastDate}</div></div>");
        sb.AppendLine("            <div class=\"summary-stat\"><div class=\"summary-stat-label\">Active days</div><div class=\"summary-stat-value\">${activeDays.length}</div></div>");
        sb.AppendLine("            <div class=\"summary-stat\"><div class=\"summary-stat-label\">Total</div><div class=\"summary-stat-value\">${ixFormatCompact(totalValue)}</div></div>");
        sb.AppendLine("            <div class=\"summary-stat\"><div class=\"summary-stat-label\">Peak day</div><div class=\"summary-stat-value\">${peak.date} (${ixFormatCompact(peak.value)})</div></div>");
        sb.AppendLine("            <div class=\"summary-stat\"><div class=\"summary-stat-label\">Categories</div><div class=\"summary-stat-value\">${categoriesCount}</div></div>");
        sb.AppendLine("          </div>");
        sb.AppendLine("          <div class=\"summary-columns\">");
        sb.AppendLine("            <article class=\"summary-card\"><h4>${overviewHeading}</h4>${overviewBody}</article>");
        sb.AppendLine("            <article class=\"summary-card\"><h4>${topHeading}</h4>${topHtml}</article>");
        sb.AppendLine("            <article class=\"summary-card\"><h4>${sectionHeading}</h4>${sectionBody}</article>");
        sb.AppendLine("            <article class=\"summary-card\"><h4>Legend</h4>${legendHtml}</article>");
        sb.AppendLine("          </div>`;");
        sb.AppendLine("      }).catch(() => {");
        sb.AppendLine("        summary.innerHTML = '<div class=\"summary-empty\">Failed to load breakdown summary.</div>';"); 
        sb.AppendLine("      });");
        sb.AppendLine("    }");
        sb.AppendLine("    ixTabs.forEach(tab => {");
        sb.AppendLine("      tab.addEventListener('click', () => {");
        sb.AppendLine("        const target = tab.getAttribute('data-target');");
        sb.AppendLine("        ixTabs.forEach(other => {");
        sb.AppendLine("          const active = other === tab;");
        sb.AppendLine("          other.classList.toggle('active', active);");
        sb.AppendLine("          other.setAttribute('aria-selected', active ? 'true' : 'false');");
        sb.AppendLine("        });");
        sb.AppendLine("        ixPanels.forEach(panel => panel.classList.toggle('active', panel.id === `panel-${target}`));");
        sb.AppendLine("        if (ixCurrentMode === 'summary') ixEnsureActiveSummary();");
        sb.AppendLine("      });");
        sb.AppendLine("    });");
        sb.AppendLine("    ixModes.forEach(button => {");
        sb.AppendLine("      button.addEventListener('click', () => ixApplyMode(button.getAttribute('data-mode') || 'preview'));");
        sb.AppendLine("    });");
        sb.AppendLine("  </script>");
        sb.AppendLine("  </main>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static void AppendHero(StringBuilder sb, UsageTelemetryOverviewDocument overview) {
        sb.AppendLine("    <section class=\"hero\">");
        sb.AppendLine("      <div>");
        sb.Append("        <h1>").Append(Html(overview.Title)).AppendLine("</h1>");
        if (!string.IsNullOrWhiteSpace(overview.Subtitle)) {
            sb.Append("        <p>").Append(Html(overview.Subtitle!)).AppendLine("</p>");
        }
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div class=\"hero-meta\">");
        AppendHeroStat(sb, "Range", FormatRange(overview.Summary.StartDayUtc, overview.Summary.EndDayUtc));
        AppendHeroStat(sb, "Sections", overview.ProviderSections.Count.ToString(CultureInfo.InvariantCulture));
        AppendHeroStat(sb, "Telemetry Tokens", FormatCompact(overview.Summary.TotalValue));
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
        sb.AppendLine("    <div class=\"page-toolbar\">");
        if (overview.ProviderSections.Count > 1) {
            sb.AppendLine("      <div class=\"hero-switcher\" role=\"tablist\" aria-label=\"Report sections\">");
            sb.AppendLine("        <button type=\"button\" class=\"hero-switch active\" data-provider-target=\"all\" role=\"tab\" aria-selected=\"true\">All sections</button>");
            foreach (var providerSection in overview.ProviderSections) {
                sb.Append("        <button type=\"button\" class=\"hero-switch\" data-provider-target=\"")
                    .Append(Html(providerSection.ProviderId))
                    .Append("\" role=\"tab\" aria-selected=\"false\">")
                    .Append(Html(providerSection.Title))
                    .AppendLine("</button>");
            }
            sb.AppendLine("      </div>");
        } else {
            sb.AppendLine("      <div></div>");
        }
        sb.AppendLine("      <div class=\"theme-switcher\" role=\"tablist\" aria-label=\"Theme selector\">");
        sb.AppendLine("        <button type=\"button\" class=\"theme-switch\" data-theme-target=\"light\" role=\"tab\" aria-selected=\"false\" title=\"Light theme\" aria-label=\"Light theme\"><span class=\"theme-icon\">☀</span></button>");
        sb.AppendLine("        <button type=\"button\" class=\"theme-switch active\" data-theme-target=\"system\" role=\"tab\" aria-selected=\"true\" title=\"System theme\" aria-label=\"System theme\"><span class=\"theme-icon\">◐</span></button>");
        sb.AppendLine("        <button type=\"button\" class=\"theme-switch\" data-theme-target=\"dark\" role=\"tab\" aria-selected=\"false\" title=\"Dark theme\" aria-label=\"Dark theme\"><span class=\"theme-icon\">☾</span></button>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </div>");
        sb.AppendLine("    <div class=\"divider\"></div>");
    }

    private static void AppendHeroStat(StringBuilder sb, string label, string value) {
        sb.AppendLine("        <div class=\"hero-stat\">");
        sb.Append("          <div class=\"hero-label\">").Append(Html(label.ToUpperInvariant())).AppendLine("</div>");
        sb.Append("          <div class=\"hero-value\">").Append(Html(value)).AppendLine("</div>");
        sb.AppendLine("        </div>");
    }

    private static void AppendProviderSection(StringBuilder sb, UsageTelemetryOverviewProviderSection section) {
        var accentColors = ResolveProviderAccentColors(section.ProviderId);
        var hasMonthly = section.MonthlyUsage.Count > 0;
        var hasModels = section.MostUsedModel is not null || section.RecentModel is not null || section.TopModels.Count > 0;
        var hasPricing = section.ApiCostEstimate is not null;
        var hasComposition = section.Composition is not null && section.Composition.Items.Count > 0;
        var hasAdditionalInsights = section.AdditionalInsights.Count > 0;
        var hasActivity = HasActivityData(section);
        var providerSectionId = "provider-section-" + section.ProviderId.Trim().ToLowerInvariant();
        sb.Append("    <section class=\"provider-section\" id=\"")
            .Append(Html(providerSectionId))
            .Append("\" data-provider=\"")
            .Append(Html(section.ProviderId))
            .AppendLine("\">");
        sb.AppendLine("      <div class=\"provider-shell\">");
        sb.AppendLine("      <div class=\"provider-header\">");
        sb.AppendLine("        <div>");
        sb.Append("          <h2 class=\"provider-title\">").Append(Html(section.Title)).AppendLine("</h2>");
        sb.Append("          <div class=\"provider-subtitle\">").Append(Html(section.Subtitle)).AppendLine("</div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-metrics\">");
        if (section.Metrics.Count > 0) {
            foreach (var metric in section.Metrics) {
                AppendProviderMetric(sb, metric);
            }
        } else {
            AppendProviderMetric(sb, new UsageTelemetryOverviewSectionMetric("input", "Input Tokens", FormatCompact(section.InputTokens), FormatPercent(section.InputTokens, section.TotalTokens) + " of section total", ComputeRatio(section.InputTokens, section.TotalTokens), accentColors.Input));
            AppendProviderMetric(sb, new UsageTelemetryOverviewSectionMetric("output", "Output Tokens", FormatCompact(section.OutputTokens), FormatPercent(section.OutputTokens, section.TotalTokens) + " of section total", ComputeRatio(section.OutputTokens, section.TotalTokens), accentColors.Output));
            AppendProviderMetric(sb, new UsageTelemetryOverviewSectionMetric("total", "Total Tokens", FormatCompact(section.TotalTokens), "100% of section total", section.TotalTokens > 0 ? 1d : 0d, accentColors.Total));
        }
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div class=\"provider-datasets\">");
        sb.AppendLine("        <div class=\"provider-dataset-tabs\" role=\"tablist\" aria-label=\"Section datasets\">");
        sb.AppendLine("          <button type=\"button\" class=\"provider-dataset-tab active\" data-provider-panel=\"summary\" role=\"tab\" aria-selected=\"true\">Summary</button>");
        if (hasActivity) {
            sb.AppendLine("          <button type=\"button\" class=\"provider-dataset-tab\" data-provider-panel=\"activity\" role=\"tab\" aria-selected=\"false\">Activity</button>");
        }
        if (hasModels) {
            sb.AppendLine("          <button type=\"button\" class=\"provider-dataset-tab\" data-provider-panel=\"models\" role=\"tab\" aria-selected=\"false\">Models</button>");
        }
        if (hasPricing) {
            sb.AppendLine("          <button type=\"button\" class=\"provider-dataset-tab\" data-provider-panel=\"pricing\" role=\"tab\" aria-selected=\"false\">Pricing</button>");
        }
        if (hasAdditionalInsights) {
            sb.AppendLine("          <button type=\"button\" class=\"provider-dataset-tab\" data-provider-panel=\"impact\" role=\"tab\" aria-selected=\"false\">Impact</button>");
        }
        if (IsGitHubSection(section) && hasActivity) {
            sb.AppendLine("          <a class=\"provider-dataset-tab provider-dataset-link\" href=\"github-wrapped.html\" target=\"_blank\" rel=\"noopener\">Wrapped</a>");
        }
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-panel active\" data-provider-panel-content=\"summary\">");
        sb.AppendLine("          <div class=\"provider-summary-stack\">");
        sb.AppendLine("            <div class=\"provider-spotlight\">");
        if (section.SpotlightCards.Count > 0) {
            foreach (var card in section.SpotlightCards) {
                AppendSpotlightCard(sb, card);
            }
        } else {
            AppendMiniCard(sb, "Most Used Model", section.MostUsedModel);
            AppendMiniCard(sb, "Recent Use (Last 30 Days)", section.RecentModel);
            AppendMiniMetricCard(sb, "Longest Streak", section.LongestStreakDays + " days");
            AppendMiniMetricCard(sb, "Current Streak", section.CurrentStreakDays + " days");
        }
        sb.AppendLine("            </div>");
        if (hasComposition && IsGitHubSection(section)) {
            AppendProviderComposition(sb, section.Composition!);
        }
        if (hasMonthly && IsGitHubSection(section)) {
            AppendProviderMonthlyUsage(sb, section, accentColors.Total);
        }
        if (IsGitHubSection(section)) {
            AppendGitHubSummaryStrip(sb, section);
        }
        var useSummaryGrid = !IsGitHubSection(section) && (hasComposition || hasMonthly || hasPricing || hasModels || hasAdditionalInsights);
        if (useSummaryGrid) {
            sb.AppendLine("            <div class=\"provider-summary-grid\">");
            sb.AppendLine("              <div class=\"provider-summary-stack\">");
            if (hasComposition) {
                AppendProviderComposition(sb, section.Composition!);
            }
            if (hasMonthly) {
                AppendProviderMonthlyUsage(sb, section, accentColors.Total);
            }
            sb.AppendLine("              </div>");
            sb.AppendLine("              <div class=\"provider-summary-stack\">");
            if (hasPricing) {
                sb.AppendLine("                <article class=\"insight-card\">");
                sb.AppendLine("                  <div class=\"insight-title\">Estimated API route</div>");
                AppendApiCostEstimate(sb, section.ApiCostEstimate);
                sb.AppendLine("                </article>");
            }
            if (hasModels) {
                sb.AppendLine("                <article class=\"insight-card\">");
                sb.AppendLine("                  <div class=\"insight-title\">Top models</div>");
                AppendTopModelsList(sb, section);
                sb.AppendLine("                </article>");
            }
            if (hasAdditionalInsights) {
                foreach (var insight in section.AdditionalInsights) {
                    AppendInsightSection(sb, insight);
                }
            }
            sb.AppendLine("              </div>");
            sb.AppendLine("            </div>");
        } else if (hasPricing || hasModels || (hasAdditionalInsights && !IsGitHubSection(section))) {
            sb.AppendLine("            <div class=\"provider-insights\">");
            if (hasPricing) {
                sb.AppendLine("              <article class=\"insight-card\">");
                sb.AppendLine("                <div class=\"insight-title\">Estimated API route</div>");
                AppendApiCostEstimate(sb, section.ApiCostEstimate);
                sb.AppendLine("              </article>");
            }
            if (hasModels) {
                sb.AppendLine("              <article class=\"insight-card\">");
                sb.AppendLine("                <div class=\"insight-title\">Top models</div>");
                AppendTopModelsList(sb, section);
                sb.AppendLine("              </article>");
            }
            if (hasAdditionalInsights && !IsGitHubSection(section)) {
                foreach (var insight in section.AdditionalInsights) {
                    AppendInsightSection(sb, insight);
                }
            }
            sb.AppendLine("            </div>");
        }
        sb.AppendLine("          </div>");
        sb.AppendLine("        </div>");
        if (hasActivity) {
            sb.AppendLine("        <div class=\"provider-panel\" data-provider-panel-content=\"activity\">");
            if (hasMonthly) {
                AppendProviderMonthlyUsage(sb, section, accentColors.Total);
            }
            sb.AppendLine("          <figure class=\"provider-heatmap\">");
            sb.Append("            <img src=\"").Append(Html(section.Key)).Append(".light.svg\" data-light-src=\"").Append(Html(section.Key)).Append(".light.svg\" data-dark-src=\"").Append(Html(section.Key)).Append(".dark.svg\" alt=\"").Append(Html(section.Title)).AppendLine(" usage heatmap\">");
            sb.AppendLine("          </figure>");
            if (!string.IsNullOrWhiteSpace(section.Note)) {
                sb.Append("          <div class=\"provider-note\">").Append(Html(section.Note!)).AppendLine("</div>");
            }
            AppendProviderLegend(sb, section.ProviderId);
            sb.AppendLine("        </div>");
        }
        if (hasModels) {
            sb.AppendLine("        <div class=\"provider-panel\" data-provider-panel-content=\"models\">");
            sb.AppendLine("          <div class=\"provider-spotlight\">");
            AppendMiniCard(sb, "Most Used Model", section.MostUsedModel);
            AppendMiniCard(sb, "Recent Use (Last 30 Days)", section.RecentModel);
            sb.AppendLine("          </div>");
            sb.AppendLine("          <div class=\"provider-models-stack\">");
            sb.AppendLine("            <article class=\"insight-card\">");
            sb.AppendLine("              <div class=\"insight-title\">Top models</div>");
            AppendTopModelsList(sb, section);
            sb.AppendLine("            </article>");
            sb.AppendLine("          </div>");
            sb.AppendLine("        </div>");
        }
        if (hasPricing) {
            sb.AppendLine("        <div class=\"provider-panel\" data-provider-panel-content=\"pricing\">");
            sb.AppendLine("          <article class=\"insight-card\">");
            sb.AppendLine("            <div class=\"insight-title\">Estimated API route</div>");
            AppendApiCostEstimate(sb, section.ApiCostEstimate);
            sb.AppendLine("          </article>");
            sb.AppendLine("        </div>");
        }
        if (hasAdditionalInsights) {
            sb.AppendLine("        <div class=\"provider-panel\" data-provider-panel-content=\"impact\">");
            if (IsGitHubSection(section)) {
                AppendGitHubImpactExplorer(sb, section);
            } else {
                sb.AppendLine("          <div class=\"provider-insights\">");
                foreach (var insight in section.AdditionalInsights) {
                    AppendInsightSection(sb, insight);
                }
                sb.AppendLine("          </div>");
            }
            sb.AppendLine("        </div>");
        }
        sb.AppendLine("      </div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
    }

    private static bool HasActivityData(UsageTelemetryOverviewProviderSection section) {
        return section.Heatmap.Sections.Any(static entry => entry.Days.Count > 0);
    }

    private static void AppendProviderMetric(StringBuilder sb, UsageTelemetryOverviewSectionMetric metric) {
        sb.AppendLine("          <div class=\"provider-metric\">");
        sb.Append("            <div class=\"metric-label\">").Append(Html(metric.Label.ToUpperInvariant())).AppendLine("</div>");
        sb.Append("            <div class=\"metric-value\">").Append(Html(metric.Value)).AppendLine("</div>");
        sb.Append("            <div class=\"metric-copy\">").Append(Html(metric.Subtitle ?? string.Empty)).AppendLine("</div>");
        sb.AppendLine("            <div class=\"metric-bar\">");
        sb.Append("              <div class=\"metric-fill\" style=\"width:").Append(Html(FormatRatioPercent(metric.Ratio))).Append("%; background:").Append(Html(metric.Color)).AppendLine(";\"></div>");
        sb.AppendLine("            </div>");
        sb.AppendLine("          </div>");
    }

    private static void AppendProviderComposition(StringBuilder sb, UsageTelemetryOverviewComposition composition) {
        sb.AppendLine("      <div class=\"provider-token-mix\">");
        sb.AppendLine("        <div class=\"provider-token-mix-header\">");
        sb.Append("          <div class=\"provider-token-mix-title\">").Append(Html(composition.Title)).AppendLine("</div>");
        sb.Append("          <div class=\"provider-token-mix-copy\">").Append(Html(composition.Copy)).AppendLine("</div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-token-mix-bar\">");
        foreach (var item in composition.Items) {
            AppendProviderTokenSegment(sb, item);
        }
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-token-mix-legend\">");
        foreach (var item in composition.Items) {
            AppendProviderTokenMixItem(sb, item);
        }
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
    }

    private static void AppendProviderTokenSegment(StringBuilder sb, UsageTelemetryOverviewCompositionItem item) {
        if (!item.Ratio.HasValue || item.Ratio.Value <= 0d) {
            return;
        }

        sb.Append("          <span class=\"provider-token-segment\" style=\"width:")
            .Append(Html(FormatRatioPercent(item.Ratio)))
            .Append("%; background:")
            .Append(Html(item.Color))
            .AppendLine(";\"></span>");
    }

    private static void AppendProviderTokenMixItem(StringBuilder sb, UsageTelemetryOverviewCompositionItem item) {
        sb.Append("          <div class=\"provider-token-mix-item\"><span class=\"provider-token-dot\" style=\"background:")
            .Append(Html(item.Color))
            .Append("\"></span>")
            .Append(Html(item.Label))
            .Append(": <strong>")
            .Append(Html(item.Value))
            .Append("</strong>");
        if (!string.IsNullOrWhiteSpace(item.Subtitle)) {
            sb.Append(" <span>(")
                .Append(Html(item.Subtitle!))
                .Append(")</span>");
        }
        sb.AppendLine("</div>");
    }

    private static void AppendProviderLegend(StringBuilder sb, string providerId) {
        var palette = ResolveLegendColors(providerId);
        sb.AppendLine("      <div class=\"provider-legend\">");
        sb.AppendLine("        <span class=\"legend-copy\">Less</span>");
        foreach (var color in palette) {
            sb.Append("        <span class=\"legend-swatch\" style=\"background:").Append(Html(color)).AppendLine("\"></span>");
        }
        sb.AppendLine("        <span class=\"legend-copy\">More</span>");
        sb.AppendLine("      </div>");
    }

    private static void AppendProviderMonthlyUsage(StringBuilder sb, UsageTelemetryOverviewProviderSection section, string accentColor) {
        var months = section.MonthlyUsage ?? Array.Empty<UsageTelemetryOverviewMonthlyUsage>();
        if (months.Count == 0) {
            return;
        }

        var maxTokens = months.Max(static month => month.TotalValue);
        sb.AppendLine("      <div class=\"provider-monthly\">");
        sb.AppendLine("        <div class=\"provider-monthly-header\">");
        sb.Append("          <div class=\"provider-monthly-title\">").Append(Html(section.MonthlyUsageTitle)).AppendLine("</div>");
        sb.Append("          <div class=\"provider-monthly-copy\">").Append(Html(months.Count.ToString(CultureInfo.InvariantCulture))).AppendLine(" month window</div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-monthly-grid\">");
        foreach (var month in months) {
            var height = maxTokens <= 0L ? 4d : Math.Max(4d, month.TotalValue / (double)maxTokens * 90d);
            var alpha = month.TotalValue <= 0L ? "33" : Math.Max(64, Math.Min(255, (int)Math.Round(month.TotalValue / (double)Math.Max(1L, maxTokens) * 255d))).ToString("X2", CultureInfo.InvariantCulture);
            var monthColor = month.TotalValue <= 0L ? "#dfdfdf" : accentColor + alpha;
            var title = $"{month.Key}: {FormatCompact(month.TotalValue)} {section.MonthlyUsageUnitsLabel}";
            if (month.ActiveDays > 0) {
                title += $" across {month.ActiveDays} active day(s)";
            }

            sb.Append("          <div class=\"provider-month\" title=\"").Append(Html(title)).AppendLine("\">");
            sb.AppendLine("            <div class=\"provider-month-bar-wrap\">");
            sb.Append("              <div class=\"provider-month-bar\" style=\"height:")
                .Append(Html(height.ToString("0.##", CultureInfo.InvariantCulture)))
                .Append("px; background:")
                .Append(Html(monthColor))
                .AppendLine(";\"></div>");
            sb.AppendLine("            </div>");
            sb.Append("            <div class=\"provider-month-label\">").Append(Html(month.Label)).AppendLine("</div>");
            sb.Append("            <div class=\"provider-month-value\">").Append(Html(FormatCompact(month.TotalValue))).AppendLine("</div>");
            sb.AppendLine("          </div>");
        }
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
    }

    private static void AppendProviderInsights(StringBuilder sb, UsageTelemetryOverviewProviderSection section) {
        var hasTopModels = section.TopModels.Count > 0;
        var hasEstimate = section.ApiCostEstimate is not null;
        if (!hasTopModels && !hasEstimate) {
            return;
        }

        sb.AppendLine("      <div class=\"provider-insights\">");
        sb.AppendLine("        <article class=\"insight-card\">");
        sb.AppendLine("          <div class=\"insight-title\">Top models</div>");
        if (hasTopModels) {
            sb.AppendLine("          <div class=\"rank-list\">");
            var rank = 1;
            foreach (var model in section.TopModels) {
                sb.AppendLine("            <div class=\"rank-row\">");
                sb.Append("              <div class=\"rank-index\">").Append(rank.ToString(CultureInfo.InvariantCulture)).AppendLine(".</div>");
                sb.Append("              <div class=\"rank-label\">").Append(Html(model.Model)).AppendLine("</div>");
                sb.Append("              <div class=\"rank-value\">")
                    .Append(Html(FormatCompact(model.TotalTokens)))
                    .Append(" (")
                    .Append(Html(model.SharePercent.ToString("0.#", CultureInfo.InvariantCulture)))
                    .AppendLine("%)</div>");
                sb.AppendLine("            </div>");
                rank++;
            }
            sb.AppendLine("          </div>");
        } else {
            sb.AppendLine("          <div class=\"estimate-note\">No model breakdown available.</div>");
        }
        sb.AppendLine("        </article>");

        sb.AppendLine("        <article class=\"insight-card\">");
        sb.AppendLine("          <div class=\"insight-title\">Estimated API route</div>");
        AppendApiCostEstimate(sb, section.ApiCostEstimate);
        sb.AppendLine("        </article>");
        sb.AppendLine("      </div>");
    }

    private static bool IsGitHubSection(UsageTelemetryOverviewProviderSection section) {
        return string.Equals(section.ProviderId, "github", StringComparison.OrdinalIgnoreCase);
    }

    private static UsageTelemetryOverviewInsightSection? FindInsight(
        UsageTelemetryOverviewProviderSection section,
        string key) {
        return section.AdditionalInsights.FirstOrDefault(insight =>
            string.Equals(insight.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private static void AppendGitHubSummaryStrip(StringBuilder sb, UsageTelemetryOverviewProviderSection section) {
        var yearComparison = FindInsight(section, "github-year-comparison");
        var scopeSplit = FindInsight(section, "github-scope-split");
        var recentRepositories = FindInsight(section, "github-recent-repositories");
        var ownerImpact = FindInsight(section, "github-owner-impact");
        var ownerSections = FindGitHubOwnerInsights(section);

        if (yearComparison is null && scopeSplit is null && recentRepositories is null && ownerImpact is null) {
            return;
        }

        sb.AppendLine("            <div class=\"provider-feature-grid\">");
        if (yearComparison is not null) {
            AppendGitHubComparisonCard(sb, yearComparison);
        }
        if (scopeSplit is not null) {
            AppendGitHubScopeCard(sb, scopeSplit);
        }
        if (recentRepositories is not null) {
            AppendGitHubRecentRepositoriesCard(sb, recentRepositories);
        }
        sb.AppendLine("            </div>");
        if (ownerSections.Length > 0) {
            sb.AppendLine("            <div class=\"github-scope-pills\">");
            sb.AppendLine("              <span class=\"provider-badge scope\">Owner lenses available in Impact</span>");
            foreach (var ownerSection in ownerSections) {
                sb.Append("              <span class=\"provider-badge scope\">")
                    .Append(Html(ownerSection.Title))
                    .AppendLine("</span>");
            }
            sb.AppendLine("            </div>");
            AppendGitHubSummaryOwnerExplorer(sb, ownerImpact, scopeSplit, ownerSections);
        }
    }

    private static void AppendGitHubImpactExplorer(StringBuilder sb, UsageTelemetryOverviewProviderSection section) {
        var topRepositories = FindInsight(section, "github-top-repositories");
        var topRepositoriesByForks = FindInsight(section, "github-top-repositories-forks");
        var topRepositoriesByHealth = FindInsight(section, "github-top-repositories-health");
        var recentRepositories = FindInsight(section, "github-recent-repositories");
        var topLanguages = FindInsight(section, "github-top-languages");
        var ownerImpact = FindInsight(section, "github-owner-impact");
        var scopeSplit = FindInsight(section, "github-scope-split");
        var ownerSections = FindGitHubOwnerInsights(section);

        sb.AppendLine("          <div class=\"github-impact-shell\">");
        sb.AppendLine("            <div class=\"github-lens-switcher\" role=\"tablist\" aria-label=\"GitHub impact lenses\">");
        sb.AppendLine("              <button type=\"button\" class=\"github-lens-tab active\" data-github-lens=\"impact\">Impact</button>");
        sb.AppendLine("              <button type=\"button\" class=\"github-lens-tab\" data-github-lens=\"recent\">Recent</button>");
        if (ownerSections.Length > 0) {
            sb.AppendLine("              <button type=\"button\" class=\"github-lens-tab\" data-github-lens=\"owners\">Owners</button>");
        }
        if (topLanguages is not null) {
            sb.AppendLine("              <button type=\"button\" class=\"github-lens-tab\" data-github-lens=\"languages\">Languages</button>");
        }
        sb.AppendLine("            </div>");

        sb.AppendLine("            <div class=\"github-lens-panel active\" data-github-lens-content=\"impact\">");
        sb.AppendLine("              <div class=\"github-impact-toolbar\">");
        sb.AppendLine("                <div class=\"github-repo-sorter\">");
        sb.AppendLine("                  <div class=\"github-repo-sort-kicker\">Repository ranking</div>");
        sb.AppendLine("                  <div class=\"github-repo-sort-tabs\" role=\"tablist\" aria-label=\"GitHub repository ranking\">");
        sb.AppendLine("                    <button type=\"button\" class=\"github-repo-sort-tab active\" data-github-repo-sort=\"stars\">Top by stars</button>");
        if (topRepositoriesByForks is not null) {
            sb.AppendLine("                    <button type=\"button\" class=\"github-repo-sort-tab\" data-github-repo-sort=\"forks\">Top by forks</button>");
        }
        if (topRepositoriesByHealth is not null) {
            sb.AppendLine("                    <button type=\"button\" class=\"github-repo-sort-tab\" data-github-repo-sort=\"health\">Top by health</button>");
        }
        sb.AppendLine("                  </div>");
        sb.AppendLine("                </div>");
        sb.AppendLine("              </div>");
        sb.AppendLine("              <div class=\"provider-insights tight\">");
        if (topRepositories is not null && topRepositoriesByHealth is not null) {
            AppendGitHubRepositoryComparisonCard(sb, topRepositories, topRepositoriesByHealth);
        }
        sb.AppendLine("                <div class=\"github-repo-sort-panel active\" data-github-repo-sort-content=\"stars\">");
        if (topRepositories is not null) {
            AppendInsightSection(sb, topRepositories);
        }
        sb.AppendLine("                </div>");
        if (topRepositoriesByForks is not null) {
            sb.AppendLine("                <div class=\"github-repo-sort-panel\" data-github-repo-sort-content=\"forks\">");
            AppendInsightSection(sb, topRepositoriesByForks);
            sb.AppendLine("                </div>");
        }
        if (topRepositoriesByHealth is not null) {
            sb.AppendLine("                <div class=\"github-repo-sort-panel\" data-github-repo-sort-content=\"health\">");
            AppendInsightSection(sb, topRepositoriesByHealth);
            sb.AppendLine("                </div>");
        }
        if (ownerImpact is not null) {
            AppendInsightSection(sb, ownerImpact);
        }
        if (scopeSplit is not null) {
            AppendInsightSection(sb, scopeSplit);
        }
        sb.AppendLine("              </div>");
        sb.AppendLine("            </div>");

        sb.AppendLine("            <div class=\"github-lens-panel\" data-github-lens-content=\"recent\">");
        sb.AppendLine("              <div class=\"provider-feature-grid\">");
        if (recentRepositories is not null) {
            AppendGitHubRecentRepositoriesCard(sb, recentRepositories);
        }
        if (scopeSplit is not null) {
            AppendGitHubScopeCard(sb, scopeSplit);
        }
        if (topRepositories is not null) {
            AppendFeatureCard(sb, topRepositories);
        }
        sb.AppendLine("              </div>");
        sb.AppendLine("            </div>");

        if (ownerSections.Length > 0) {
            sb.AppendLine("            <div class=\"github-lens-panel\" data-github-lens-content=\"owners\">");
            sb.AppendLine("              <div class=\"github-owner-explorer\">");
            sb.AppendLine("                <div class=\"github-owner-switcher\" role=\"tablist\" aria-label=\"GitHub owner scope\">");
            sb.AppendLine("                  <button type=\"button\" class=\"github-owner-chip active\" data-github-owner=\"all\">All scope</button>");
            foreach (var ownerSection in ownerSections) {
                sb.Append("                  <button type=\"button\" class=\"github-owner-chip\" data-github-owner=\"")
                    .Append(Html(ownerSection.Key))
                    .Append("\">")
                    .Append(Html(ownerSection.Title))
                    .AppendLine("</button>");
            }
            sb.AppendLine("                </div>");
            sb.AppendLine("                <div class=\"github-owner-panel active\" data-github-owner-content=\"all\">");
            sb.AppendLine("                  <div class=\"github-impact-compact\">");
            if (ownerImpact is not null) {
                AppendInsightSection(sb, ownerImpact);
            }
            if (topRepositories is not null) {
                AppendInsightSection(sb, topRepositories);
            }
            sb.AppendLine("                  </div>");
            sb.AppendLine("                </div>");
            foreach (var ownerSection in ownerSections) {
                sb.Append("                <div class=\"github-owner-panel\" data-github-owner-content=\"")
                    .Append(Html(ownerSection.Key))
                    .AppendLine("\">");
                sb.AppendLine("                  <div class=\"github-impact-compact\">");
                AppendInsightSection(sb, ownerSection);
                if (topLanguages is not null) {
                    AppendInsightSection(sb, topLanguages);
                }
                sb.AppendLine("                  </div>");
                sb.AppendLine("                </div>");
            }
            sb.AppendLine("              </div>");
            sb.AppendLine("            </div>");
        }

        if (topLanguages is not null) {
            sb.AppendLine("            <div class=\"github-lens-panel\" data-github-lens-content=\"languages\">");
            sb.AppendLine("              <div class=\"provider-insights tight\">");
            AppendInsightSection(sb, topLanguages);
            if (ownerImpact is not null) {
                AppendInsightSection(sb, ownerImpact);
            }
            sb.AppendLine("              </div>");
            sb.AppendLine("            </div>");
        }

        sb.AppendLine("          </div>");
    }

    private static void AppendGitHubRepositoryComparisonCard(
        StringBuilder sb,
        UsageTelemetryOverviewInsightSection topRepositories,
        UsageTelemetryOverviewInsightSection topRepositoriesByHealth) {
        var starsRow = topRepositories.Rows.FirstOrDefault();
        var healthRow = topRepositoriesByHealth.Rows.FirstOrDefault();
        if (starsRow is null || healthRow is null) {
            return;
        }

        sb.AppendLine("                <article class=\"provider-compare-card\">");
        sb.AppendLine("                  <div class=\"provider-feature-kicker\">Repository comparison</div>");
        sb.AppendLine("                  <div class=\"provider-feature-headline\">Impact vs momentum</div>");
        sb.AppendLine("                  <div class=\"provider-feature-copy\">Compare the repository leading on raw stars with the repository currently leading on health across the selected owner scope.</div>");
        sb.AppendLine("                  <div class=\"provider-compare-grid\">");
        AppendGitHubCompareSide(sb, new UsageTelemetryOverviewInsightRow(
            "Top by stars",
            starsRow.Label,
            starsRow.Value + (string.IsNullOrWhiteSpace(starsRow.Subtitle) ? string.Empty : " · " + starsRow.Subtitle),
            starsRow.Ratio,
            starsRow.Href), false);
        sb.AppendLine("                    <div class=\"provider-compare-arrow\">⇄</div>");
        AppendGitHubCompareSide(sb, new UsageTelemetryOverviewInsightRow(
            "Top by health",
            healthRow.Label,
            healthRow.Value + (string.IsNullOrWhiteSpace(healthRow.Subtitle) ? string.Empty : " · " + healthRow.Subtitle),
            healthRow.Ratio,
            healthRow.Href), true);
        sb.AppendLine("                  </div>");
        sb.AppendLine("                </article>");
    }

    private static void AppendGitHubSummaryOwnerExplorer(
        StringBuilder sb,
        UsageTelemetryOverviewInsightSection? ownerImpact,
        UsageTelemetryOverviewInsightSection? scopeSplit,
        IReadOnlyList<UsageTelemetryOverviewInsightSection> ownerSections) {
        if (ownerSections.Count == 0) {
            return;
        }

        sb.AppendLine("            <div class=\"github-summary-owner-shell github-owner-explorer\">");
        sb.AppendLine("              <div class=\"github-owner-switcher\" role=\"tablist\" aria-label=\"GitHub owner scope summary\">");
        sb.AppendLine("                <button type=\"button\" class=\"github-owner-chip active\" data-github-owner=\"all\">All scope</button>");
        foreach (var ownerSection in ownerSections) {
            sb.Append("                <button type=\"button\" class=\"github-owner-chip\" data-github-owner=\"")
                .Append(Html(ownerSection.Key))
                .Append("\">")
                .Append(Html(ownerSection.Title))
                .AppendLine("</button>");
        }
        sb.AppendLine("              </div>");
        sb.AppendLine("              <div class=\"github-owner-panel active\" data-github-owner-content=\"all\">");
        sb.AppendLine("                <div class=\"github-impact-compact\">");
        if (scopeSplit is not null) {
            AppendFeatureCard(sb, scopeSplit);
        }
        if (ownerImpact is not null) {
            AppendInsightSection(sb, ownerImpact);
        }
        sb.AppendLine("                </div>");
        sb.AppendLine("              </div>");

        foreach (var ownerSection in ownerSections) {
            sb.Append("              <div class=\"github-owner-panel\" data-github-owner-content=\"")
                .Append(Html(ownerSection.Key))
                .AppendLine("\">");
            sb.AppendLine("                <div class=\"github-impact-compact\">");
            AppendInsightSection(sb, ownerSection);
            sb.AppendLine("                </div>");
            sb.AppendLine("              </div>");
        }

        sb.AppendLine("            </div>");
    }

    private static UsageTelemetryOverviewInsightSection[] FindGitHubOwnerInsights(UsageTelemetryOverviewProviderSection section) {
        return section.AdditionalInsights
            .Where(static insight =>
                insight.Key.StartsWith("github-owner-", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(insight.Key, "github-owner-impact", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static insight => insight.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AppendFeatureCard(StringBuilder sb, UsageTelemetryOverviewInsightSection insight) {
        sb.AppendLine("              <article class=\"provider-feature-card\">");
        sb.Append("                <div class=\"provider-feature-kicker\">").Append(Html(insight.Title)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(insight.Headline)) {
            sb.Append("                <div class=\"provider-feature-headline\">").Append(Html(insight.Headline!)).AppendLine("</div>");
        }
        if (!string.IsNullOrWhiteSpace(insight.Note)) {
            sb.Append("                <div class=\"provider-feature-copy\">").Append(Html(insight.Note!)).AppendLine("</div>");
        }

        if (insight.Rows.Count > 0) {
            sb.AppendLine("                <div class=\"provider-feature-rows\">");
            foreach (var row in insight.Rows.Take(4)) {
                sb.AppendLine("                  <div class=\"provider-feature-row\">");
                sb.AppendLine("                    <div class=\"provider-feature-row-head\">");
                sb.Append("                      <div class=\"provider-feature-row-label\">");
                if (!string.IsNullOrWhiteSpace(row.Href)) {
                    sb.Append("<a href=\"").Append(Html(row.Href!)).Append("\" target=\"_blank\" rel=\"noopener\" style=\"color:inherit;text-decoration:none;\">")
                        .Append(Html(row.Label))
                        .Append("</a>");
                } else {
                    sb.Append(Html(row.Label));
                }
                sb.AppendLine("</div>");
                sb.Append("                      <div class=\"provider-feature-row-value\">").Append(Html(row.Value)).AppendLine("</div>");
                sb.AppendLine("                    </div>");
                if (!string.IsNullOrWhiteSpace(row.Subtitle)) {
                    sb.Append("                    <div class=\"provider-feature-row-copy\">").Append(Html(row.Subtitle!)).AppendLine("</div>");
                }
                if (row.Ratio.HasValue && row.Ratio.Value > 0d) {
                    sb.AppendLine("                    <div class=\"provider-feature-row-bar\">");
                    sb.Append("                      <div class=\"provider-feature-row-fill\" style=\"width:")
                        .Append(Html(FormatRatioPercent(row.Ratio)))
                        .AppendLine("%;\"></div>");
                    sb.AppendLine("                    </div>");
                }
                sb.AppendLine("                  </div>");
            }
            sb.AppendLine("                </div>");
        }

        sb.AppendLine("              </article>");
    }

    private static void AppendGitHubComparisonCard(StringBuilder sb, UsageTelemetryOverviewInsightSection insight) {
        sb.AppendLine("              <article class=\"provider-compare-card\">");
        sb.Append("                <div class=\"provider-feature-kicker\">").Append(Html(insight.Title)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(insight.Headline)) {
            sb.Append("                <div class=\"provider-feature-headline\">").Append(Html(insight.Headline!)).AppendLine("</div>");
        }
        if (!string.IsNullOrWhiteSpace(insight.Note)) {
            sb.Append("                <div class=\"provider-feature-copy\">").Append(Html(insight.Note!)).AppendLine("</div>");
        }

        if (insight.Rows.Count >= 2) {
            var left = insight.Rows[0];
            var right = insight.Rows[1];
            sb.AppendLine("                <div class=\"provider-compare-grid\">");
            AppendGitHubCompareSide(sb, left, false);
            sb.AppendLine("                  <div class=\"provider-compare-arrow\">→</div>");
            AppendGitHubCompareSide(sb, right, true);
            sb.AppendLine("                </div>");
        } else if (insight.Rows.Count == 1) {
            var row = insight.Rows[0];
            sb.AppendLine("                <div class=\"provider-feature-rows\">");
            sb.AppendLine("                  <div class=\"provider-feature-row\">");
            sb.AppendLine("                    <div class=\"provider-feature-row-head\">");
            sb.Append("                      <div class=\"provider-feature-row-label\">").Append(Html(row.Label)).AppendLine("</div>");
            sb.Append("                      <div class=\"provider-feature-row-value\">").Append(Html(row.Value)).AppendLine("</div>");
            sb.AppendLine("                    </div>");
            if (!string.IsNullOrWhiteSpace(row.Subtitle)) {
                sb.Append("                    <div class=\"provider-feature-row-copy\">").Append(Html(row.Subtitle!)).AppendLine("</div>");
            }
            sb.AppendLine("                  </div>");
            sb.AppendLine("                </div>");
        }

        sb.AppendLine("              </article>");
    }

    private static void AppendGitHubCompareSide(StringBuilder sb, UsageTelemetryOverviewInsightRow row, bool emphasize) {
        sb.Append("                  <div class=\"provider-compare-side");
        if (emphasize) {
            sb.Append(" right");
        }
        sb.AppendLine("\">");
        sb.Append("                    <div class=\"provider-compare-label\">").Append(Html(row.Label)).AppendLine("</div>");
        sb.Append("                    <div class=\"provider-compare-value\">").Append(Html(row.Value)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(row.Subtitle)) {
            sb.Append("                    <div class=\"provider-compare-subtitle\">").Append(Html(row.Subtitle!)).AppendLine("</div>");
        }
        sb.AppendLine("                  </div>");
    }

    private static void AppendGitHubScopeCard(StringBuilder sb, UsageTelemetryOverviewInsightSection insight) {
        sb.AppendLine("              <article class=\"provider-feature-card\">");
        sb.Append("                <div class=\"provider-feature-kicker\">").Append(Html(insight.Title)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(insight.Headline)) {
            sb.Append("                <div class=\"provider-feature-headline\">").Append(Html(insight.Headline!)).AppendLine("</div>");
        }
        if (!string.IsNullOrWhiteSpace(insight.Note)) {
            sb.Append("                <div class=\"provider-feature-copy\">").Append(Html(insight.Note!)).AppendLine("</div>");
        }
        sb.AppendLine("                <div class=\"provider-feature-rows\">");
        foreach (var row in insight.Rows.Take(2)) {
            sb.AppendLine("                  <div class=\"provider-feature-row\">");
            sb.AppendLine("                    <div class=\"provider-feature-row-head\">");
            sb.Append("                      <div class=\"provider-feature-row-label\">").Append(Html(row.Label)).AppendLine("</div>");
            sb.Append("                      <div class=\"provider-feature-row-value\">").Append(Html(row.Value)).AppendLine("</div>");
            sb.AppendLine("                    </div>");
            if (!string.IsNullOrWhiteSpace(row.Subtitle)) {
                sb.Append("                    <div class=\"provider-feature-row-copy\">").Append(Html(row.Subtitle!)).AppendLine("</div>");
            }
            if (row.Ratio.HasValue) {
                sb.AppendLine("                    <div class=\"provider-feature-row-bar\">");
                sb.Append("                      <div class=\"provider-feature-row-fill\" style=\"width:")
                    .Append(Html(FormatRatioPercent(row.Ratio)))
                    .AppendLine("%;\"></div>");
                sb.AppendLine("                    </div>");
            }
            sb.AppendLine("                  </div>");
        }
        sb.AppendLine("                </div>");
        sb.AppendLine("                <div class=\"provider-badge-row\">");
        sb.AppendLine("                  <span class=\"provider-badge scope\">Personal activity</span>");
        sb.AppendLine("                  <span class=\"provider-badge scope\">Org repository impact</span>");
        sb.AppendLine("                </div>");
        sb.AppendLine("              </article>");
    }

    private static void AppendGitHubRecentRepositoriesCard(StringBuilder sb, UsageTelemetryOverviewInsightSection insight) {
        sb.AppendLine("              <article class=\"provider-feature-card\">");
        sb.Append("                <div class=\"provider-feature-kicker\">").Append(Html(insight.Title)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(insight.Headline)) {
            sb.Append("                <div class=\"provider-feature-headline\">").Append(Html(insight.Headline!)).AppendLine("</div>");
        }
        if (!string.IsNullOrWhiteSpace(insight.Note)) {
            sb.Append("                <div class=\"provider-feature-copy\">").Append(Html(insight.Note!)).AppendLine("</div>");
        }
        sb.AppendLine("                <div class=\"provider-feature-rows\">");
        foreach (var row in insight.Rows.Take(4)) {
            var (badgeClass, badgeLabel) = ResolveRepoHealthBadge(row.Value, row.Subtitle);
            sb.AppendLine("                  <div class=\"provider-feature-row\">");
            sb.AppendLine("                    <div class=\"provider-feature-row-head\">");
            sb.Append("                      <div class=\"provider-feature-row-label\">");
            if (!string.IsNullOrWhiteSpace(row.Href)) {
                sb.Append("<a href=\"").Append(Html(row.Href!)).Append("\" target=\"_blank\" rel=\"noopener\" style=\"color:inherit;text-decoration:none;\">")
                    .Append(Html(row.Label))
                    .Append("</a>");
            } else {
                sb.Append(Html(row.Label));
            }
            sb.AppendLine("</div>");
            sb.Append("                      <div class=\"provider-feature-row-value\">").Append(Html(row.Value)).AppendLine("</div>");
            sb.AppendLine("                    </div>");
            if (!string.IsNullOrWhiteSpace(row.Subtitle)) {
                sb.Append("                    <div class=\"provider-feature-row-copy\">").Append(Html(row.Subtitle!)).AppendLine("</div>");
            }
            sb.AppendLine("                    <div class=\"provider-badge-row\">");
            sb.Append("                      <span class=\"provider-badge ").Append(Html(badgeClass)).Append("\">").Append(Html(badgeLabel)).AppendLine("</span>");
            sb.AppendLine("                    </div>");
            sb.AppendLine("                  </div>");
        }
        sb.AppendLine("                </div>");
        sb.AppendLine("              </article>");
    }

    private static (string CssClass, string Label) ResolveRepoHealthBadge(string? yyyyMmDd, string? subtitle) {
        var hinted = ResolveRepoHealthBadgeFromSubtitle(subtitle);
        if (hinted.HasValue) {
            return hinted.Value;
        }

        if (DateTime.TryParseExact(
                yyyyMmDd,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)) {
            var daysOld = (DateTime.UtcNow.Date - parsed.Date).Days;
            if (daysOld <= 14) {
                return ("active", "Active");
            }

            if (daysOld <= 60) {
                return ("warm", "Warm");
            }

            if (daysOld <= 365) {
                return ("established", "Established");
            }

            return ("dormant", "Dormant");
        }

        return ("dormant", "Unknown");
    }

    private static (string CssClass, string Label)? ResolveRepoHealthBadgeFromSubtitle(string? subtitle) {
        var normalized = HeatmapText.NormalizeOptionalText(subtitle);
        if (string.IsNullOrWhiteSpace(normalized)) {
            return null;
        }

        var text = normalized!;
        if (text.StartsWith("Rising ·", StringComparison.OrdinalIgnoreCase)) {
            return ("rising", "Rising");
        }

        if (text.StartsWith("Active ·", StringComparison.OrdinalIgnoreCase)) {
            return ("active", "Active");
        }

        if (text.StartsWith("Established ·", StringComparison.OrdinalIgnoreCase)) {
            return ("established", "Established");
        }

        if (text.StartsWith("Warm ·", StringComparison.OrdinalIgnoreCase)) {
            return ("warm", "Warm");
        }

        if (text.StartsWith("Dormant ·", StringComparison.OrdinalIgnoreCase)) {
            return ("dormant", "Dormant");
        }

        return null;
    }

    private static void AppendTopModelsList(StringBuilder sb, UsageTelemetryOverviewProviderSection section) {
        if (section.TopModels.Count == 0) {
            sb.AppendLine("          <div class=\"estimate-note\">No model breakdown available.</div>");
            return;
        }

        sb.AppendLine("          <div class=\"rank-list\">");
        var rank = 1;
        foreach (var model in section.TopModels) {
            sb.AppendLine("            <div class=\"rank-row\">");
            sb.Append("              <div class=\"rank-index\">").Append(rank.ToString(CultureInfo.InvariantCulture)).AppendLine(".</div>");
            sb.Append("              <div class=\"rank-label\">").Append(Html(model.Model)).AppendLine("</div>");
            sb.Append("              <div class=\"rank-value\">")
                .Append(Html(FormatCompact(model.TotalTokens)))
                .Append(" (")
                .Append(Html(model.SharePercent.ToString("0.#", CultureInfo.InvariantCulture)))
                .AppendLine("%)</div>");
            sb.AppendLine("            </div>");
            rank++;
        }
        sb.AppendLine("          </div>");
    }

    private static void AppendInsightSection(StringBuilder sb, UsageTelemetryOverviewInsightSection insight) {
        sb.AppendLine("              <article class=\"insight-card\">");
        sb.Append("                <div class=\"insight-title\">").Append(Html(insight.Title)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(insight.Headline)) {
            sb.Append("                <div class=\"estimate-value\">").Append(Html(insight.Headline!)).AppendLine("</div>");
        }
        if (!string.IsNullOrWhiteSpace(insight.Note)) {
            sb.Append("                <div class=\"estimate-note\">").Append(Html(insight.Note!)).AppendLine("</div>");
        }
        if (insight.Rows.Count == 0) {
            sb.AppendLine("                <div class=\"estimate-note\">No detailed rows available.</div>");
        } else {
            sb.AppendLine("                <div class=\"rank-list\">");
            foreach (var row in insight.Rows) {
                sb.AppendLine("                  <div class=\"rank-row\">");
                sb.AppendLine("                    <div class=\"rank-index\">•</div>");
                sb.Append("                    <div class=\"rank-label\">");
                if (!string.IsNullOrWhiteSpace(row.Href)) {
                    sb.Append("<a href=\"").Append(Html(row.Href!)).Append("\" target=\"_blank\" rel=\"noopener\" style=\"color:inherit;text-decoration:none;\">")
                        .Append(Html(row.Label))
                        .Append("</a>");
                } else {
                    sb.Append(Html(row.Label));
                }
                sb.AppendLine("</div>");
                sb.Append("                    <div class=\"rank-value\">").Append(Html(row.Value)).AppendLine("</div>");
                sb.AppendLine("                  </div>");
                if (!string.IsNullOrWhiteSpace(row.Subtitle)) {
                    sb.Append("                  <div class=\"estimate-note\" style=\"margin:2px 0 10px 44px;\">").Append(Html(row.Subtitle!)).AppendLine("</div>");
                }
            }
            sb.AppendLine("                </div>");
        }
        sb.AppendLine("              </article>");
    }

    private static void AppendApiCostEstimate(StringBuilder sb, UsageTelemetryOverviewApiCostEstimate? estimate) {
        if (estimate is null) {
            sb.AppendLine("          <div class=\"estimate-note\">No model pricing coverage available for this section yet.</div>");
            return;
        }

        sb.AppendLine("          <div class=\"estimate-total\">");
        sb.Append("            <div class=\"estimate-value\">$").Append(Html(FormatCurrencyCompact(estimate.TotalEstimatedCostUsd))).AppendLine("</div>");
        sb.Append("            <div class=\"estimate-copy\">Estimated from exact token telemetry using current public API rates.</div>");
        sb.AppendLine("          </div>");
        if (estimate.TopDrivers.Count > 0) {
            sb.AppendLine("          <div class=\"rank-list\">");
            foreach (var driver in estimate.TopDrivers) {
                sb.AppendLine("            <div class=\"rank-row\">");
                sb.AppendLine("              <div class=\"rank-index\">$</div>");
                sb.Append("              <div class=\"rank-label\">").Append(Html(driver.Model)).AppendLine("</div>");
                sb.Append("              <div class=\"rank-value\">$")
                    .Append(Html(FormatCurrencyCompact(driver.EstimatedCostUsd)))
                    .Append(" (")
                    .Append(Html(driver.SharePercent.ToString("0.#", CultureInfo.InvariantCulture)))
                    .AppendLine("%)</div>");
                sb.AppendLine("            </div>");
            }
            sb.AppendLine("          </div>");
        }

        var totalTokens = estimate.CoveredTokens + estimate.UncoveredTokens;
        var coveredPercent = totalTokens <= 0L ? 0d : estimate.CoveredTokens / (double)totalTokens * 100d;
        sb.Append("          <div class=\"estimate-note\">Priced coverage: ")
            .Append(Html(coveredPercent.ToString("0.#", CultureInfo.InvariantCulture)))
            .Append("% of tokens");
        if (estimate.UncoveredTokens > 0L) {
            sb.Append(" (")
                .Append(Html(FormatCompact(estimate.UncoveredTokens)))
                .Append(" unpriced)");
        }
        sb.AppendLine(".</div>");
    }

    private static void AppendMiniCard(StringBuilder sb, string label, UsageTelemetryOverviewModelHighlight? highlight) {
        sb.AppendLine("        <article class=\"mini-card\">");
        sb.Append("          <div class=\"mini-label\">").Append(Html(label.ToUpperInvariant())).AppendLine("</div>");
        if (highlight is null) {
            sb.AppendLine("          <div class=\"mini-value\">n/a</div>");
        } else {
            sb.Append("          <div class=\"mini-value\">").Append(Html(highlight.Model)).Append(" <span>(").Append(Html(FormatCompact(highlight.TotalTokens))).AppendLine(")</span></div>");
        }
        sb.AppendLine("        </article>");
    }

    private static void AppendMiniMetricCard(StringBuilder sb, string label, string value) {
        sb.AppendLine("        <article class=\"mini-card\">");
        sb.Append("          <div class=\"mini-label\">").Append(Html(label.ToUpperInvariant())).AppendLine("</div>");
        sb.Append("          <div class=\"mini-value\">").Append(Html(value)).AppendLine("</div>");
        sb.AppendLine("        </article>");
    }

    private static void AppendSpotlightCard(StringBuilder sb, UsageTelemetryOverviewCard card) {
        sb.AppendLine("        <article class=\"mini-card\">");
        sb.Append("          <div class=\"mini-label\">").Append(Html(card.Label.ToUpperInvariant())).AppendLine("</div>");
        sb.Append("          <div class=\"mini-value\">").Append(Html(card.Value)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(card.Subtitle)) {
            sb.Append("          <div class=\"mini-copy\">").Append(Html(card.Subtitle!)).AppendLine("</div>");
        }
        sb.AppendLine("        </article>");
    }

    private static string[] ResolveLegendColors(string providerId) {
        return providerId.Trim().ToLowerInvariant() switch {
            "claude" => new[] { "#e8e8e8", "#f5d8b0", "#f3ba73", "#fb8c1d", "#c65102" },
            "codex" => new[] { "#e8e8e8", "#cfd6ff", "#98a8ff", "#6268f1", "#2f2a93" },
            _ => new[] { "#e8e8e8", "#d6ecd3", "#9be9a8", "#40c463", "#216e39" }
        };
    }

    private static ProviderAccentColors ResolveProviderAccentColors(string providerId) {
        return providerId.Trim().ToLowerInvariant() switch {
            "claude" => new ProviderAccentColors("#f3ba73", "#fb8c1d", "#c65102", "#e9c89e"),
            "codex" => new ProviderAccentColors("#98a8ff", "#6268f1", "#2f2a93", "#bcc5ff"),
            _ => new ProviderAccentColors("#9be9a8", "#40c463", "#216e39", "#cfe8d2")
        };
    }

    private static string FormatRange(DateTime? startDayUtc, DateTime? endDayUtc) {
        if (!startDayUtc.HasValue || !endDayUtc.HasValue) {
            return "n/a";
        }

        return startDayUtc.Value.ToString("yyyy-MM-dd") + " to " + endDayUtc.Value.ToString("yyyy-MM-dd");
    }

    private static string FormatCompact(decimal value) {
        if (value <= 0m) {
            return "0";
        }

        return FormatCompact((double)value);
    }

    private static string FormatCompact(long value) {
        if (value <= 0L) {
            return "0";
        }

        return FormatCompact((double)value);
    }

    private static string FormatCompact(double value) {
        if (value >= 1_000_000_000d) {
            return (value / 1_000_000_000d).ToString(value >= 10_000_000_000d ? "0.#" : "0.##", CultureInfo.InvariantCulture) + "B";
        }
        if (value >= 1_000_000d) {
            return (value / 1_000_000d).ToString(value >= 10_000_000d ? "0.#" : "0.##", CultureInfo.InvariantCulture) + "M";
        }
        if (value >= 1_000d) {
            return (value / 1_000d).ToString(value >= 10_000d ? "0.#" : "0.##", CultureInfo.InvariantCulture) + "K";
        }
        return value.ToString("0", CultureInfo.InvariantCulture);
    }

    private static string FormatCurrencyCompact(decimal value) {
        if (value >= 1000m) {
            return (value / 1000m).ToString(value >= 10000m ? "0.#" : "0.##", CultureInfo.InvariantCulture) + "K";
        }

        return value.ToString(value >= 100m ? "0" : "0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatPercent(long value, long total) {
        if (value <= 0 || total <= 0) {
            return "0%";
        }

        return (Math.Min(1d, value / (double)total) * 100d).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    private static double? ComputeRatio(long value, long total) {
        if (value <= 0 || total <= 0) {
            return 0d;
        }

        return Math.Min(1d, value / (double)total);
    }

    private static string FormatRatioPercent(double? ratio) {
        if (!ratio.HasValue || ratio.Value <= 0d) {
            return "0";
        }

        return (Math.Min(1d, ratio.Value) * 100d).ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string Html(string value) {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }

    private sealed record ProviderAccentColors(string Input, string Output, string Total, string Other);
}
