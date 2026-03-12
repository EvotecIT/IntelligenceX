using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Shared parsing and display helpers for structured startup bootstrap warnings.
/// </summary>
public static class StartupBootstrapWarningFormatter {
    private static readonly Regex PluginProgressRegex = new(
        @"^\[plugin\]\s+load_progress\s+plugin='(?<plugin>[^']*)'\s+phase='(?<phase>[^']*)'\s+index='(?<index>\d+)'\s+total='(?<total>\d+)'",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PackProgressRegex = new(
        @"^\[startup\]\s+pack_load_progress\s+pack='(?<pack>[^']*)'\s+phase='(?<phase>[^']*)'\s+index='(?<index>\d+)'\s+total='(?<total>\d+)'",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PackRegistrationProgressRegex = new(
        @"^\[startup\]\s+pack_register_progress\s+pack='(?<pack>[^']*)'\s+phase='(?<phase>[^']*)'\s+index='(?<index>\d+)'\s+total='(?<total>\d+)'",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ProviderConnectProgressRegex = new(
        @"^\[startup\]\s+provider_connect_progress\s+phase='(?<phase>[^']*)'\s+operation='(?<operation>[^']*)'(?:\s+transport='(?<transport>[^']*)')?(?:\s+status='(?<status>[^']*)')?",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ProgressSummaryRegex = new(
        @"^\[startup\]\s+plugin load progress:\s+processed\s+(?<processed>\d+)\/(?<total>\d+)\s+plugin folders",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TimingRegex = new(
        @"^\[startup\]\s+tooling bootstrap timings\s+total=(?<total>[^\s]+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CacheHitRegex = new(
        @"^\[startup\]\s+tooling bootstrap cache hit\s+elapsed=(?<elapsed>\d+)ms(?:\s+tools=(?<tools>\d+))?(?:\s+packsLoaded=(?<packs>\d+))?\.$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ElapsedMsRegex = new(
        @"(?:^|\s)elapsed_ms='(?<elapsed>\d+)'",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private const string PackWarningPrefix = "[pack warning]";
    private const string PersistedPreviewRestoredWarning = "[startup] tooling bootstrap preview restored from persisted cache while runtime rebuild continues.";

    /// <summary>
    /// Parses a structured bootstrap warning into user-facing status text.
    /// </summary>
    public static bool TryBuildStatusText(string? rawWarning, out string statusText, out bool allowDuringSend) {
        statusText = string.Empty;
        allowDuringSend = false;

        var normalized = (rawWarning ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (normalized.StartsWith(PackWarningPrefix, StringComparison.OrdinalIgnoreCase)) {
            normalized = normalized.Substring(PackWarningPrefix.Length).Trim();
        }

        var providerConnectProgressMatch = ProviderConnectProgressRegex.Match(normalized);
        if (providerConnectProgressMatch.Success) {
            var phase = providerConnectProgressMatch.Groups["phase"].Value.Trim();
            var status = providerConnectProgressMatch.Groups["status"].Value.Trim();
            var transportLabel = NormalizeEntityLabel(providerConnectProgressMatch.Groups["transport"].Value, "provider");

            if (string.Equals(phase, "begin", StringComparison.OrdinalIgnoreCase)) {
                statusText = $"Starting runtime... connecting runtime provider ({transportLabel})";
                allowDuringSend = true;
                return true;
            }

            if (string.Equals(phase, "end", StringComparison.OrdinalIgnoreCase)) {
                var elapsedLabel = FormatElapsedLabel(normalized);
                statusText = string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
                    ? $"Starting runtime... runtime provider connection failed ({transportLabel}, {elapsedLabel})"
                    : $"Starting runtime... connected runtime provider ({transportLabel}, {elapsedLabel})";
                allowDuringSend = true;
                return true;
            }

            return false;
        }

        var packRegistrationProgressMatch = PackRegistrationProgressRegex.Match(normalized);
        if (packRegistrationProgressMatch.Success) {
            return TryBuildIndexedPhaseStatus(
                packRegistrationProgressMatch,
                subjectFallback: "pack",
                beginTemplate: "Starting runtime... registering tool pack {0}/{1} ({2})",
                endTemplate: "Starting runtime... registered tool pack {0}/{1} ({2}, {3})",
                normalized,
                out statusText,
                out allowDuringSend);
        }

        var packProgressMatch = PackProgressRegex.Match(normalized);
        if (packProgressMatch.Success) {
            return TryBuildIndexedPhaseStatus(
                packProgressMatch,
                subjectFallback: "pack",
                beginTemplate: "Starting runtime... initializing tool packs {0}/{1} ({2})",
                endTemplate: "Starting runtime... initialized tool packs {0}/{1} ({2}, {3})",
                normalized,
                out statusText,
                out allowDuringSend);
        }

        var pluginProgressMatch = PluginProgressRegex.Match(normalized);
        if (pluginProgressMatch.Success) {
            return TryBuildIndexedPhaseStatus(
                pluginProgressMatch,
                subjectFallback: "plugin",
                beginTemplate: "Starting runtime... loading tool packs {0}/{1} ({2})",
                endTemplate: "Starting runtime... loaded tool packs {0}/{1} ({2}, {3})",
                normalized,
                out statusText,
                out allowDuringSend,
                nameGroup: "plugin");
        }

        var progressSummaryMatch = ProgressSummaryRegex.Match(normalized);
        if (progressSummaryMatch.Success) {
            if (!TryReadProgressSummaryValues(progressSummaryMatch, out var processed, out var total)) {
                return false;
            }

            statusText = $"Starting runtime... plugin folder scan {processed}/{total}";
            allowDuringSend = true;
            return true;
        }

        var timingMatch = TimingRegex.Match(normalized);
        if (timingMatch.Success) {
            var total = timingMatch.Groups["total"].Value.Trim();
            if (total.Length == 0) {
                total = "unknown";
            }

            statusText = $"Starting runtime... tool bootstrap finished ({total}), finalizing runtime connection";
            allowDuringSend = true;
            return true;
        }

        var cacheHitMatch = CacheHitRegex.Match(normalized);
        if (cacheHitMatch.Success) {
            var elapsed = cacheHitMatch.Groups["elapsed"].Value.Trim();
            if (elapsed.Length == 0) {
                elapsed = "unknown";
            } else {
                elapsed += "ms";
            }

            statusText = $"Starting runtime... reused tooling bootstrap cache ({elapsed}), finalizing runtime connection";
            allowDuringSend = true;
            return true;
        }

        if (IsPersistedPreviewRestoredWarning(normalized)) {
            statusText = "Starting runtime... restored persisted tool preview while runtime rebuild continues";
            allowDuringSend = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Determines whether a warning matches the persisted-preview bootstrap envelope.
    /// </summary>
    public static bool IsPersistedPreviewRestoredWarning(string? warning) {
        var normalized = (warning ?? string.Empty).Trim();
        return string.Equals(normalized, PersistedPreviewRestoredWarning, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryBuildIndexedPhaseStatus(
        Match match,
        string subjectFallback,
        string beginTemplate,
        string endTemplate,
        string normalizedWarning,
        out string statusText,
        out bool allowDuringSend,
        string nameGroup = "pack") {
        statusText = string.Empty;
        allowDuringSend = false;

        var phase = match.Groups["phase"].Value.Trim();
        var label = NormalizeEntityLabel(match.Groups[nameGroup].Value, subjectFallback);
        if (!TryReadIndexedValues(match, out var index, out var total)) {
            return false;
        }

        if (string.Equals(phase, "begin", StringComparison.OrdinalIgnoreCase)) {
            statusText = string.Format(CultureInfo.InvariantCulture, beginTemplate, index, total, label);
            allowDuringSend = true;
            return true;
        }

        if (string.Equals(phase, "end", StringComparison.OrdinalIgnoreCase)) {
            statusText = string.Format(
                CultureInfo.InvariantCulture,
                endTemplate,
                index,
                total,
                label,
                FormatElapsedLabel(normalizedWarning));
            allowDuringSend = true;
            return true;
        }

        return false;
    }

    private static bool TryReadIndexedValues(Match match, out int index, out int total) {
        index = 0;
        total = 0;
        if (!int.TryParse(match.Groups["index"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out index)) {
            return false;
        }

        if (!int.TryParse(match.Groups["total"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out total)) {
            return false;
        }

        index = Math.Max(1, index);
        total = Math.Max(index, total);
        return true;
    }

    private static bool TryReadProgressSummaryValues(Match match, out int processed, out int total) {
        processed = 0;
        total = 0;
        if (!int.TryParse(match.Groups["processed"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out processed)) {
            return false;
        }

        if (!int.TryParse(match.Groups["total"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out total)) {
            return false;
        }

        processed = Math.Max(0, processed);
        total = Math.Max(processed, total);
        return true;
    }

    private static string NormalizeEntityLabel(string? value, string fallback) {
        var label = (value ?? string.Empty).Trim();
        if (label.Length == 0) {
            label = fallback;
        }

        if (label.Length > 42) {
            label = label.Substring(0, 39) + "...";
        }

        return label;
    }

    private static string FormatElapsedLabel(string normalizedWarning) {
        var elapsedMs = TryReadElapsedMs(normalizedWarning);
        return elapsedMs.HasValue
            ? $"{Math.Max(1, elapsedMs.Value)}ms"
            : "done";
    }

    private static int? TryReadElapsedMs(string normalizedWarning) {
        var elapsedMatch = ElapsedMsRegex.Match(normalizedWarning);
        if (!elapsedMatch.Success) {
            return null;
        }

        if (!int.TryParse(elapsedMatch.Groups["elapsed"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var elapsedMs)) {
            return null;
        }

        return Math.Max(1, elapsedMs);
    }
}
