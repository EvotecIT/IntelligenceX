using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Json;

namespace IntelligenceX.Visualization.Heatmaps;

internal sealed record UsageTelemetryReportBundleManifest(
    IReadOnlyList<string> Assets,
    IReadOnlyList<string> Pages,
    IReadOnlyList<string> DataFiles,
    IReadOnlyList<string> LightSvgFiles,
    IReadOnlyList<string> DarkSvgFiles) {

    public JsonObject ToJson() {
        return new JsonObject {
            ["assets"] = JsonValue.From(ToJsonArray(Assets)),
            ["pages"] = JsonValue.From(ToJsonArray(Pages)),
            ["dataFiles"] = JsonValue.From(ToJsonArray(DataFiles)),
            ["lightSvgFiles"] = JsonValue.From(ToJsonArray(LightSvgFiles)),
            ["darkSvgFiles"] = JsonValue.From(ToJsonArray(DarkSvgFiles))
        };
    }

    public static UsageTelemetryReportBundleManifest Create(
        IEnumerable<string> assets,
        IEnumerable<string> pages,
        IEnumerable<string> dataFiles,
        IEnumerable<string> lightSvgFiles,
        IEnumerable<string> darkSvgFiles) {
        return new UsageTelemetryReportBundleManifest(
            Normalize(assets),
            Normalize(pages),
            Normalize(dataFiles),
            Normalize(lightSvgFiles),
            Normalize(darkSvgFiles));
    }

    private static IReadOnlyList<string> Normalize(IEnumerable<string> values) {
        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values) {
        var array = new JsonArray();
        foreach (var value in values) {
            array.Add(JsonValue.From(value));
        }

        return array;
    }
}
