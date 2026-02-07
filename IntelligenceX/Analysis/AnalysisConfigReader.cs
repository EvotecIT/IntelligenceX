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
        var overrides = AnalysisJsonHelpers.ReadStringMap(analysis, "severityOverrides");
        if (overrides is not null) {
            var normalizedOverrides = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var entry in overrides) {
                normalizedOverrides[entry.Key] = entry.Value;
            }
            settings.SeverityOverrides = normalizedOverrides;
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
}
