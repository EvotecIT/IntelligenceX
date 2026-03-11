using System;
using System.Linq;
using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Visualization.Heatmaps;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestUsageHeatmapDocumentBuilderBuildsSurfaceLegendAndTooltip() {
        var builder = new UsageHeatmapDocumentBuilder();
        var document = builder.Build(
            new[] {
                new UsageDailyAggregateRecord(new DateTime(2026, 03, 10)) {
                    ProviderId = "codex",
                    Surface = "cli",
                    TotalTokens = 120,
                    EventCount = 2,
                    TruthLevel = UsageTruthLevel.Exact
                },
                new UsageDailyAggregateRecord(new DateTime(2026, 03, 10)) {
                    ProviderId = "codex",
                    Surface = "github_code_review",
                    TotalTokens = 40,
                    EventCount = 1,
                    TruthLevel = UsageTruthLevel.Exact
                },
                new UsageDailyAggregateRecord(new DateTime(2026, 03, 11)) {
                    ProviderId = "claude",
                    Surface = "cli",
                    TotalTokens = 55,
                    EventCount = 1,
                    TruthLevel = UsageTruthLevel.Inferred
                }
            },
            new UsageHeatmapDocumentOptions {
                Title = "Telemetry usage",
                Subtitle = "Preview",
                Metric = UsageHeatmapMetric.TotalTokens,
                BreakdownDimension = UsageHeatmapBreakdownDimension.Surface,
                Units = "tokens",
                LegendEntries = new[] {
                    new UsageHeatmapLegendEntry("cli", "CLI", "#f25ca7"),
                    new UsageHeatmapLegendEntry("github_code_review", "GitHub Code Review", "#8ccf1f")
                }
            });

        AssertEqual("Telemetry usage", document.Title, "usage heatmap title");
        AssertEqual("Preview", document.Subtitle, "usage heatmap subtitle");
        AssertEqual(1, document.Sections.Count, "usage heatmap section count");
        AssertEqual("2026", document.Sections[0].Title, "usage heatmap section title");
        AssertContainsText(document.Sections[0].Subtitle ?? string.Empty, "peak 160 tokens", "usage heatmap section subtitle");
        AssertEqual(2, document.Sections[0].Days.Count, "usage heatmap day count");

        var firstDay = document.Sections[0].Days[0];
        AssertEqual(new DateTime(2026, 03, 10), firstDay.Date, "usage heatmap first date");
        AssertEqual(160d, firstDay.Value, "usage heatmap first value");
        AssertContainsText(firstDay.Tooltip ?? string.Empty, "Total: 160 tokens", "usage heatmap tooltip total");
        AssertContainsText(firstDay.Tooltip ?? string.Empty, "CLI: 120", "usage heatmap tooltip breakdown");
        AssertEqual(true, document.LegendItems.Any(item => item.Label == "CLI"), "usage heatmap legend cli");
        AssertEqual(true, document.LegendItems.Any(item => item.Label == "GitHub Code Review"), "usage heatmap legend code review");
        AssertEqual(true, !string.IsNullOrWhiteSpace(firstDay.FillColor) && !string.Equals(firstDay.FillColor, document.Palette.EmptyColor, StringComparison.OrdinalIgnoreCase), "usage heatmap fill color");
    }

    private static void TestUsageHeatmapDocumentBuilderSupportsCostMetricAndYearSections() {
        var builder = new UsageHeatmapDocumentBuilder();
        var document = builder.Build(
            new[] {
                new UsageDailyAggregateRecord(new DateTime(2025, 12, 31)) {
                    ProviderId = "codex",
                    AccountLabel = "work",
                    TotalCostUsd = 1.23m,
                    TruthLevel = UsageTruthLevel.Exact
                },
                new UsageDailyAggregateRecord(new DateTime(2026, 01, 01)) {
                    ProviderId = "claude",
                    AccountLabel = "personal",
                    TotalCostUsd = 2.50m,
                    TruthLevel = UsageTruthLevel.Inferred
                }
            },
            new UsageHeatmapDocumentOptions {
                Title = "Cost heatmap",
                Metric = UsageHeatmapMetric.TotalCostUsd,
                BreakdownDimension = UsageHeatmapBreakdownDimension.Provider,
                Units = "USD",
                LegendEntries = new[] {
                    new UsageHeatmapLegendEntry("codex", "Codex", "#38bdf8"),
                    new UsageHeatmapLegendEntry("claude", "Claude", "#f59e0b")
                }
            });

        AssertEqual(2, document.Sections.Count, "cost heatmap year sections");
        AssertEqual("2026", document.Sections[0].Title, "cost heatmap latest section first");
        AssertEqual("2025", document.Sections[1].Title, "cost heatmap earlier section second");

        var day2026 = document.Sections[0].Days.Single();
        AssertContainsText(day2026.Tooltip ?? string.Empty, "Total: 2.50 USD", "cost heatmap tooltip total");
        AssertContainsText(day2026.Tooltip ?? string.Empty, "Claude: 2.50", "cost heatmap tooltip provider breakdown");
        AssertEqual(true, document.LegendItems.Any(item => item.Label == "Codex"), "cost heatmap legend codex");
        AssertEqual(true, document.LegendItems.Any(item => item.Label == "Claude"), "cost heatmap legend claude");
    }
}
