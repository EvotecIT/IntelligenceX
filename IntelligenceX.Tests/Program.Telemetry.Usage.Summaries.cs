using System;
using System.Linq;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestUsageSummaryBuilderCalculatesTotalsPeakAndRollingWindows() {
        var builder = new UsageSummaryBuilder();
        var aggregates = new[] {
            new UsageDailyAggregateRecord(new DateTime(2026, 03, 01)) {
                ProviderId = "codex",
                TotalTokens = 100,
                EventCount = 1
            },
            new UsageDailyAggregateRecord(new DateTime(2026, 03, 02)) {
                ProviderId = "codex",
                TotalTokens = 0,
                EventCount = 0
            },
            new UsageDailyAggregateRecord(new DateTime(2026, 03, 03)) {
                ProviderId = "codex",
                TotalTokens = 250,
                EventCount = 2
            },
            new UsageDailyAggregateRecord(new DateTime(2026, 03, 04)) {
                ProviderId = "codex",
                TotalTokens = 50,
                EventCount = 1
            }
        };

        var summary = builder.Build(
            aggregates,
            new UsageSummaryOptions {
                Metric = UsageSummaryMetric.TotalTokens,
                RollingWindowDays = new[] { 2, 7 }
            });

        AssertEqual(UsageSummaryMetric.TotalTokens, summary.Metric, "summary metric");
        AssertEqual(new DateTime(2026, 03, 01), summary.StartDayUtc, "summary start");
        AssertEqual(new DateTime(2026, 03, 04), summary.EndDayUtc, "summary end");
        AssertEqual(400m, summary.TotalValue, "summary total");
        AssertEqual(4, summary.TotalDays, "summary total days");
        AssertEqual(3, summary.ActiveDays, "summary active days");
        AssertEqual(100m, summary.AveragePerCalendarDay, "summary calendar average");
        AssertEqual(400m / 3m, summary.AveragePerActiveDay, "summary active average");
        AssertEqual(new DateTime(2026, 03, 03), summary.PeakDayUtc, "summary peak day");
        AssertEqual(250m, summary.PeakValue, "summary peak value");
        AssertEqual(2, summary.RollingWindows.Count, "summary rolling window count");

        var rolling2 = summary.RollingWindows.Single(window => window.WindowDays == 2);
        AssertEqual(new DateTime(2026, 03, 03), rolling2.StartDayUtc, "rolling 2 start");
        AssertEqual(new DateTime(2026, 03, 04), rolling2.EndDayUtc, "rolling 2 end");
        AssertEqual(2, rolling2.DaysCovered, "rolling 2 days covered");
        AssertEqual(300m, rolling2.TotalValue, "rolling 2 total");
        AssertEqual(150m, rolling2.AveragePerCalendarDay, "rolling 2 average");

        var rolling7 = summary.RollingWindows.Single(window => window.WindowDays == 7);
        AssertEqual(new DateTime(2026, 03, 01), rolling7.StartDayUtc, "rolling 7 clipped start");
        AssertEqual(4, rolling7.DaysCovered, "rolling 7 clipped days covered");
        AssertEqual(400m, rolling7.TotalValue, "rolling 7 total");
    }

    private static void TestUsageSummaryBuilderBuildsTopBreakdowns() {
        var builder = new UsageSummaryBuilder();
        var aggregates = new[] {
            new UsageDailyAggregateRecord(new DateTime(2026, 03, 10)) {
                ProviderId = "codex",
                AccountKey = "acct:work",
                PersonLabel = "Przemek",
                Model = "gpt-5.4-codex",
                Surface = "reviewer",
                TotalTokens = 500
            },
            new UsageDailyAggregateRecord(new DateTime(2026, 03, 10)) {
                ProviderId = "claude",
                AccountKey = "acct:personal",
                PersonLabel = "Przemek",
                Model = "claude-sonnet-4-5",
                Surface = "chat",
                TotalTokens = 300
            },
            new UsageDailyAggregateRecord(new DateTime(2026, 03, 11)) {
                ProviderId = "codex",
                AccountKey = "acct:work",
                PersonLabel = "Przemek",
                Model = "gpt-5.4-codex",
                Surface = "chat",
                TotalTokens = 200
            }
        };

        var summary = builder.Build(
            aggregates,
            new UsageSummaryOptions {
                Metric = UsageSummaryMetric.TotalTokens,
                BreakdownLimit = 2,
                RollingWindowDays = Array.Empty<int>()
            });

        AssertEqual(2, summary.ProviderBreakdown.Count, "provider breakdown count");
        AssertEqual("codex", summary.ProviderBreakdown[0].Key, "top provider key");
        AssertEqual(700m, summary.ProviderBreakdown[0].Value, "top provider value");
        AssertEqual("claude", summary.ProviderBreakdown[1].Key, "second provider key");

        AssertEqual(2, summary.AccountBreakdown.Count, "account breakdown count");
        AssertEqual("acct:work", summary.AccountBreakdown[0].Key, "top account key");
        AssertEqual(700m, summary.AccountBreakdown[0].Value, "top account value");

        AssertEqual(2, summary.ModelBreakdown.Count, "model breakdown count");
        AssertEqual("gpt-5.4-codex", summary.ModelBreakdown[0].Key, "top model key");
        AssertEqual(700m, summary.ModelBreakdown[0].Value, "top model value");

        AssertEqual(1, summary.PersonBreakdown.Count, "person breakdown count");
        AssertEqual("Przemek", summary.PersonBreakdown[0].Key, "top person key");
        AssertEqual(1000m, summary.PersonBreakdown[0].Value, "top person value");

        AssertEqual(2, summary.SurfaceBreakdown.Count, "surface breakdown count");
        AssertEqual("chat", summary.SurfaceBreakdown[0].Key, "top surface key");
        AssertEqual(500m, summary.SurfaceBreakdown[0].Value, "top surface value");
        AssertEqual("reviewer", summary.SurfaceBreakdown[1].Key, "second surface key");
        AssertEqual(500m, summary.SurfaceBreakdown[1].Value, "second surface value");
    }
}
