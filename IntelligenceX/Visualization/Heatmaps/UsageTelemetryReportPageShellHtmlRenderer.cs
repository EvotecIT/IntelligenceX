using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace IntelligenceX.Visualization.Heatmaps;

internal static class UsageTelemetryReportPageShellHtmlRenderer {
    public static void AppendOverviewHeader(StringBuilder sb, UsageTelemetryOverviewPageModel page) {
        if (sb is null) {
            throw new ArgumentNullException(nameof(sb));
        }
        if (page is null) {
            throw new ArgumentNullException(nameof(page));
        }

        AppendHero(sb, page.Title, page.Subtitle, null, static (heroBuilder, state) => {
            foreach (var stat in state.HeroStats) {
                AppendHeroStat(heroBuilder, stat.Label, stat.Value);
            }
        }, page);

        AppendToolbar(sb, static (toolbarBuilder, state) => {
            if (state.SectionSwitches.Count > 0) {
                toolbarBuilder.AppendLine("      <div class=\"hero-switcher\" role=\"tablist\" aria-label=\"Report sections\">");
                foreach (var providerSection in state.SectionSwitches) {
                    var isActive = string.Equals(providerSection.Key, "all", StringComparison.OrdinalIgnoreCase);
                    toolbarBuilder.Append("        <button type=\"button\" class=\"hero-switch");
                    if (isActive) {
                        toolbarBuilder.Append(" active");
                    }

                    toolbarBuilder.Append("\" data-provider-target=\"")
                        .Append(Html(providerSection.Key))
                        .Append("\" role=\"tab\" aria-selected=\"")
                        .Append(isActive ? "true" : "false")
                        .Append("\">")
                        .Append(Html(providerSection.Label))
                        .AppendLine("</button>");
                }
                toolbarBuilder.AppendLine("      </div>");
            } else {
                toolbarBuilder.AppendLine("      <div></div>");
            }
        }, page);

        AppendDivider(sb);
    }

    public static void AppendBreakdownHeader(StringBuilder sb, UsageTelemetryBreakdownPageModel page) {
        if (sb is null) {
            throw new ArgumentNullException(nameof(sb));
        }
        if (page is null) {
            throw new ArgumentNullException(nameof(page));
        }

        var heroState = new BreakdownHeroState(page, ResolveBreakdownGuide(page.BreakdownKey));

        AppendToolbar(sb, static (toolbarBuilder, _) => {
            UsageTelemetryReportChromeHtmlFragments.AppendPillLink(toolbarBuilder, "back-link", "index.html", "Back to overview", indentLevel: 3);
        }, state: (object?)null);

        AppendHero(sb, page.BreakdownLabel, page.SummaryHint, static (heroBuilder, state) => {
            if (!string.IsNullOrWhiteSpace(state.Guide)) {
                heroBuilder.Append("        <p class=\"hero-guide\">").Append(Html(state.Guide)).AppendLine("</p>");
            }
        }, static (heroBuilder, state) => {
            heroBuilder.AppendLine("      <div class=\"asset-links\">");
            heroBuilder.Append("        <a class=\"asset-link\" data-light-href=\"").Append(Html(state.Page.FileStem)).Append(".light.svg\" data-dark-href=\"").Append(Html(state.Page.FileStem)).Append(".dark.svg\" href=\"").Append(Html(state.Page.FileStem)).Append(".light.svg\" target=\"_blank\" rel=\"noopener\">Chart SVG</a>").AppendLine();
            UsageTelemetryReportChromeHtmlFragments.AppendPillExternalLink(heroBuilder, "asset-link", state.Page.FileStem + ".json", "Data JSON", indentLevel: 4);
            heroBuilder.AppendLine("      </div>");
            AppendBreakdownHeroChips(heroBuilder, state.Page);
        }, heroState);
    }

