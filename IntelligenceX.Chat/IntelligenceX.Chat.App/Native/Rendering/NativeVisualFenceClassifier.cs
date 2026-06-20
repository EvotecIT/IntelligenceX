using System;
using System.Collections.Generic;

namespace IntelligenceX.Chat.App.Native.Rendering;

/// <summary>
/// Product-neutral visual fence kinds the native app can route to reusable visual engines.
/// </summary>
internal enum NativeVisualFenceKind {
    Unsupported,
    Mermaid,
    Topology,
    Flow,
    Table,
    Chart,
    Timeline
}

/// <summary>
/// Maps OfficeIMO semantic fenced-block metadata to ChartForgeX visual fence identities.
/// </summary>
internal static class NativeVisualFenceClassifier {
    public static bool TryClassify(
        string? semanticKind,
        string? language,
        string? infoString,
        out NativeVisualFenceKind kind,
        out string fenceName) {
        var semantic = Normalize(semanticKind);
        var lang = Normalize(language);
        var info = Normalize(infoString);

        if (semantic == "mermaid" || lang == "mermaid" || StartsWithToken(info, "mermaid")) {
            kind = NativeVisualFenceKind.Mermaid;
            fenceName = "mermaid";
            return true;
        }

        if (TryClassifyChartForgeX(info, out kind, out fenceName)) {
            return true;
        }

        if (TryClassifyChartForgeX(lang, out kind, out fenceName)) {
            return true;
        }

        if (TryClassifyChartForgeX(semantic, out kind, out fenceName)) {
            return true;
        }

        kind = NativeVisualFenceKind.Unsupported;
        fenceName = string.Empty;
        return false;
    }

    public static IReadOnlyDictionary<string, string> BuildAttributes(
        string? elementId,
        IEnumerable<string>? classes,
        IReadOnlyDictionary<string, string?>? attributes) {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(elementId)) {
            result["id"] = elementId.Trim();
        }

        var classText = classes == null ? string.Empty : string.Join(" ", classes);
        if (!string.IsNullOrWhiteSpace(classText)) {
            result["class"] = classText.Trim();
        }

        if (attributes != null) {
            foreach (var pair in attributes) {
                if (string.IsNullOrWhiteSpace(pair.Key)) {
                    continue;
                }

                result[pair.Key.Trim()] = pair.Value ?? "true";
            }
        }

        return result;
    }

    private static bool TryClassifyChartForgeX(string value, out NativeVisualFenceKind kind, out string fenceName) {
        if (string.IsNullOrEmpty(value)) {
            kind = NativeVisualFenceKind.Unsupported;
            fenceName = string.Empty;
            return false;
        }

        if (MatchesChartForgeX(value, "topology")) {
            kind = NativeVisualFenceKind.Topology;
            fenceName = "chartforgex topology";
            return true;
        }

        if (MatchesChartForgeX(value, "flow")) {
            kind = NativeVisualFenceKind.Flow;
            fenceName = "chartforgex flow";
            return true;
        }

        if (MatchesChartForgeX(value, "table")) {
            kind = NativeVisualFenceKind.Table;
            fenceName = "chartforgex table";
            return true;
        }

        if (MatchesChartForgeX(value, "chart")) {
            kind = NativeVisualFenceKind.Chart;
            fenceName = "chartforgex chart";
            return true;
        }

        if (MatchesChartForgeX(value, "timeline")) {
            kind = NativeVisualFenceKind.Timeline;
            fenceName = "chartforgex timeline";
            return true;
        }

        kind = NativeVisualFenceKind.Unsupported;
        fenceName = string.Empty;
        return false;
    }

    private static bool MatchesChartForgeX(string value, string kind) =>
        StartsWithToken(value, "chartforgex " + kind)
        || StartsWithToken(value, "chartforgex-" + kind)
        || StartsWithToken(value, "cfx " + kind)
        || StartsWithToken(value, "cfx-" + kind);

    private static bool StartsWithToken(string value, string prefix) =>
        string.Equals(value, prefix, StringComparison.Ordinal)
        || value.StartsWith(prefix + " ", StringComparison.Ordinal)
        || value.StartsWith(prefix + "{", StringComparison.Ordinal)
        || value.StartsWith(prefix + "[", StringComparison.Ordinal);

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
}
