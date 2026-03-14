using System;
using System.Text;

namespace IntelligenceX.Visualization.Heatmaps;

internal static class UsageTelemetryBreakdownFileNames {
    public static string ResolveFileStem(string? breakdownKey, string? breakdownLabel = null) {
        var labelStem = BuildLabelStem(breakdownLabel);
        if (labelStem.Length > 0) {
            return labelStem;
        }

        var normalized = (breakdownKey ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return "breakdown";
        }

        return normalized.Equals("sourceroot", StringComparison.OrdinalIgnoreCase)
            ? "source-root"
            : normalized;
    }

    private static string BuildLabelStem(string? breakdownLabel) {
        var normalized = (breakdownLabel ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        if (normalized.StartsWith("By ", StringComparison.OrdinalIgnoreCase)) {
            normalized = normalized.Substring(3).Trim();
        }

        if (normalized.Length == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder(normalized.Length);
        var lastWasDash = false;
        foreach (var ch in normalized) {
            if (char.IsLetterOrDigit(ch)) {
                sb.Append(char.ToLowerInvariant(ch));
                lastWasDash = false;
                continue;
            }

            if (sb.Length == 0 || lastWasDash) {
                continue;
            }

            sb.Append('-');
            lastWasDash = true;
        }

        return sb.ToString().Trim('-');
    }
}
