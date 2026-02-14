using System;
using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.Analysis;

/// <summary>
/// Reads analysis configuration from a reviewer.json payload.
/// </summary>
public static class AnalysisConfigReader {
    /// <summary>
    /// Applies analysis configuration values from the JSON object.
    /// </summary>
    public static void Apply(JsonObject root, JsonObject? reviewObj, AnalysisSettings settings) {
        if (root is null || settings is null) {
            return;
        }
        var analysis = root.GetObject("analysis") ?? reviewObj?.GetObject("analysis");
        if (analysis is null) {
            return;
        }
        settings.Enabled = ReadBool(analysis, "enabled", settings.Enabled);
        var configMode = analysis.GetString("configMode");
        if (!string.IsNullOrWhiteSpace(configMode)) {
            settings.ConfigMode = AnalysisSettings.ParseConfigMode(configMode, settings.ConfigMode);
        }
        var packs = AnalysisJsonHelpers.ReadStringList(analysis, "packs");
        if (packs is not null) {
            settings.Packs = packs;
        }
        var disabledRules = AnalysisJsonHelpers.ReadStringList(analysis, "disabledRules");
        if (disabledRules is not null) {
            settings.DisabledRules = disabledRules;
        }
        var run = analysis.GetObject("run");
        if (run is not null) {
            settings.Run.Strict = ReadBool(run, "strict", settings.Run.Strict);
        }
        var overrides = AnalysisJsonHelpers.ReadStringMap(analysis, "severityOverrides");
        if (overrides is not null) {
            var normalizedOverrides = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var entry in overrides) {
                normalizedOverrides[entry.Key] = entry.Value;
            }
            settings.SeverityOverrides = normalizedOverrides;
        }

        var hotspots = analysis.GetObject("hotspots");
        if (hotspots is not null) {
            settings.Hotspots.Show = ReadBool(hotspots, "show", settings.Hotspots.Show);
            settings.Hotspots.MaxItems = ReadNonNegativeInt(hotspots, "maxItems", settings.Hotspots.MaxItems);
            settings.Hotspots.StatePath = hotspots.GetString("statePath") ?? settings.Hotspots.StatePath;
            settings.Hotspots.ShowStateSummary =
                ReadBool(hotspots, "showStateSummary", settings.Hotspots.ShowStateSummary);
            settings.Hotspots.AlwaysRender = ReadBool(hotspots, "alwaysRender", settings.Hotspots.AlwaysRender);
        }

        var gate = analysis.GetObject("gate");
        if (gate is not null) {
            settings.Gate.Enabled = ReadBool(gate, "enabled", settings.Gate.Enabled);
            settings.Gate.MinSeverity = gate.GetString("minSeverity") ?? settings.Gate.MinSeverity;
            var types = AnalysisJsonHelpers.ReadStringList(gate, "types");
            if (types is not null) {
                settings.Gate.Types = types;
            }
            settings.Gate.IncludeOutsidePackRules = ReadBool(gate, "includeOutsidePackRules", settings.Gate.IncludeOutsidePackRules);
            settings.Gate.FailOnUnavailable = ReadBool(gate, "failOnUnavailable", settings.Gate.FailOnUnavailable);
            settings.Gate.FailOnNoEnabledRules = ReadBool(gate, "failOnNoEnabledRules", settings.Gate.FailOnNoEnabledRules);
            settings.Gate.FailOnHotspotsToReview = ReadBool(gate, "failOnHotspotsToReview", settings.Gate.FailOnHotspotsToReview);
            settings.Gate.NewIssuesOnly = ReadBool(gate, "newIssuesOnly", settings.Gate.NewIssuesOnly);
            settings.Gate.BaselinePath = gate.GetString("baselinePath") ?? settings.Gate.BaselinePath;

            var duplication = gate.GetObject("duplication");
            if (duplication is not null) {
                settings.Gate.Duplication.Enabled = ReadBool(duplication, "enabled", settings.Gate.Duplication.Enabled);
                settings.Gate.Duplication.MetricsPath =
                    duplication.GetString("metricsPath") ?? settings.Gate.Duplication.MetricsPath;
                var ruleIds = AnalysisJsonHelpers.ReadStringList(duplication, "ruleIds");
                if (ruleIds is not null) {
                    var normalizedRuleIds = NormalizeRuleIds(ruleIds);
                    if (normalizedRuleIds.Count > 0) {
                        settings.Gate.Duplication.RuleIds = normalizedRuleIds;
                    }
                }
                settings.Gate.Duplication.MaxFilePercent = ReadPercentOrDefault(
                    duplication,
                    "maxFilePercent",
                    settings.Gate.Duplication.MaxFilePercent);
                settings.Gate.Duplication.MaxOverallPercent = ReadPercentOrDefault(
                    duplication,
                    "maxOverallPercent",
                    settings.Gate.Duplication.MaxOverallPercent);
                settings.Gate.Duplication.MaxOverallPercentIncrease = ReadPercentOrDefault(
                    duplication,
                    "maxOverallPercentIncrease",
                    settings.Gate.Duplication.MaxOverallPercentIncrease);
                settings.Gate.Duplication.Scope = NormalizeDuplicationScope(
                    duplication.GetString("scope"),
                    settings.Gate.Duplication.Scope);
                settings.Gate.Duplication.NewIssuesOnly =
                    ReadBool(duplication, "newIssuesOnly", settings.Gate.Duplication.NewIssuesOnly);
                settings.Gate.Duplication.FailOnUnavailable = ReadBool(
                    duplication,
                    "failOnUnavailable",
                    settings.Gate.FailOnUnavailable);
            }
        }

