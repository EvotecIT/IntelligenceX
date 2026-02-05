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
        var packs = ReadStringList(analysis, "packs");
        if (packs is not null) {
            settings.Packs = packs;
        }
        var disabledRules = ReadStringList(analysis, "disabledRules");
        if (disabledRules is not null) {
            settings.DisabledRules = disabledRules;
        }
        var overrides = ReadStringMap(analysis, "severityOverrides");
        if (overrides is not null) {
            settings.SeverityOverrides = overrides;
        }

        var results = analysis.GetObject("results");
        if (results is null) {
            return;
        }
        var inputs = ReadStringList(results, "inputs");
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
    }

    private static IReadOnlyList<string>? ReadStringList(JsonObject obj, string key) {
        if (obj.TryGetValue(key, out var value)) {
            var array = value?.AsArray();
            if (array is not null) {
                var list = new List<string>();
                foreach (var item in array) {
                    var text = item.AsString();
                    if (!string.IsNullOrWhiteSpace(text)) {
                        list.Add(text);
                    }
                }
                return list;
            }
            var textValue = value?.AsString();
            if (!string.IsNullOrWhiteSpace(textValue)) {
                return textValue.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
            }
        }
        return null;
    }

    private static IReadOnlyDictionary<string, string>? ReadStringMap(JsonObject obj, string key) {
        if (!obj.TryGetValue(key, out var value)) {
            return null;
        }
        var mapObj = value?.AsObject();
        if (mapObj is null || mapObj.Count == 0) {
            return null;
        }
        var result = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var entry in mapObj) {
            if (string.IsNullOrWhiteSpace(entry.Key)) {
                continue;
            }
            var text = entry.Value?.AsString();
            if (text is null) {
                continue;
            }
            result[entry.Key] = text;
        }
        return result;
    }

    private static int ReadNonNegativeInt(JsonObject obj, string key, int fallback) {
        var value = obj.GetInt64(key);
        if (value.HasValue && value.Value >= 0) {
            return (int)value.Value;
        }
        return fallback;
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
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "top" or "header" => "top",
            "bottom" or "footer" => "bottom",
            "above" => "above",
            "below" => "below",
            _ => fallback
        };
    }
}
