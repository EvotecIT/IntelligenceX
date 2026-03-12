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
        AssertEqual(6, overview.Heatmaps.Count, "usage overview heatmap count");
        AssertEqual(3, overview.ProviderSections.Count, "usage overview provider section count");
        AssertEqual("Codex", overview.ProviderSections[0].Title, "usage overview first provider section title");
        AssertEqual(900L, overview.ProviderSections[0].InputTokens, "usage overview codex input tokens");
        AssertEqual(300L, overview.ProviderSections[0].OutputTokens, "usage overview codex output tokens");
        AssertEqual("2025-03-12 -> 2026-03-10", overview.ProviderSections[0].Subtitle, "usage overview codex trailing year subtitle");
        AssertEqual("gpt-5-codex", overview.ProviderSections[0].MostUsedModel?.Model, "usage overview codex most used model");
        AssertEqual(1, overview.ProviderSections[0].LongestStreakDays, "usage overview codex longest streak");
        AssertEqual("provider-codex", overview.ProviderSections[0].Key, "usage overview codex section key");
        AssertEqual(1, overview.ProviderSections[0].Heatmap.Sections.Count, "usage overview codex single heatmap section");
        AssertEqual(13, overview.ProviderSections[0].MonthlyUsage.Count, "usage overview codex monthly usage count");
        AssertEqual("2025-03", overview.ProviderSections[0].MonthlyUsage[0].Key, "usage overview codex monthly usage first key");
        AssertEqual(1200L, overview.ProviderSections[0].MonthlyUsage[12].TotalValue, "usage overview codex monthly usage last total");
        AssertEqual(1, overview.ProviderSections[0].TopModels.Count, "usage overview codex top model count");
        AssertEqual("gpt-5-codex", overview.ProviderSections[0].TopModels[0].Model, "usage overview codex top model");
        AssertEqual(true, overview.ProviderSections[0].ApiCostEstimate is not null, "usage overview codex api estimate exists");
        AssertEqual(0.004125m, overview.ProviderSections[0].ApiCostEstimate?.TotalEstimatedCostUsd ?? 0m, "usage overview codex api estimate total");
        AssertEqual(new DateTime(2025, 03, 12), overview.ProviderSections[0].Heatmap.Sections[0].Days[0].Date, "usage overview codex heatmap first padded day");
        AssertEqual(new DateTime(2026, 03, 10), overview.ProviderSections[0].Heatmap.Sections[0].Days[overview.ProviderSections[0].Heatmap.Sections[0].Days.Count - 1].Date, "usage overview codex heatmap last padded day");
        AssertEqual("surface", overview.Heatmaps[0].Key, "usage overview first heatmap key");
        AssertContainsText(overview.Heatmaps.Single(heatmap => heatmap.Key == "provider").Document.Title, "By telemetry source", "usage overview provider heatmap title");
        var json = JsonLite.Serialize(JsonValue.From(overview.ToJson()));
        AssertContainsText(json, "\"key\":\"person\"", "usage overview json person heatmap");
        AssertContainsText(json, "\"providerSections\":[", "usage overview json provider sections");
        AssertContainsText(json, "\"title\":\"Codex\"", "usage overview json codex provider section");
        AssertContainsText(json, "\"monthlyUsage\":[", "usage overview json provider monthly usage");
        AssertContainsText(json, "\"topModels\":[", "usage overview json provider top models");
        AssertContainsText(json, "\"apiCostEstimate\":", "usage overview json provider api estimate");
    }

    private static void TestGitHubWrappedHtmlRendererBuildsShareablePage() {
        var section = CreateSampleGitHubOverviewSection();

        var html = GitHubWrappedHtmlRenderer.Render(section);
        AssertContainsText(html, "GitHub Wrapped", "github wrapped title");
        AssertContainsText(html, "provider-github.dark.svg", "github wrapped heatmap image");
        AssertContainsText(html, "11.8K contributions", "github wrapped current year value");
        AssertContainsText(html, "567 contributions", "github wrapped previous year value");
        AssertContainsText(html, "EvotecIT/GPOZaurr", "github wrapped top repo");
        AssertContainsText(html, "badge rising", "github wrapped rising badge");
        AssertContainsText(html, "github-wrapped-card.html", "github wrapped share card link");
        AssertContainsText(html, "data-owner-panel=\"github-owner-evotecit\"", "github wrapped owner chip");
        AssertContainsText(html, "data-owner-panel-content=\"github-owner-przemyslawklys\"", "github wrapped owner panel");
    }

    private static void TestGitHubWrappedCardHtmlRendererBuildsCompactCard() {
        var section = CreateSampleGitHubOverviewSection();

        var html = GitHubWrappedCardHtmlRenderer.Render(section);
        AssertContainsText(html, "GitHub Wrapped Card", "github wrapped card title");
        AssertContainsText(html, "provider-github.dark.svg", "github wrapped card heatmap image");
        AssertContainsText(html, "EvotecIT/GPOZaurr", "github wrapped card top repository");
        AssertContainsText(html, "8.99K stars", "github wrapped card owner scope");
        AssertContainsText(html, "2026 YTD +1989.1% vs 2025", "github wrapped card year comparison");
    }

    private static void TestUsageTelemetryOverviewHtmlRendererBuildsGitHubOwnerExplorer() {
        var section = CreateSampleGitHubOverviewSection();
        var summary = new UsageSummarySnapshot {
            Metric = UsageSummaryMetric.TotalTokens,
            StartDayUtc = new DateTime(2025, 03, 14),
            EndDayUtc = new DateTime(2026, 03, 12),
            TotalValue = 0m,
            TotalDays = 365,
            ActiveDays = 71,
            PeakDayUtc = new DateTime(2026, 03, 11),
            PeakValue = 18m
        };

        var overview = new UsageTelemetryOverviewDocument(
            title: "Usage Overview",
            subtitle: "@przemyslawklys",
            metric: UsageSummaryMetric.TotalTokens,
            units: "tokens",
            summary: summary,
            cards: Array.Empty<UsageTelemetryOverviewCard>(),
            heatmaps: Array.Empty<UsageTelemetryOverviewHeatmap>(),
            providerSections: new[] { section });

        var html = UsageTelemetryOverviewHtmlRenderer.Render(overview);
        AssertContainsText(html, "data-github-lens=\"owners\"", "github main explorer owner lens");
        AssertContainsText(html, "data-github-owner=\"github-owner-evotecit\"", "github main explorer evotecit owner chip");
        AssertContainsText(html, "data-github-owner-content=\"github-owner-przemyslawklys\"", "github main explorer personal owner panel");
        AssertContainsText(html, "Owner lenses available in Impact", "github main explorer summary owner scope pills");
        AssertContainsText(html, "aria-label=\"GitHub owner scope summary\"", "github main summary owner explorer");
        AssertContainsText(html, "data-github-repo-sort=\"forks\"", "github main explorer repo sort forks");
        AssertContainsText(html, "data-github-repo-sort-content=\"health\"", "github main explorer repo sort health");
    }

    private static UsageTelemetryOverviewProviderSection CreateSampleGitHubOverviewSection() {
        return new UsageTelemetryOverviewProviderSection(
            key: "provider-github",
            providerId: "github",
            title: "GitHub",
            subtitle: "@przemyslawklys · 2025-03-14 -> 2026-03-12",
            heatmap: new HeatmapDocument(
                title: "@przemyslawklys on GitHub",
                subtitle: "https://github.com/przemyslawklys",
                palette: HeatmapPalette.ChatGptDark(),
                sections: new[] {
                    new HeatmapSection(
                        "GitHub",
                        null,
                        new[] {
                            new HeatmapDay(new DateTime(2026, 03, 10), 12, level: 3),
                            new HeatmapDay(new DateTime(2026, 03, 11), 18, level: 4)
                        })
                }),
            metrics: new[] {
                new UsageTelemetryOverviewSectionMetric("contributions", "Total contributions", "11.8K", "71 active day(s)", 1d, "#216e39")
            },
            composition: null,
            spotlightCards: new[] {
                new UsageTelemetryOverviewCard("most-active-month", "Most Active Month", "July 2025", "7.6K contributions"),
                new UsageTelemetryOverviewCard("longest-streak", "Longest Streak", "204 days", "Current: 204 days")
            },
            inputTokens: 0,
            outputTokens: 0,
            totalTokens: 0,
            monthlyUsageTitle: "Monthly contributions",
            monthlyUsageUnitsLabel: "contributions",
            monthlyUsage: new[] {
                new UsageTelemetryOverviewMonthlyUsage(new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc), 4150, 3),
                new UsageTelemetryOverviewMonthlyUsage(new DateTime(2026, 01, 1, 0, 0, 0, DateTimeKind.Utc), 400_600_000, 24)
            },
            additionalInsights: new[] {
                new UsageTelemetryOverviewInsightSection(
                    "github-year-comparison",
                    "Year over year",
                    "2026 YTD +1989.1% vs 2025",
                    "Compared through 03-12; current year is year-to-date.",
                    new[] {
                        new UsageTelemetryOverviewInsightRow("2026 YTD", "11.8K contributions", "71 active day(s) · longest streak 204 day(s)", 1d),
                        new UsageTelemetryOverviewInsightRow("2025 YTD", "567 contributions", "19 active day(s) · longest streak 34 day(s)", 0.05d)
                    }),
                new UsageTelemetryOverviewInsightSection(
                    "github-scope-split",
                    "Profile vs owner scope",
                    "8.99K stars across selected scope",
                    "Personal profile activity and owned-repository impact are tracked separately here.",
                    new[] {
                        new UsageTelemetryOverviewInsightRow("Personal scope", "6 stars", "przemyslawklys · 4 repo(s) · 1 forks", 0.001d),
                        new UsageTelemetryOverviewInsightRow("Org / owner scope", "8.99K stars", "EvotecIT · 114 repo(s) · 1.23K forks", 0.999d)
                    }),
                new UsageTelemetryOverviewInsightSection(
                    "github-recent-repositories",
                    "Recent repository activity",
                    "EvotecIT/IntelligenceX",
                    "Public repositories sorted by latest push timestamp across the selected owner scope.",
                    new[] {
                        new UsageTelemetryOverviewInsightRow("EvotecIT/IntelligenceX", "2026-03-12", "Rising · 120 stars · 20 forks · PowerShell", 1d, "https://github.com/EvotecIT/IntelligenceX"),
                        new UsageTelemetryOverviewInsightRow("EvotecIT/OfficeIMO", "2026-02-20", "Established · 80 stars · 10 forks · C#", 0.85d, "https://github.com/EvotecIT/OfficeIMO")
                    }),
                new UsageTelemetryOverviewInsightSection(
                    "github-top-languages",
                    "Top languages",
                    "PowerShell",
                    "Ranked by stars across the selected owner scope.",
                    new[] {
                        new UsageTelemetryOverviewInsightRow("PowerShell", "7.06K stars", "68 repo(s) · 921 forks", 1d),
                        new UsageTelemetryOverviewInsightRow("C#", "1.64K stars", "14 repo(s) · 217 forks", 0.23d)
                    }),
                new UsageTelemetryOverviewInsightSection(
                    "github-top-repositories",
                    "Top repositories",
                    "EvotecIT/GPOZaurr",
                    "Ranked by stars across the selected owner scope",
                    new[] {
                        new UsageTelemetryOverviewInsightRow("EvotecIT/GPOZaurr", "533 stars", "72 forks · PowerShell", 1d, "https://github.com/EvotecIT/GPOZaurr"),
                        new UsageTelemetryOverviewInsightRow("EvotecIT/PSWriteHTML", "410 stars", "61 forks · PowerShell", 0.76d, "https://github.com/EvotecIT/PSWriteHTML")
                    }),
                new UsageTelemetryOverviewInsightSection(
                    "github-top-repositories-forks",
                    "Top repositories by forks",
                    "EvotecIT/GPOZaurr",
                    "Ranked by forks across the selected owner scope",
                    new[] {
                        new UsageTelemetryOverviewInsightRow("EvotecIT/GPOZaurr", "72 forks", "533 stars · PowerShell · pushed 2026-03-12", 1d, "https://github.com/EvotecIT/GPOZaurr"),
                        new UsageTelemetryOverviewInsightRow("EvotecIT/PSWriteHTML", "61 forks", "410 stars · PowerShell · pushed 2026-02-28", 0.85d, "https://github.com/EvotecIT/PSWriteHTML")
                    }),
                new UsageTelemetryOverviewInsightSection(
                    "github-top-repositories-health",
                    "Top repositories by health",
                    "EvotecIT/IntelligenceX",
                    "Ranked by recency plus repository impact across the selected owner scope",
                    new[] {
                        new UsageTelemetryOverviewInsightRow("EvotecIT/IntelligenceX", "Rising", "120 stars · 20 forks · pushed 2026-03-12", 1d, "https://github.com/EvotecIT/IntelligenceX"),
                        new UsageTelemetryOverviewInsightRow("EvotecIT/GPOZaurr", "Established", "533 stars · 72 forks · pushed 2026-03-12", 0.91d, "https://github.com/EvotecIT/GPOZaurr")
                    }),
                new UsageTelemetryOverviewInsightSection(
                    "github-owner-przemyslawklys",
                    "przemyslawklys",
                    "6 stars · 1 forks",
                    "4 public repo(s) in this owner scope",
                    new[] {
                        new UsageTelemetryOverviewInsightRow("przemyslawklys/ExampleRepo", "6 stars", "1 forks · PowerShell · pushed 2026-03-01", 1d, "https://github.com/przemyslawklys/ExampleRepo")
                    }),
                new UsageTelemetryOverviewInsightSection(
                    "github-owner-evotecit",
                    "EvotecIT",
                    "8.99K stars · 1.23K forks",
                    "114 public repo(s) in this owner scope",
                    new[] {
                        new UsageTelemetryOverviewInsightRow("EvotecIT/GPOZaurr", "533 stars", "72 forks · PowerShell · pushed 2026-03-12", 1d, "https://github.com/EvotecIT/GPOZaurr"),
                        new UsageTelemetryOverviewInsightRow("EvotecIT/PSWriteHTML", "410 stars", "61 forks · PowerShell · pushed 2026-02-28", 0.76d, "https://github.com/EvotecIT/PSWriteHTML")
                    })
            },
            topModels: Array.Empty<UsageTelemetryOverviewTopModel>(),
            apiCostEstimate: null,
            mostUsedModel: null,
            recentModel: null,
            longestStreakDays: 204,
            currentStreakDays: 204,
            note: "Owner scope: EvotecIT, przemyslawklys · 8.99K stars across 118 public repo(s)");
    }
}