    public static void AppendFootnote(StringBuilder sb, string? footnote) {
        if (sb is null) {
            throw new ArgumentNullException(nameof(sb));
        }
        if (string.IsNullOrWhiteSpace(footnote)) {
            return;
        }

        sb.Append("    <div class=\"footnote\">").Append(Html(footnote)).AppendLine("</div>");
    }

    private static void AppendHero<TState>(StringBuilder sb, string title, string? subtitle, Action<StringBuilder, TState>? appendBody, Action<StringBuilder, TState> appendMeta, TState state) {
        sb.AppendLine("    <section class=\"hero\">");
        sb.AppendLine("      <div>");
        sb.Append("        <h1>").Append(Html(title)).AppendLine("</h1>");
        if (!string.IsNullOrWhiteSpace(subtitle)) {
            sb.Append("        <p>").Append(Html(subtitle!)).AppendLine("</p>");
        }
        appendBody?.Invoke(sb, state);
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div class=\"hero-meta\">");
        appendMeta(sb, state);
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
    }

    private static void AppendToolbar<TState>(StringBuilder sb, Action<StringBuilder, TState> appendLeftSide, TState state) {
        sb.AppendLine("    <div class=\"page-toolbar\">");
        appendLeftSide(sb, state);
        UsageTelemetryReportChromeHtmlFragments.AppendThemeSwitcher(sb, indentLevel: 3);
        sb.AppendLine("    </div>");
    }

    private static void AppendDivider(StringBuilder sb) {
        sb.AppendLine("    <div class=\"divider\"></div>");
    }

    private static void AppendBreakdownHeroChips(StringBuilder sb, UsageTelemetryBreakdownPageModel page) {
        if (!page.Summary.IsSourceRoot || page.Summary.SecondaryRows.Count == 0) {
            return;
        }

        var chips = page.Summary.SecondaryRows
            .Select(static row => row.Label)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(label => UsageTelemetrySourceFamilyBadges.TryResolve(label, out var tone, out var text)
                ? new SourceFamilyChip(tone, text)
                : null)
            .Where(static chip => chip is not null)
            .Cast<SourceFamilyChip>()
            .ToArray();

        if (chips.Length == 0) {
            return;
        }

        sb.AppendLine("      <div class=\"hero-chip-group\" aria-label=\"Source families in this breakdown\">");
        foreach (var chip in chips) {
            sb.Append("        <span class=\"summary-row-badge hero-chip ")
                .Append(Html(chip.Tone))
                .Append("\">")
                .Append(Html(chip.Text))
                .AppendLine("</span>");
        }
        sb.AppendLine("      </div>");
    }

    private static void AppendHeroStat(StringBuilder sb, string label, string value) {
        sb.AppendLine("        <div class=\"hero-stat\">");
        sb.Append("          <div class=\"hero-label\">").Append(Html(label.ToUpperInvariant())).AppendLine("</div>");
        sb.Append("          <div class=\"hero-value\">").Append(Html(value)).AppendLine("</div>");
        sb.AppendLine("        </div>");
    }

    private sealed record BreakdownHeroState(UsageTelemetryBreakdownPageModel Page, string? Guide);
    private sealed record SourceFamilyChip(string Tone, string Text);

    private static string? ResolveBreakdownGuide(string? breakdownKey) {
        var normalized = (breakdownKey ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) {
            return null;
        }

        return normalized switch {
            "surface" => "Compare where work happened across CLI sessions, chat flows, reviewer runs, and other recorded surfaces.",
            "provider" => "Compare usage across telemetry sources such as Codex, Claude, LM Studio, and future compatible providers.",
            "model" => "Spot which models dominated the selected window across all imported providers.",
            "sourceroot" => "Trace activity back to current machines, recovered Windows.old profiles, WSL homes, and imported source roots.",
            "account" => "Compare normalized provider accounts after account bindings and alias cleanup are applied.",
            "person" => "Roll up multiple accounts into shared people labels for cross-account reporting.",
            _ => null
        };
    }

    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
