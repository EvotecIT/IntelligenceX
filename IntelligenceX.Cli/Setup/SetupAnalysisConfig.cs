using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace IntelligenceX.Cli.Setup;

internal static class SetupAnalysisConfig {
    public static JsonObject Build(bool enabled, bool gateEnabled, IReadOnlyList<string> packs) {
        var packIds = NormalizePacks(packs);
        var packsNode = new JsonArray();
        foreach (var pack in packIds) {
            packsNode.Add(pack);
        }

        return new JsonObject {
            ["enabled"] = enabled,
            ["packs"] = packsNode,
            ["configMode"] = "respect",
            ["gate"] = new JsonObject {
                // Default to non-blocking onboarding; teams can enable gating explicitly.
                ["enabled"] = gateEnabled,
                ["minSeverity"] = "warning",
                ["types"] = new JsonArray("vulnerability", "bug"),
                ["failOnUnavailable"] = true,
                ["failOnNoEnabledRules"] = true,
                ["includeOutsidePackRules"] = false,
                ["failOnHotspotsToReview"] = false
            },
            ["results"] = new JsonObject {
                ["inputs"] = new JsonArray("artifacts/**/*.sarif", "artifacts/intelligencex.findings.json"),
                ["minSeverity"] = "warning",
                ["maxInline"] = 20,
                ["summary"] = true,
                ["summaryMaxItems"] = 10,
                ["summaryPlacement"] = "bottom",
                ["showPolicy"] = true
            }
        };
    }

    public static void Apply(JsonObject root,
        bool enabledSet, bool enabled,
        bool gateEnabledSet, bool gateEnabled,
        bool packsSet, IReadOnlyList<string> packs) {
        var analysis = root["analysis"] as JsonObject;
        if (analysis is null) {
            // If the caller is toggling analysis on/off, we need a baseline object to edit.
            // When enabling (or setting packs/gate), initialize a full config; when disabling, a minimal object is fine.
            var inferredEnabled = enabledSet ? enabled : (gateEnabledSet || packsSet);
            analysis = inferredEnabled
                ? Build(enabled: true, gateEnabled: gateEnabled, packs: packs)
                : new JsonObject();
        }

        if (enabledSet) {
            analysis["enabled"] = enabled;
        } else if (analysis["enabled"] is null && (gateEnabledSet || packsSet)) {
            // If the user sets packs/gate without explicitly enabling analysis, infer enabled=true.
            analysis["enabled"] = true;
        }

        if (packsSet) {
            var packIds = NormalizePacks(packs);
            var packsNode = new JsonArray();
            foreach (var pack in packIds) {
                packsNode.Add(pack);
            }
            analysis["packs"] = packsNode;
        }

        if (gateEnabledSet) {
            var gate = analysis["gate"] as JsonObject ?? new JsonObject();
            gate["enabled"] = gateEnabled;
            analysis["gate"] = gate;
        }

        root["analysis"] = analysis;
    }

    public static string[] ReadStringArray(JsonObject obj, string key) {
        if (!obj.TryGetPropertyValue(key, out var value) || value is null) {
            return Array.Empty<string>();
        }
        if (value is not JsonArray arr || arr.Count == 0) {
            return Array.Empty<string>();
        }
        var list = new List<string>();
        foreach (var item in arr) {
            var str = item?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(str)) {
                list.Add(str.Trim());
            }
        }
        return list.ToArray();
    }

    private static IReadOnlyList<string> NormalizePacks(IReadOnlyList<string> packs) {
        if (packs.Count == 0) {
            return new[] { "all-50" };
        }
        var normalized = new List<string>(packs.Count);
        foreach (var pack in packs) {
            if (!string.IsNullOrWhiteSpace(pack)) {
                normalized.Add(pack.Trim());
            }
        }
        return normalized.Count == 0 ? new[] { "all-50" } : normalized;
    }
}

