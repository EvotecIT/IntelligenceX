using System;

namespace IntelligenceX.Visualization.Heatmaps;

internal static class UsageTelemetrySourceFamilyBadges {
    public static bool TryResolve(string? label, out string badgeTone, out string badgeText) {
        badgeTone = "tone-imported";
        badgeText = "Imported";

        if (string.IsNullOrWhiteSpace(label)) {
            return false;
        }

        var normalizedLabel = label!;

        if (normalizedLabel.IndexOf("internal", StringComparison.OrdinalIgnoreCase) >= 0) {
            badgeTone = "tone-internal";
            badgeText = "Internal";
            return true;
        }

        if (normalizedLabel.IndexOf("windows.old", StringComparison.OrdinalIgnoreCase) >= 0) {
            badgeTone = "tone-recovered";
            badgeText = "Windows.old";
            return true;
        }

        if (normalizedLabel.IndexOf("current", StringComparison.OrdinalIgnoreCase) >= 0) {
            badgeTone = "tone-current";
            badgeText = "Current";
            return true;
        }

        if (normalizedLabel.IndexOf("wsl", StringComparison.OrdinalIgnoreCase) >= 0) {
            badgeTone = "tone-wsl";
            badgeText = "WSL";
            return true;
        }

        if (normalizedLabel.IndexOf("mac", StringComparison.OrdinalIgnoreCase) >= 0) {
            badgeTone = "tone-macos";
            badgeText = "macOS";
            return true;
        }

        if (normalizedLabel.IndexOf("import", StringComparison.OrdinalIgnoreCase) >= 0 ||
            normalizedLabel.IndexOf("other", StringComparison.OrdinalIgnoreCase) >= 0) {
            badgeTone = "tone-imported";
            badgeText = "Imported";
            return true;
        }

        return false;
    }
}
