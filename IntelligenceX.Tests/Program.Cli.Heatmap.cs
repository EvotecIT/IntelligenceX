using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IntelligenceX.Visualization.Heatmaps;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestHeatmapHelpRoutes() {
        var (exit, stdout, stderr) = RunCliDispatchWithCapturedOutput(
            new[] { "heatmap", "--help" },
            () => false,
            _ => Task.FromResult(0));

        AssertEqual(0, exit, "heatmap help exit");
        AssertContainsText(stdout, "intelligencex heatmap chatgpt", "heatmap help chatgpt usage");
        AssertContainsText(stdout, "intelligencex heatmap github", "heatmap help github usage");
        AssertContainsText(stdout, "intelligencex heatmap usage", "heatmap help telemetry usage");
        AssertContainsText(stdout, "--person <value>", "heatmap help person filter");
        AssertEqual(string.Empty, stderr, "heatmap help stderr");
    }

    private static void TestHeatmapUsageJsonRoutesTelemetryFromSqlite() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-heatmap-usage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var dbPath = Path.Combine(temp, "usage.db");

        try {
            using (var eventStore = new SqliteUsageEventStore(dbPath)) {
                eventStore.Upsert(new UsageEventRecord(
                    eventId: "ev_ix_1",
                    providerId: "ix",
                    adapterId: "ix.client-turn",
                    sourceRootId: "src_ix",
                    timestampUtc: new DateTimeOffset(2026, 03, 10, 12, 0, 0, TimeSpan.Zero)) {
                    AccountLabel = "work",
                    PersonLabel = "Przemek",
                    Surface = "reviewer",
                    Model = "gpt-5.4",
                    TotalTokens = 1200,
                    InputTokens = 900,
                    OutputTokens = 300,
                    TruthLevel = UsageTruthLevel.Exact
                });
                eventStore.Upsert(new UsageEventRecord(
                    eventId: "ev_ix_2",
                    providerId: "ix",
                    adapterId: "ix.client-turn",
                    sourceRootId: "src_ix",
                    timestampUtc: new DateTimeOffset(2026, 03, 11, 12, 0, 0, TimeSpan.Zero)) {
                    AccountLabel = "work",
                    PersonLabel = "Przemek",
                    Surface = "chat",
                    Model = "gpt-5.4",
                    TotalTokens = 800,
                    InputTokens = 500,
                    OutputTokens = 300,
                    TruthLevel = UsageTruthLevel.Exact
                });
            }

            var (exit, stdout, stderr) = RunCliDispatchWithCapturedOutput(
                new[] { "heatmap", "usage", "--db", dbPath, "--provider", "ix", "--person", "Przemek", "--breakdown", "person", "--json" },
                () => false,
                _ => Task.FromResult(0));

            AssertEqual(0, exit, "heatmap usage json exit");
            AssertContainsText(stdout, "\"title\":\"ix usage\"", "heatmap usage json title");
            AssertContainsText(stdout, "\"subtitle\":\"provider: ix | person: Przemek | 2000 tokens | 2 active day(s) | peak 2026-03-10 (1200)\"", "heatmap usage json subtitle");
            AssertContainsText(stdout, "\"label\":\"Przemek\"", "heatmap usage json person legend");
            AssertEqual(string.Empty, stderr, "heatmap usage json stderr");
        } finally {
            try {
                if (Directory.Exists(temp)) {
                    Directory.Delete(temp, recursive: true);
                }
            } catch {
                // Best-effort cleanup only.
            }
        }
    }
#endif

    private static void TestHeatmapSvgRendererEmitsLegendAndTooltip() {
        var document = new HeatmapDocument(
            title: "Usage Heatmap",
            subtitle: "Preview",
            palette: HeatmapPalette.ChatGptDark(),
            sections: new[] {
                new HeatmapSection(
                    "2026",
                    "2 active day(s)",
                    new[] {
                        new HeatmapDay(
                            new DateTime(2026, 3, 10),
                            12.5,
                            level: 4,
                            fillColor: "#00AAFF",
                            tooltip: "2026-03-10\nCLI: 12.5"),
                        new HeatmapDay(
                            new DateTime(2026, 3, 11),
                            4.2,
                            level: 2,
                            fillColor: "#22CC88",
                            tooltip: "2026-03-11\nGitHub Code Review: 4.2")
                    })
            },
            showIntensityLegend: true,
            legendLowLabel: "Lower",
            legendHighLabel: "Higher",
            legendItems: new[] {
                new HeatmapLegendItem("CLI", "#00AAFF"),
                new HeatmapLegendItem("Code Review", "#22CC88")
            });

        var svg = HeatmapSvgRenderer.Render(document);
        AssertContainsText(svg, "<svg", "heatmap svg root");
        AssertContainsText(svg, "Usage Heatmap", "heatmap svg title");
        AssertContainsText(svg, "2026-03-10", "heatmap svg tooltip");
        AssertContainsText(svg, "Higher", "heatmap svg legend high");
        AssertContainsText(svg, "Code Review", "heatmap svg custom legend");
    }

    private static void TestUsageTelemetryHeatmapDocumentBuilderBuildsTelemetryDocument() {
        var builder = new UsageTelemetryHeatmapDocumentBuilder();
        var events = new[] {
            new UsageEventRecord("evt-1", "codex", "codex.logs", "src-1", new DateTimeOffset(2026, 03, 10, 9, 0, 0, TimeSpan.Zero)) {
                Surface = "cli",
                TotalTokens = 300,
                TruthLevel = UsageTruthLevel.Exact
            },
            new UsageEventRecord("evt-2", "codex", "codex.logs", "src-1", new DateTimeOffset(2026, 03, 11, 9, 0, 0, TimeSpan.Zero)) {
                Surface = "cli",
                TotalTokens = 150,
                TruthLevel = UsageTruthLevel.Exact
            },
            new UsageEventRecord("evt-3", "codex", "codex.logs", "src-1", new DateTimeOffset(2026, 03, 11, 12, 0, 0, TimeSpan.Zero)) {
                Surface = "reviewer",
                TotalTokens = 250,
                TruthLevel = UsageTruthLevel.Exact
            }
        };

        var document = builder.Build(
            events,
            new UsageTelemetryHeatmapOptions {
                Title = "Codex Preview",
                Breakdown = UsageHeatmapBreakdownDimension.Surface,
                Metric = UsageSummaryMetric.TotalTokens,
                Subtitle = "provider: codex"
            });

        AssertEqual("Codex Preview", document.Title, "telemetry heatmap title");
        AssertContainsText(document.Subtitle ?? string.Empty, "provider: codex", "telemetry heatmap subtitle prefix");
        AssertContainsText(document.Subtitle ?? string.Empty, "700 tokens", "telemetry heatmap subtitle total");
        AssertEqual("tokens", document.Units, "telemetry heatmap units");
        AssertEqual(1, document.Sections.Count, "telemetry heatmap section count");
        AssertEqual(2, document.Sections[0].Days.Count, "telemetry heatmap day count");
        AssertEqual(400d, document.Sections[0].Days.Single(day => day.Date == new DateTime(2026, 03, 11)).Value, "telemetry heatmap merged day total");
        AssertEqual(2, document.LegendItems.Count, "telemetry heatmap legend count");
        AssertEqual("CLI", document.LegendItems[0].Label, "telemetry heatmap legend first");
        AssertEqual("Reviewer", document.LegendItems[1].Label, "telemetry heatmap legend second");
    }
}
