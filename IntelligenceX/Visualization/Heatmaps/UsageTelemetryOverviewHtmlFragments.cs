using System;
using System.Globalization;
using System.Net;
using System.Text;

namespace IntelligenceX.Visualization.Heatmaps;

internal static class UsageTelemetryOverviewHtmlFragments {
    public static void AppendInsightSection(StringBuilder sb, UsageTelemetryOverviewInsightSection insight) {
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
                    sb.Append("<a class=\"inline-link\" href=\"").Append(Html(row.Href!)).Append("\" target=\"_blank\" rel=\"noopener\">")
                        .Append(Html(row.Label))
                        .Append("</a>");
                } else {
                    sb.Append(Html(row.Label));
                }
                sb.AppendLine("</div>");
                sb.Append("                    <div class=\"rank-value\">").Append(Html(row.Value)).AppendLine("</div>");
                sb.AppendLine("                  </div>");
                if (!string.IsNullOrWhiteSpace(row.Subtitle)) {
                    sb.Append("                  <div class=\"estimate-note rank-note\">").Append(Html(row.Subtitle!)).AppendLine("</div>");
                }
            }
            sb.AppendLine("                </div>");
        }
        sb.AppendLine("              </article>");
    }

    public static string[] ResolveLegendColors(string providerId) {
        return providerId.Trim().ToLowerInvariant() switch {
            "claude" => new[] { "#e8e8e8", "#f5d8b0", "#f3ba73", "#fb8c1d", "#c65102" },
            "codex" => new[] { "#e8e8e8", "#cfd6ff", "#98a8ff", "#6268f1", "#2f2a93" },
            _ => new[] { "#e8e8e8", "#d6ecd3", "#9be9a8", "#40c463", "#216e39" }
        };
    }

    public static string FormatCompact(decimal value) {
        if (value <= 0m) {
            return "0";
        }

        return FormatCompact((double)value);
    }

    public static string FormatCompact(long value) {
        if (value <= 0L) {
            return "0";
        }

        return FormatCompact((double)value);
    }

    public static string FormatCompact(double value) {
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

    public static string FormatCurrencyCompact(decimal value) {
        if (value >= 1000m) {
            return (value / 1000m).ToString(value >= 10000m ? "0.#" : "0.##", CultureInfo.InvariantCulture) + "K";
        }

        return value.ToString(value >= 100m ? "0" : "0.##", CultureInfo.InvariantCulture);
    }

    public static string FormatPercent(long value, long total) {
        if (value <= 0 || total <= 0) {
            return "0%";
        }

        return (Math.Min(1d, value / (double)total) * 100d).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    public static double? ComputeRatio(long value, long total) {
        if (value <= 0 || total <= 0) {
            return 0d;
        }

        return Math.Min(1d, value / (double)total);
    }

    public static string FormatRatioPercent(double? ratio) {
        if (!ratio.HasValue || ratio.Value <= 0d) {
            return "0";
        }

        return (Math.Min(1d, ratio.Value) * 100d).ToString("0.###", CultureInfo.InvariantCulture);
    }

    public static string Html(string value) {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }
}
