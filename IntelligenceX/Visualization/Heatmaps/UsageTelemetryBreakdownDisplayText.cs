using System;

namespace IntelligenceX.Visualization.Heatmaps;

internal static class UsageTelemetryBreakdownDisplayText {
    public static string FormatSummaryHint(string? breakdownLabel, string? subtitle) {
        var trimmed = subtitle?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) {
            return "Detailed breakdown view.";
        }

        var display = trimmed!;
        var label = breakdownLabel?.Trim();
        if (!string.IsNullOrWhiteSpace(label)) {
            display = StripLeadingLabel(display, label!);
        }

        if (display.StartsWith("by ", StringComparison.OrdinalIgnoreCase)) {
            display = StripLeadingSegment(display);
        }

        return display.Replace(" | ", " · ");
    }

    private static string StripLeadingLabel(string display, string label) {
        var trimmedLabel = label.Trim();
        if (trimmedLabel.Length == 0) {
            return display;
        }

        var candidates = new[] {
            trimmedLabel + " | ",
            trimmedLabel + " · "
        };

        foreach (var candidate in candidates) {
            if (display.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)) {
                return display.Substring(candidate.Length).Trim();
            }
        }

        return display;
    }

    private static string StripLeadingSegment(string display) {
        var separators = new[] { " | ", " · " };
        foreach (var separator in separators) {
            var separatorIndex = display.IndexOf(separator, StringComparison.Ordinal);
            if (separatorIndex > 0 && separatorIndex + separator.Length < display.Length) {
                return display.Substring(separatorIndex + separator.Length).Trim();
            }
        }

        return display;
    }
}