        var results = analysis.GetObject("results");
        if (results is null) {
            return;
        }
        var inputs = AnalysisJsonHelpers.ReadStringList(results, "inputs");
        if (inputs is not null) {
            settings.Results.Inputs = inputs;
        }
        settings.Results.MinSeverity = results.GetString("minSeverity") ?? settings.Results.MinSeverity;
        settings.Results.MaxInline = ReadNonNegativeInt(results, "maxInline", settings.Results.MaxInline);
        settings.Results.Summary = ReadBool(results, "summary", settings.Results.Summary);
        settings.Results.SummaryMaxItems =
            ReadNonNegativeInt(results, "summaryMaxItems", settings.Results.SummaryMaxItems);
        var placement = results.GetString("summaryPlacement");
        if (!string.IsNullOrWhiteSpace(placement)) {
            settings.Results.SummaryPlacement = NormalizePlacement(placement, settings.Results.SummaryPlacement);
        }
        settings.Results.ShowPolicy = ReadBool(results, "showPolicy", settings.Results.ShowPolicy);
        settings.Results.PolicyRulePreviewItems = ReadPolicyRulePreviewItems(
            results, "policyRulePreviewItems", settings.Results.PolicyRulePreviewItems);
    }

    private static int ReadNonNegativeInt(JsonObject obj, string key, int fallback) {
        var value = obj.GetInt64(key);
        if (value.HasValue && value.Value >= 0) {
            return (int)value.Value;
        }
        return fallback;
    }

    private static int ReadPolicyRulePreviewItems(JsonObject obj, string key, int fallback) {
        var value = obj.GetInt64(key);
        if (!value.HasValue) {
            return fallback;
        }
        if (value.Value < 0) {
            return 0;
        }
        if (value.Value > AnalysisResultsSettings.MaxPolicyRulePreviewItems) {
            return AnalysisResultsSettings.MaxPolicyRulePreviewItems;
        }
        return (int)value.Value;
    }

    private static double? ReadPercentOrDefault(JsonObject obj, string key, double? fallback) {
        var value = obj.GetDouble(key);
        if (!value.HasValue) {
            return fallback;
        }
        if (value.Value is < 0 or > 100) {
            return fallback;
        }
        return value.Value;
    }

    private static bool ReadBool(JsonObject obj, string key, bool fallback) {
        if (obj.TryGetValue(key, out var value)) {
            return value?.AsBoolean(fallback) ?? fallback;
        }
        return fallback;
    }

    private static string NormalizePlacement(string? value, string fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        var normalized = value!.Trim().ToLowerInvariant();
        return normalized switch {
            "top" or "header" => "top",
            "bottom" or "footer" => "bottom",
            "above" => "above",
            "below" => "below",
            _ => fallback
        };
    }

    private static string NormalizeDuplicationScope(string? value, string fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        var normalized = value!.Trim().ToLowerInvariant();
        return normalized switch {
            "all" => "all",
            "changedfiles" => "changed-files",
            "changed-files" => "changed-files",
            "changed" => "changed-files",
            _ => fallback
        };
    }

    private static IReadOnlyList<string> NormalizeRuleIds(IReadOnlyList<string> ruleIds) {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ruleId in ruleIds ?? Array.Empty<string>()) {
            if (string.IsNullOrWhiteSpace(ruleId)) {
                continue;
            }
            var trimmed = ruleId.Trim();
            if (trimmed.Length == 0) {
                continue;
            }
            if (seen.Add(trimmed)) {
                normalized.Add(trimmed);
            }
        }
        return normalized;
    }
}
