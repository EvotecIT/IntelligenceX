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
                InputTokens = 900,
                OutputTokens = 300,
                TotalTokens = 1200,
                TruthLevel = UsageTruthLevel.Exact
            },
            new UsageEventRecord("evt-2", "claude", "claude.logs", "src-2", new DateTimeOffset(2026, 03, 11, 11, 0, 0, TimeSpan.Zero)) {
                AccountLabel = "lab",
                PersonLabel = "Przemek",
                Surface = "chat",
                Model = "claude-opus",
                InputTokens = 410,
                OutputTokens = 90,
                TotalTokens = 500,
                TruthLevel = UsageTruthLevel.Exact
            },
            new UsageEventRecord("evt-3", "ix", "ix.client-turn", "src-3", new DateTimeOffset(2026, 03, 11, 15, 0, 0, TimeSpan.Zero)) {
                AccountLabel = "work",
                PersonLabel = "Przemek",
                Surface = "reviewer",
                Model = "gpt-5.4",
                InputTokens = 240,
                OutputTokens = 60,
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
        AssertEqual(3, overview.ProviderSections.Count, "usage overview provider section count");
        AssertEqual("Codex", overview.ProviderSections[0].Title, "usage overview first provider section title");
        AssertEqual(900L, overview.ProviderSections[0].InputTokens, "usage overview codex input tokens");
        AssertEqual(300L, overview.ProviderSections[0].OutputTokens, "usage overview codex output tokens");
        AssertEqual("2025-03-12 -> 2026-03-10", overview.ProviderSections[0].Subtitle, "usage overview codex trailing year subtitle");
        AssertEqual("gpt-5-codex", overview.ProviderSections[0].MostUsedModel?.Model, "usage overview codex most used model");
        AssertEqual(1, overview.ProviderSections[0].LongestStreakDays, "usage overview codex longest streak");
        AssertEqual("provider-codex", overview.ProviderSections[0].Key, "usage overview codex section key");
        AssertEqual(1, overview.ProviderSections[0].Heatmap.Sections.Count, "usage overview codex single heatmap section");
        AssertEqual(new DateTime(2025, 03, 12), overview.ProviderSections[0].Heatmap.Sections[0].Days[0].Date, "usage overview codex heatmap first padded day");
        AssertEqual(new DateTime(2026, 03, 10), overview.ProviderSections[0].Heatmap.Sections[0].Days[overview.ProviderSections[0].Heatmap.Sections[0].Days.Count - 1].Date, "usage overview codex heatmap last padded day");
        AssertEqual("surface", overview.Heatmaps[0].Key, "usage overview first heatmap key");
        AssertContainsText(overview.Heatmaps.Single(heatmap => heatmap.Key == "provider").Document.Title, "By provider", "usage overview provider heatmap title");
        var json = JsonLite.Serialize(JsonValue.From(overview.ToJson()));
        AssertContainsText(json, "\"key\":\"person\"", "usage overview json person heatmap");
        AssertContainsText(json, "\"providerSections\":[", "usage overview json provider sections");
        AssertContainsText(json, "\"title\":\"Codex\"", "usage overview json codex provider section");
    }
}
