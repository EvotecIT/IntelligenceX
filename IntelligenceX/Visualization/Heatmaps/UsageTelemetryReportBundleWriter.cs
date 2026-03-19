using System;
using System.IO;
using System.Linq;
using System.Text;
using IntelligenceX.Json;

namespace IntelligenceX.Visualization.Heatmaps;

internal static class UsageTelemetryReportBundleWriter {
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteOverviewBundle(UsageTelemetryOverviewDocument overview, string outputDirectory) {
        if (overview is null) {
            throw new ArgumentNullException(nameof(overview));
        }
        if (string.IsNullOrWhiteSpace(outputDirectory)) {
            throw new ArgumentException("Output directory cannot be null or whitespace.", nameof(outputDirectory));
        }

        var pages = new System.Collections.Generic.List<string>();
        var dataFiles = new System.Collections.Generic.List<string>();
        var lightSvgFiles = new System.Collections.Generic.List<string>();
        var darkSvgFiles = new System.Collections.Generic.List<string>();

        UsageTelemetryReportStaticAssets.WriteBundleAssets(outputDirectory);

        WriteTextFile(outputDirectory, "overview.json", JsonLite.Serialize(JsonValue.From(overview.ToJson())));
        dataFiles.Add("overview.json");
        WriteTextFile(outputDirectory, "index.html", UsageTelemetryOverviewHtmlRenderer.Render(overview));
        pages.Add("index.html");

        foreach (var heatmap in overview.Heatmaps) {
            var fileStem = UsageTelemetryBreakdownFileNames.ResolveFileStem(heatmap.Key, heatmap.Label);
            var lightDocument = CreateThemeVariant(heatmap.Document, darkMode: false);
            var darkDocument = CreateThemeVariant(heatmap.Document, darkMode: true);
            WriteTextFile(outputDirectory, fileStem + ".json", JsonLite.Serialize(JsonValue.From(heatmap.Document.ToJson())));
            dataFiles.Add(fileStem + ".json");
            WriteTextFile(outputDirectory, fileStem + ".svg", HeatmapSvgRenderer.Render(lightDocument));
            WriteTextFile(outputDirectory, fileStem + ".light.svg", HeatmapSvgRenderer.Render(lightDocument));
            lightSvgFiles.Add(fileStem + ".svg");
            lightSvgFiles.Add(fileStem + ".light.svg");
            WriteTextFile(outputDirectory, fileStem + ".dark.svg", HeatmapSvgRenderer.Render(darkDocument));
            darkSvgFiles.Add(fileStem + ".dark.svg");
            WriteTextFile(
                outputDirectory,
                fileStem + ".html",
                UsageTelemetryBreakdownHtmlRenderer.Render(
                    overview.Title,
                    heatmap.Key,
                    heatmap.Label,
                    heatmap.Document.Subtitle,
                    heatmap.Document,
                    overview.Summary,
                    overview.Metadata,
                    overview.ProviderSections.Count));
            pages.Add(fileStem + ".html");
        }

        foreach (var providerSection in overview.ProviderSections) {
            var lightDocument = CreateThemeVariant(providerSection.Heatmap, darkMode: false);
            var darkDocument = CreateThemeVariant(providerSection.Heatmap, darkMode: true);
            WriteTextFile(outputDirectory, providerSection.Key + ".json", JsonLite.Serialize(JsonValue.From(providerSection.Heatmap.ToJson())));
            dataFiles.Add(providerSection.Key + ".json");
            WriteTextFile(outputDirectory, providerSection.Key + ".svg", HeatmapSvgRenderer.Render(lightDocument));
            WriteTextFile(outputDirectory, providerSection.Key + ".light.svg", HeatmapSvgRenderer.Render(lightDocument));
            lightSvgFiles.Add(providerSection.Key + ".svg");
            lightSvgFiles.Add(providerSection.Key + ".light.svg");
            WriteTextFile(outputDirectory, providerSection.Key + ".dark.svg", HeatmapSvgRenderer.Render(darkDocument));
            darkSvgFiles.Add(providerSection.Key + ".dark.svg");
        }

        var githubSection = overview.ProviderSections.FirstOrDefault(section =>
            string.Equals(section.ProviderId, "github", StringComparison.OrdinalIgnoreCase));
        if (githubSection is not null && HasHeatmapActivity(githubSection.Heatmap)) {
            WriteTextFile(outputDirectory, "github-wrapped.html", GitHubWrappedHtmlRenderer.Render(githubSection, overview.Summary, overview.Metadata, overview.ProviderSections.Count));
            WriteTextFile(outputDirectory, "github-wrapped-card.html", GitHubWrappedCardHtmlRenderer.Render(githubSection, overview.Summary, overview.Metadata, overview.ProviderSections.Count));
            pages.Add("github-wrapped.html");
            pages.Add("github-wrapped-card.html");
        }

        var manifest = UsageTelemetryReportBundleManifest.Create(
            UsageTelemetryReportStaticAssets.GetPublishableAssetFileNames(),
            pages,
            dataFiles,
            lightSvgFiles,
            darkSvgFiles);
        WriteTextFile(outputDirectory, "bundle-manifest.json", JsonLite.Serialize(JsonValue.From(manifest.ToJson())));
    }

    private static bool HasHeatmapActivity(HeatmapDocument heatmap) {
        return heatmap.Sections.Any(static section => section.Days.Count > 0);
    }

    private static HeatmapDocument CreateThemeVariant(HeatmapDocument source, bool darkMode) {
        var themedPalette = CreateThemeVariant(source.Palette, darkMode);
        var sections = source.Sections
            .Select(static section => new HeatmapSection(
                section.Title,
                section.Subtitle,
                section.Days
                    .Select(static day => new HeatmapDay(day.Date, day.Value, day.Level, day.FillColor, day.Tooltip, day.Breakdown))
                    .ToArray()))
            .ToArray();
        var legend = source.LegendItems
            .Select(static item => new HeatmapLegendItem(item.Label, item.Color))
            .ToArray();

        return new HeatmapDocument(
            source.Title,
            source.Subtitle,
            themedPalette,
            sections,
            units: source.Units,
            weekStart: source.WeekStart,
            showIntensityLegend: source.ShowIntensityLegend,
            legendLowLabel: source.LegendLowLabel,
            legendHighLabel: source.LegendHighLabel,
            legendItems: legend,
            showDocumentHeader: source.ShowDocumentHeader,
            showSectionHeaders: source.ShowSectionHeaders,
            compactWeekdayLabels: source.CompactWeekdayLabels);
    }

    private static HeatmapPalette CreateThemeVariant(HeatmapPalette source, bool darkMode) {
        if (darkMode) {
            return new HeatmapPalette(
                backgroundColor: "#0f1115",
                panelColor: "#171b22",
                textColor: "#f5f7fa",
                mutedTextColor: "#9aa4b2",
                emptyColor: "#252b34",
                intensityColors: source.IntensityColors.ToArray());
        }

        return new HeatmapPalette(
            backgroundColor: "#f6f8fa",
            panelColor: "#ffffff",
            textColor: "#24292f",
            mutedTextColor: "#57606a",
            emptyColor: "#ebedf0",
            intensityColors: source.IntensityColors.ToArray());
    }

    private static void WriteTextFile(string outputDirectory, string fileName, string content) {
        File.WriteAllText(
            Path.Combine(outputDirectory, fileName),
            content,
            Utf8NoBom);
    }
}
