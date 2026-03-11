using System;
using System.Linq;
using IntelligenceX.Json;
using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Visualization.Heatmaps;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestUsageTelemetryOverviewBuilderBuildsCardsAndHeatmaps() {
        var builder = new UsageTelemetryOverviewBuilder();
        var events = new[] {
            new UsageEventRecord("evt-1", "codex", "codex.logs", "src-1", new DateTimeOffset(2026, 03, 10, 9, 0, 0, TimeSpan.Zero)) {
                AccountLabel = "work",
                PersonLabel = "Przemek",
                Surface = "cli",
                Model = "gpt-5-codex",
                TotalTokens = 1200,
                TruthLevel = UsageTruthLevel.Exact
            },
            new UsageEventRecord("evt-2", "claude", "claude.logs", "src-2", new DateTimeOffset(2026, 03, 11, 11, 0, 0, TimeSpan.Zero)) {
                AccountLabel = "lab",
                PersonLabel = "Przemek",
                Surface = "chat",
                Model = "claude-opus",
                TotalTokens = 500,
                TruthLevel = UsageTruthLevel.Exact
            },
            new UsageEventRecord("evt-3", "ix", "ix.client-turn", "src-3", new DateTimeOffset(2026, 03, 11, 15, 0, 0, TimeSpan.Zero)) {
                AccountLabel = "work",
                PersonLabel = "Przemek",
                Surface = "reviewer",
                Model = "gpt-5.4",
                TotalTokens = 300,
                TruthLevel = UsageTruthLevel.Exact
            }
        };

        var overview = builder.Build(
            events,
            new UsageTelemetryOverviewOptions {
                Title = "Combined Usage",
                Subtitle = "person: Przemek"
            });

        AssertEqual("Combined Usage", overview.Title, "usage overview title");
        AssertContainsText(overview.Subtitle ?? string.Empty, "person: Przemek", "usage overview subtitle prefix");
        AssertContainsText(overview.Subtitle ?? string.Empty, "2000 tokens", "usage overview subtitle total");
        AssertEqual("tokens", overview.Units, "usage overview units");
        AssertEqual(true, overview.Cards.Count >= 5, "usage overview card count");
        AssertEqual("2000", overview.Cards[0].Value, "usage overview total card value");
        AssertEqual("2026-03-10", overview.Cards.Single(card => card.Key == "peak_day").Value, "usage overview peak day card");
        AssertEqual(4, overview.Heatmaps.Count, "usage overview heatmap count");
        AssertEqual("surface", overview.Heatmaps[0].Key, "usage overview first heatmap key");
        AssertContainsText(overview.Heatmaps.Single(heatmap => heatmap.Key == "provider").Document.Title, "By provider", "usage overview provider heatmap title");
        AssertContainsText(JsonLite.Serialize(JsonValue.From(overview.ToJson())), "\"key\":\"person\"", "usage overview json person heatmap");
    }
}
