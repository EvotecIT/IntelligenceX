using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;

namespace IntelligenceX.Visualization.Heatmaps;

internal static class UsageTelemetryReportStaticAssets {
    private static readonly Assembly AssetAssembly = typeof(UsageTelemetryReportStaticAssets).Assembly;
    private static readonly Dictionary<string, string> TextAssets = new(StringComparer.OrdinalIgnoreCase) {
        ["overview.html"] = LoadText("IntelligenceX.Visualization.Heatmaps.Assets.overview.html"),
        ["breakdown.html"] = LoadText("IntelligenceX.Visualization.Heatmaps.Assets.breakdown.html"),
        ["github-wrapped.html"] = LoadText("IntelligenceX.Visualization.Heatmaps.Assets.github-wrapped.html"),
        ["github-wrapped-card.html"] = LoadText("IntelligenceX.Visualization.Heatmaps.Assets.github-wrapped-card.html"),
        ["report-shell.css"] = LoadText("IntelligenceX.Visualization.Heatmaps.Assets.report-shell.css"),
        ["report.css"] = LoadText("IntelligenceX.Visualization.Heatmaps.Assets.report.css"),
        ["report-runtime.js"] = LoadText("IntelligenceX.Visualization.Heatmaps.Assets.report-runtime.js"),
        ["report.js"] = LoadText("IntelligenceX.Visualization.Heatmaps.Assets.report.js"),
        ["breakdown.css"] = LoadText("IntelligenceX.Visualization.Heatmaps.Assets.breakdown.css"),
        ["breakdown.js"] = LoadText("IntelligenceX.Visualization.Heatmaps.Assets.breakdown.js"),
        ["github-wrapped-shared.css"] = LoadText("IntelligenceX.Visualization.Heatmaps.Assets.github-wrapped-shared.css"),
        ["github-wrapped.css"] = LoadText("IntelligenceX.Visualization.Heatmaps.Assets.github-wrapped.css"),
        ["github-wrapped.js"] = LoadText("IntelligenceX.Visualization.Heatmaps.Assets.github-wrapped.js"),
        ["github-wrapped-card.css"] = LoadText("IntelligenceX.Visualization.Heatmaps.Assets.github-wrapped-card.css")
    };

    private static readonly string[] PublishableAssets = [
        "report-shell.css",
        "report.css",
        "report-runtime.js",
        "report.js",
        "breakdown.css",
        "breakdown.js",
        "github-wrapped-shared.css",
        "github-wrapped.css",
        "github-wrapped.js",
        "github-wrapped-card.css"
    ];

    public static string RenderOverviewPage(string title, string bodyContent, string bootstrapJson) {
        return RenderPage("overview.html", title, bodyContent, bootstrapJson);
    }

    public static string RenderPage(string templateAssetFileName, string title, string bodyContent, string bootstrapJson) {
        var template = GetText(templateAssetFileName);
        return UsageTelemetryHtmlTemplateBinder.Bind(template, new Dictionary<string, string?>(StringComparer.Ordinal) {
            ["TITLE"] = WebUtility.HtmlEncode(title ?? string.Empty),
            ["CONTENT"] = bodyContent ?? string.Empty,
            ["BOOTSTRAP_JSON"] = EscapeJsonForInlineScript(bootstrapJson)
        });
    }

    public static void WriteBundleAssets(string outputDirectory) {
        Directory.CreateDirectory(outputDirectory);
        foreach (var assetFileName in PublishableAssets) {
            WriteText(outputDirectory, assetFileName);
        }
    }

    public static IReadOnlyList<string> GetPublishableAssetFileNames() {
        return PublishableAssets.ToArray();
    }

    public static string GetText(string assetFileName) {
        if (!TextAssets.TryGetValue(assetFileName, out var content)) {
            throw new InvalidOperationException($"Embedded report asset '{assetFileName}' was not found.");
        }

        return content;
    }

    private static void WriteText(string outputDirectory, string assetFileName) {
        File.WriteAllText(
            Path.Combine(outputDirectory, assetFileName),
            GetText(assetFileName),
            new UTF8Encoding(false));
    }

    private static string LoadText(string resourceName) {
        using var stream = AssetAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string EscapeJsonForInlineScript(string json) {
        return (json ?? "{}")
            .Replace("</", "<\\/")
            .Replace("<", "\\u003c")
            .Replace(">", "\\u003e")
            .Replace("&", "\\u0026");
    }
}
