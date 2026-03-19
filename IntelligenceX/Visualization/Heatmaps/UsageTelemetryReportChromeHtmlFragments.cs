using System.Text;

namespace IntelligenceX.Visualization.Heatmaps;

internal static class UsageTelemetryReportChromeHtmlFragments {
    public static void AppendThemeSwitcher(StringBuilder sb, int indentLevel = 0) {
        var indent = new string(' ', indentLevel * 2);
        sb.Append(indent).AppendLine("<div class=\"appearance-switchers\">");
        sb.Append(indent).AppendLine("  <div class=\"theme-switcher\" role=\"tablist\" aria-label=\"Theme selector\">");
        sb.Append(indent).AppendLine("    <button type=\"button\" class=\"theme-switch\" data-theme-target=\"light\" role=\"tab\" aria-selected=\"false\" title=\"Light theme\" aria-label=\"Light theme\"><span class=\"theme-icon\">☀</span></button>");
        sb.Append(indent).AppendLine("    <button type=\"button\" class=\"theme-switch active\" data-theme-target=\"system\" role=\"tab\" aria-selected=\"true\" title=\"System theme\" aria-label=\"System theme\"><span class=\"theme-icon\">◐</span></button>");
        sb.Append(indent).AppendLine("    <button type=\"button\" class=\"theme-switch\" data-theme-target=\"dark\" role=\"tab\" aria-selected=\"false\" title=\"Dark theme\" aria-label=\"Dark theme\"><span class=\"theme-icon\">☾</span></button>");
        sb.Append(indent).AppendLine("  </div>");
        sb.Append(indent).AppendLine("  <div class=\"accent-switcher\" role=\"tablist\" aria-label=\"Accent selector\">");
        sb.Append(indent).AppendLine("    <button type=\"button\" class=\"accent-switch active\" data-accent-target=\"violet\" role=\"tab\" aria-selected=\"true\" title=\"Violet accent\" aria-label=\"Violet accent\"><span class=\"accent-dot accent-dot-violet\"></span></button>");
        sb.Append(indent).AppendLine("    <button type=\"button\" class=\"accent-switch\" data-accent-target=\"ocean\" role=\"tab\" aria-selected=\"false\" title=\"Ocean accent\" aria-label=\"Ocean accent\"><span class=\"accent-dot accent-dot-ocean\"></span></button>");
        sb.Append(indent).AppendLine("    <button type=\"button\" class=\"accent-switch\" data-accent-target=\"forest\" role=\"tab\" aria-selected=\"false\" title=\"Forest accent\" aria-label=\"Forest accent\"><span class=\"accent-dot accent-dot-forest\"></span></button>");
        sb.Append(indent).AppendLine("    <button type=\"button\" class=\"accent-switch\" data-accent-target=\"sunset\" role=\"tab\" aria-selected=\"false\" title=\"Sunset accent\" aria-label=\"Sunset accent\"><span class=\"accent-dot accent-dot-sunset\"></span></button>");
        sb.Append(indent).AppendLine("  </div>");
        sb.Append(indent).AppendLine("</div>");
    }

    public static void AppendPillLink(StringBuilder sb, string cssClass, string href, string label, int indentLevel = 0) {
        var indent = new string(' ', indentLevel * 2);
        sb.Append(indent)
            .Append("<a class=\"")
            .Append(UsageTelemetryOverviewHtmlFragments.Html(cssClass))
            .Append("\" href=\"")
            .Append(UsageTelemetryOverviewHtmlFragments.Html(href))
            .Append("\">")
            .Append(UsageTelemetryOverviewHtmlFragments.Html(label))
            .AppendLine("</a>");
    }

    public static void AppendPillExternalLink(StringBuilder sb, string cssClass, string href, string label, int indentLevel = 0) {
        var indent = new string(' ', indentLevel * 2);
        sb.Append(indent)
            .Append("<a class=\"")
            .Append(UsageTelemetryOverviewHtmlFragments.Html(cssClass))
            .Append("\" href=\"")
            .Append(UsageTelemetryOverviewHtmlFragments.Html(href))
            .Append("\" target=\"_blank\" rel=\"noopener\">")
            .Append(UsageTelemetryOverviewHtmlFragments.Html(label))
            .AppendLine("</a>");
    }
}
