using System;
using System.Linq;
using System.Net;
using System.Text;

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

        var sb = new StringBuilder(16 * 1024);
        var heroCard = overview.Cards.FirstOrDefault();
        var secondaryCards = overview.Cards.Skip(1).ToArray();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("  <title>").Append(Html(overview.Title)).AppendLine("</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    :root { color-scheme: dark; --bg:#09111a; --bg-2:#111b28; --panel:#101a28; --panel-2:#172335; --panel-3:#1f2c42; --text:#f3f6fb; --muted:#94a3b8; --accent:#8bd450; --accent-2:#38bdf8; --accent-3:#f59e0b; --border:rgba(148,163,184,.18); --shadow:0 22px 60px rgba(0,0,0,.34); }");
        sb.AppendLine("    * { box-sizing: border-box; }");
        sb.AppendLine("    body { margin:0; font-family: \"Space Grotesk\", \"Aptos\", \"IBM Plex Sans\", \"Segoe UI\", sans-serif; background: radial-gradient(circle at top left, rgba(56,189,248,.20), transparent 26%), radial-gradient(circle at top right, rgba(139,212,80,.18), transparent 28%), linear-gradient(180deg, var(--bg-2), var(--bg)); color:var(--text); }");
        sb.AppendLine("    body::before { content:\"\"; position:fixed; inset:0; background-image: linear-gradient(rgba(255,255,255,.02) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,.02) 1px, transparent 1px); background-size: 28px 28px; opacity:.18; pointer-events:none; }");
        sb.AppendLine("    .page { position:relative; max-width: 1440px; margin: 0 auto; padding: 32px 24px 56px; }");
        sb.AppendLine("    .eyebrow { display:inline-flex; gap:8px; align-items:center; padding:8px 12px; border-radius:999px; border:1px solid rgba(139,212,80,.26); background:rgba(139,212,80,.08); color:#dff6c3; font-size:12px; letter-spacing:.12em; text-transform:uppercase; }");
        sb.AppendLine("    .hero { margin-top:18px; display:grid; grid-template-columns: minmax(0, 1.3fr) minmax(320px, .9fr); gap:18px; }");
        sb.AppendLine("    .hero-panel, .hero-aside, .card, .heatmap, .breakdown { backdrop-filter: blur(10px); }");
        sb.AppendLine("    .hero-panel { padding:28px; border-radius:28px; border:1px solid var(--border); background:linear-gradient(145deg, rgba(56,189,248,.14), rgba(16,26,40,.95) 34%, rgba(139,212,80,.08)); box-shadow:var(--shadow); }");
        sb.AppendLine("    h1 { margin:0; font-size: clamp(34px, 5vw, 58px); line-height:1; letter-spacing:-.04em; }");
        sb.AppendLine("    .subtitle { margin-top:12px; color:var(--muted); font-size:16px; max-width: 72ch; }");
        sb.AppendLine("    .meta { margin-top: 18px; display:flex; flex-wrap:wrap; gap:10px; color: var(--muted); font-size: 12px; text-transform: uppercase; letter-spacing: 0.1em; }");
        sb.AppendLine("    .meta-pill { padding:8px 12px; border-radius:999px; background:rgba(255,255,255,.04); border:1px solid rgba(255,255,255,.07); }");
        sb.AppendLine("    .spotlight { margin-top:28px; display:grid; grid-template-columns:minmax(0,1fr) auto; gap:18px; align-items:end; }");
        sb.AppendLine("    .spotlight-label { color:var(--muted); text-transform:uppercase; letter-spacing:.12em; font-size:12px; }");
        sb.AppendLine("    .spotlight-value { margin-top:10px; font-size: clamp(44px, 7vw, 86px); line-height:.94; font-weight:800; letter-spacing:-.05em; }");
        sb.AppendLine("    .spotlight-subtitle { margin-top:10px; color:var(--muted); font-size:14px; max-width:32ch; }");
        sb.AppendLine("    .spotlight-accent { min-width: 180px; padding:18px; border-radius:22px; background:linear-gradient(180deg, rgba(245,158,11,.18), rgba(15,23,42,.5)); border:1px solid rgba(245,158,11,.20); }");
        sb.AppendLine("    .spotlight-accent strong { display:block; font-size:24px; line-height:1.05; }");
        sb.AppendLine("    .spotlight-accent span { display:block; margin-top:8px; color:var(--muted); font-size:13px; }");
        sb.AppendLine("    .hero-aside { padding:18px; border-radius:28px; border:1px solid var(--border); background:linear-gradient(180deg, rgba(255,255,255,.05), rgba(255,255,255,.02)); box-shadow:var(--shadow); }");
        sb.AppendLine("    .hero-aside-title { font-size:13px; color:var(--muted); text-transform:uppercase; letter-spacing:.12em; }");
        sb.AppendLine("    .cards { margin-top: 14px; display:grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 14px; }");
        sb.AppendLine("    .hero-aside .cards { grid-template-columns: 1fr; }");
        sb.AppendLine("    .card { background: linear-gradient(180deg, rgba(255,255,255,0.05), rgba(255,255,255,0.02)); border:1px solid rgba(255,255,255,.08); border-radius: 22px; padding: 18px; }");
        sb.AppendLine("    .card-label { color: var(--muted); font-size: 12px; text-transform: uppercase; letter-spacing: 0.1em; }");
        sb.AppendLine("    .card-value { margin-top: 10px; font-size: 28px; font-weight: 700; line-height: 1.05; }");
        sb.AppendLine("    .card-subtitle { margin-top: 8px; color: var(--muted); font-size: 13px; }");
        sb.AppendLine("    .section-head { margin: 42px 0 16px; display:flex; justify-content:space-between; gap:16px; align-items:end; }");
        sb.AppendLine("    .section-title { margin:0; font-size: 24px; letter-spacing:-.03em; }");
        sb.AppendLine("    .section-caption { color:var(--muted); font-size:14px; }");
        sb.AppendLine("    .heatmaps { display:grid; grid-template-columns: repeat(auto-fit, minmax(480px, 1fr)); gap: 18px; }");
        sb.AppendLine("    .heatmap { border:1px solid var(--border); border-radius: 24px; overflow:hidden; background:linear-gradient(180deg, rgba(255,255,255,.04), rgba(15,23,42,.7)); box-shadow:var(--shadow); }");
        sb.AppendLine("    .heatmap-header { padding: 18px 20px 10px; border-bottom:1px solid rgba(255,255,255,.05); background:linear-gradient(180deg, rgba(56,189,248,.10), transparent); }");
        sb.AppendLine("    .heatmap-label { font-size: 20px; font-weight: 700; letter-spacing:-.02em; }");
        sb.AppendLine("    .heatmap-caption { margin-top: 6px; color: var(--muted); font-size: 13px; }");
        sb.AppendLine("    .heatmap-frame { padding: 12px; }");
        sb.AppendLine("    .heatmap-frame img { display:block; width:100%; height:auto; border-radius: 18px; background:#0b1220; border:1px solid rgba(255,255,255,.05); }");
        sb.AppendLine("    .breakdowns { margin-top: 8px; display:grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 14px; }");
        sb.AppendLine("    .breakdown { border:1px solid var(--border); border-radius: 22px; padding: 18px; background:linear-gradient(180deg, rgba(255,255,255,.04), rgba(255,255,255,.02)); box-shadow:var(--shadow); }");
        sb.AppendLine("    .breakdown h3 { margin:0 0 14px; font-size: 15px; text-transform:uppercase; letter-spacing:.12em; color:var(--muted); }");
        sb.AppendLine("    .breakdown-list { display:flex; flex-direction:column; gap:12px; }");
        sb.AppendLine("    .breakdown-row { display:grid; gap:6px; }");
        sb.AppendLine("    .breakdown-labels { display:flex; justify-content:space-between; gap:12px; align-items:baseline; }");
        sb.AppendLine("    .breakdown-name { font-weight:700; }");
        sb.AppendLine("    .breakdown-value { color:var(--muted); font-size:13px; }");
        sb.AppendLine("    .breakdown-bar { height:9px; border-radius:999px; background:rgba(255,255,255,.06); overflow:hidden; }");
        sb.AppendLine("    .breakdown-fill { height:100%; border-radius:999px; background:linear-gradient(90deg, var(--accent-2), var(--accent)); min-width:2px; }");
        sb.AppendLine("    .empty-note { color: var(--muted); font-size: 13px; }");
        sb.AppendLine("    .footer-note { margin-top:28px; color:var(--muted); font-size:13px; }");
        sb.AppendLine("    @media (max-width: 980px) { .hero { grid-template-columns:1fr; } .hero-aside .cards { grid-template-columns:repeat(auto-fit,minmax(180px,1fr)); } }");
        sb.AppendLine("    @media (max-width: 720px) { .page { padding: 18px 14px 32px; } .heatmaps { grid-template-columns: 1fr; } .spotlight { grid-template-columns:1fr; } .spotlight-accent { min-width:0; } }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <main class=\"page\">");
        sb.AppendLine("    <div class=\"eyebrow\">Usage telemetry report</div>");
        sb.AppendLine("    <section class=\"hero\">");
        sb.AppendLine("      <article class=\"hero-panel\">");
        sb.Append("        <h1>").Append(Html(overview.Title)).AppendLine("</h1>");
        if (!string.IsNullOrWhiteSpace(overview.Subtitle)) {
            sb.Append("        <div class=\"subtitle\">").Append(Html(overview.Subtitle!)).AppendLine("</div>");
        }
        sb.AppendLine("        <div class=\"meta\">");
        sb.Append("          <div class=\"meta-pill\">Metric: ").Append(Html(overview.Metric.ToString())).AppendLine("</div>");
        sb.Append("          <div class=\"meta-pill\">Units: ").Append(Html(overview.Units)).AppendLine("</div>");
        sb.Append("          <div class=\"meta-pill\">Range: ").Append(Html(FormatDay(overview.Summary.StartDayUtc))).Append(" -> ").Append(Html(FormatDay(overview.Summary.EndDayUtc))).AppendLine("</div>");
        sb.AppendLine("        </div>");
        if (heroCard is not null) {
            sb.AppendLine("        <div class=\"spotlight\">");
            sb.AppendLine("          <div>");
            sb.Append("            <div class=\"spotlight-label\">").Append(Html(heroCard.Label)).AppendLine("</div>");
            sb.Append("            <div class=\"spotlight-value\">").Append(Html(heroCard.Value)).AppendLine("</div>");
            if (!string.IsNullOrWhiteSpace(heroCard.Subtitle)) {
                sb.Append("            <div class=\"spotlight-subtitle\">").Append(Html(heroCard.Subtitle!)).AppendLine("</div>");
            }
            sb.AppendLine("          </div>");
            sb.AppendLine("          <div class=\"spotlight-accent\">");
            sb.Append("            <strong>").Append(Html(overview.Summary.ActiveDays.ToString("0"))).AppendLine(" active day(s)</strong>");
            sb.Append("            <span>Peak: ").Append(Html(FormatDay(overview.Summary.PeakDayUtc))).Append(" / ").Append(Html(overview.Summary.PeakValue.ToString("0.##"))).AppendLine("</span>");
            sb.AppendLine("          </div>");
            sb.AppendLine("        </div>");
        }
        sb.AppendLine("      </article>");
        sb.AppendLine("      <aside class=\"hero-aside\">");
        sb.AppendLine("        <div class=\"hero-aside-title\">Quick stats</div>");
        sb.AppendLine("        <section class=\"cards\">");
        foreach (var card in secondaryCards) {
            AppendCard(sb, card, "          ");
        }
        sb.AppendLine("        </section>");
        sb.AppendLine("      </aside>");
        sb.AppendLine("    </section>");

        sb.AppendLine("    <div class=\"section-head\">");
        sb.AppendLine("      <div>");
        sb.AppendLine("        <h2 class=\"section-title\">Heatmaps</h2>");
        sb.AppendLine("        <div class=\"section-caption\">Daily intensity and dominant breakdown lane from the canonical telemetry ledger.</div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </div>");
        sb.AppendLine("    <section class=\"heatmaps\">");
        foreach (var heatmap in overview.Heatmaps) {
            sb.AppendLine("      <article class=\"heatmap\">");
            sb.AppendLine("        <div class=\"heatmap-header\">");
            sb.Append("          <div class=\"heatmap-label\">").Append(Html(heatmap.Label)).AppendLine("</div>");
            if (!string.IsNullOrWhiteSpace(heatmap.Document.Subtitle)) {
                sb.Append("          <div class=\"heatmap-caption\">").Append(Html(heatmap.Document.Subtitle!)).AppendLine("</div>");
            }
            sb.AppendLine("        </div>");
            sb.AppendLine("        <div class=\"heatmap-frame\">");
            sb.Append("          <img src=\"").Append(Html(heatmap.Key)).Append(".svg\" alt=\"").Append(Html(heatmap.Label)).AppendLine("\">");
            sb.AppendLine("        </div>");
            sb.AppendLine("      </article>");
        }
        sb.AppendLine("    </section>");

        sb.AppendLine("    <div class=\"section-head\">");
        sb.AppendLine("      <div>");
        sb.AppendLine("        <h2 class=\"section-title\">Top Breakdowns</h2>");
        sb.AppendLine("        <div class=\"section-caption\">Where the volume came from across providers, accounts, people, models, and surfaces.</div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </div>");
        sb.AppendLine("    <section class=\"breakdowns\">");
        AppendBreakdown(sb, "Providers", overview.Summary.ProviderBreakdown, overview.Summary.TotalValue);
        AppendBreakdown(sb, "Accounts", overview.Summary.AccountBreakdown, overview.Summary.TotalValue);
        AppendBreakdown(sb, "People", overview.Summary.PersonBreakdown, overview.Summary.TotalValue);
        AppendBreakdown(sb, "Models", overview.Summary.ModelBreakdown, overview.Summary.TotalValue);
        AppendBreakdown(sb, "Surfaces", overview.Summary.SurfaceBreakdown, overview.Summary.TotalValue);
        sb.AppendLine("    </section>");
        sb.AppendLine("    <div class=\"footer-note\">Built from provider-neutral telemetry events so the same report pipeline can power Codex, Claude, IX-native usage, and future compatible providers.</div>");
        sb.AppendLine("  </main>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static void AppendCard(StringBuilder sb, UsageTelemetryOverviewCard card, string indent) {
        sb.AppendLine(indent + "<article class=\"card\">");
        sb.Append(indent).Append("  <div class=\"card-label\">").Append(Html(card.Label)).AppendLine("</div>");
        sb.Append(indent).Append("  <div class=\"card-value\">").Append(Html(card.Value)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(card.Subtitle)) {
            sb.Append(indent).Append("  <div class=\"card-subtitle\">").Append(Html(card.Subtitle!)).AppendLine("</div>");
        }
        sb.AppendLine(indent + "</article>");
    }

    private static void AppendBreakdown(StringBuilder sb, string title, System.Collections.Generic.IReadOnlyList<IntelligenceX.Telemetry.Usage.UsageSummaryBreakdownEntry> entries, decimal totalValue) {
        sb.AppendLine("      <article class=\"breakdown\">");
        sb.Append("        <h3>").Append(Html(title)).AppendLine("</h3>");
        if (entries.Count == 0) {
            sb.AppendLine("        <div class=\"empty-note\">No data</div>");
        } else {
            var total = totalValue <= 0 ? entries.Max(entry => entry.Value) : totalValue;
            sb.AppendLine("        <div class=\"breakdown-list\">");
            foreach (var entry in entries.Take(5)) {
                var share = total <= 0 ? 0 : Math.Max(0, Math.Min(1, entry.Value / total));
                sb.AppendLine("          <div class=\"breakdown-row\">");
                sb.AppendLine("            <div class=\"breakdown-labels\">");
                sb.Append("              <span class=\"breakdown-name\">").Append(Html(entry.Key)).AppendLine("</span>");
                sb.Append("              <span class=\"breakdown-value\">").Append(Html(entry.Value.ToString("0.##"))).Append(" • ").Append(Html((share * 100m).ToString("0.#"))).AppendLine("%</span>");
                sb.AppendLine("            </div>");
                sb.AppendLine("            <div class=\"breakdown-bar\">");
                sb.Append("              <div class=\"breakdown-fill\" style=\"width:").Append((share * 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).AppendLine("%\"></div>");
                sb.AppendLine("            </div>");
                sb.AppendLine("          </div>");
            }
            sb.AppendLine("        </div>");
        }
        sb.AppendLine("      </article>");
    }

    private static string Html(string value) {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }

    private static string FormatDay(DateTime? value) {
        return value.HasValue ? value.Value.ToString("yyyy-MM-dd") : "n/a";
    }
}
